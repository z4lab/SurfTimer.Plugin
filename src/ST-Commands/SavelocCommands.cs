using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Savelocs, KSF style: !saveloc gives a server-wide #id (per map session), !tele [#id] loads one (someone
/// else's id joins their set), !teleprev / !telenext step through your set, !saveloclist / !slm is the menu.
/// Savelocs can be made outside a run and of a spectated player / replay bot. See Saveloc.cs.
/// </summary>
public partial class SurfTimer
{
	[ConsoleCommand("css_saveloc", "Save your location (or the spectated player's / bot's) as a #id")]
	[ConsoleCommand("css_sl", "Save your location (or the spectated player's / bot's) as a #id")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void SavelocCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null && playerList.TryGetValue(player.UserId ?? 0, out var p))
			SaveLocation(p);
	}

	[ConsoleCommand("css_tele", "Back to your current saveloc, or load saveloc #id")]
	[ConsoleCommand("css_tp", "Back to your current saveloc, or load saveloc #id")]
	[CommandHelper(usage: "[#id]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void TeleCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var p))
			return;

		if (command.ArgCount < 2)
		{
			TeleToCurrent(p);
			return;
		}

		string arg = command.GetArg(1).Trim().TrimStart('#');
		if (!int.TryParse(arg, out int id) || id < 1)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_unknown", arg]}");
			return;
		}
		TeleToId(p, id);
	}

	[ConsoleCommand("css_teleprev", "Previous saveloc of your set")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void TelePrevCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null && playerList.TryGetValue(player.UserId ?? 0, out var p))
			TeleStep(p, -1);
	}

	[ConsoleCommand("css_telenext", "Next saveloc of your set")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void TeleNextCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null && playerList.TryGetValue(player.UserId ?? 0, out var p))
			TeleStep(p, 1);
	}

	// ---- Saving ----

	/// <summary>
	/// A new saveloc: of your own pawn while alive (with your run when the timer runs), else of the player /
	/// replay bot you spectate. Told to you and your spectators.
	/// </summary>
	internal void SaveLocation(Player p)
	{
		var map = CurrentMap;
		var controller = p.Controller;
		if (map == null)
			return;

		var session = map.Savelocs;
		if (session.All.Count >= Config.SavelocLimit)
		{
			controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_limit", Config.SavelocLimit]}");
			return;
		}

		Saveloc? saveloc = null;
		if (controller.PawnIsAlive && controller.PlayerPawn.Value is { } pawn)
		{
			var startZone = ResettingStartZone(p);
			if (startZone != null && !IsStandingStill(pawn))
			{
				controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_start_not_still"]}");
				return;
			}
			saveloc = Saveloc.Capture(session.NextId, p, pawn, p, p.Timer.IsRunning ? SavelocSource.Run : SavelocSource.Unrun, null, p.CourseBonus,
				startZone);
		}
		else if (ObservedPawn(controller) is { } target)
		{
			var targetPawn = new CCSPlayerPawn(target.Handle);
			var targetController = targetPawn.Controller.Value;
			var slot = map.ReplayManager?.Pool.Find(s => s.Controller != null && s.Controller.IsValid && s.Controller.PlayerPawn.Raw == targetPawn.EntityHandle.Raw);
			if (slot != null)
			{
				SaveReplayLocation(p, slot);
				return;
			}
			else if (targetController != null && playerList.TryGetValue(new CCSPlayerController(targetController.Handle).UserId ?? 0, out var watched))
			{
				var startZone = ResettingStartZone(watched);
				if (startZone != null && !IsStandingStill(targetPawn))
				{
					controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_start_not_still"]}");
					return;
				}
				saveloc = Saveloc.Capture(session.NextId, p, targetPawn, watched, SavelocSource.Player, watched.Controller.PlayerName, watched.CourseBonus,
					startZone);
			}
		}

		if (saveloc == null)
		{
			controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_nothing"]}");
			return;
		}

		session.TakeId();
		AnnounceSaveloc(p, saveloc);
	}

	/// <summary>Adds a new saveloc to the owner's set and tells them and their spectators</summary>
	private void AnnounceSaveloc(Player p, Saveloc saveloc)
	{
		var controller = p.Controller;
		CurrentMap!.Savelocs.Add(saveloc);

		string message = $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_saved", controller.PlayerName, saveloc.Id, saveloc.Describe()]}";
		controller.PrintToChat(message);
		foreach (var spectator in playerList.Values)
		{
			if (!ReferenceEquals(spectator, p) && spectator.Controller.IsValid && spectator.IsSpectating(controller))
				spectator.Controller.PrintToChat(message);
		}
	}

	/// <summary>
	/// A saveloc of the replay bot being spectated, with the bot's run so far: time, stage / checkpoint and the
	/// record's splits up to the frame shown. Map WR splits are in memory; a PB replay's load first.
	/// </summary>
	private void SaveReplayLocation(Player p, ReplayPlayer slot)
	{
		var map = CurrentMap!;
		int frame = slot.PlayedFrameIndex;
		var template = new ReplayPlayer
		{
			Type = slot.Type,
			Stage = slot.Stage,
			Style = slot.Style,
			Frames = slot.Frames,
		};
		string holder = slot.RecordPlayerName ?? "?";

		// In a start zone that starts the bot's kind of run: only when the recorded player stood still there
		var startZone = ReplayStartZone(slot, frame);
		if (startZone != null && !ReplayFrameIsStill(slot.Frames, frame))
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_start_not_still"]}");
			return;
		}

		void Create(Dictionary<int, CheckpointEntity>? splits)
		{
			if (CurrentMap != map || !p.Controller.IsValid)
				return;
			var saveloc = Saveloc.FromReplayFrame(map.Savelocs.NextId, p, template, frame, splits, holder, startZone);
			if (saveloc == null)
			{
				p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_nothing"]}");
				return;
			}
			map.Savelocs.TakeId();
			AnnounceSaveloc(p, saveloc);
		}

		// Map runs carry splits - the loaded map WR's, or the PB's from the database
		var wr = map.WR.GetValueOrDefault(slot.Style);
		if (startZone != null || slot.Type != 0 || slot.MapTimeID <= 0)
		{
			Create(null);
		}
		else if (wr != null && wr.ID == slot.MapTimeID)
		{
			Create(wr.Checkpoints);
		}
		else
		{
			int timeId = slot.MapTimeID;
			Task.Run(async () =>
			{
				Dictionary<int, CheckpointEntity>? splits = null;
				try
				{
					splits = await TimeRepository.GetSplitsAsync(timeId);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "[Saveloc] Loading the splits of time {TimeId} failed - saved without splits", timeId);
				}
				Server.NextFrame(() => Create(splits));
			});
		}
	}

	// ---- Loading ----

	internal void TeleToCurrent(Player p)
	{
		var session = CurrentMap?.Savelocs;
		var set = session?.SetOf(p.Controller.SteamID);
		if (session == null || set?.Current is not int id || !session.All.TryGetValue(id, out var saveloc))
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_no_locations"]}");
			return;
		}
		LoadSaveloc(p, saveloc);
	}

	internal void TeleToId(Player p, int id)
	{
		if (!CanLoadSaveloc(p))
			return; // Before the set / cursor changes
		var session = CurrentMap?.Savelocs;
		if (session == null || !session.Select(p.Controller.SteamID, id))
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_unknown", id]}");
			return;
		}
		TeleToCurrent(p);
	}

	internal void TeleStep(Player p, int direction)
	{
		if (!CanLoadSaveloc(p))
			return; // Before the set / cursor changes
		var session = CurrentMap?.Savelocs;
		var set = session?.SetOf(p.Controller.SteamID);
		if (session == null || set == null || set.Ids.Count == 0)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_no_locations"]}");
			return;
		}

		int next = set.Cursor + direction;
		if (next < 0 || next >= set.Ids.Count)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull[direction < 0 ? "saveloc_first" : "saveloc_last"]}");
			return;
		}

		set.Cursor = next;
		TeleToCurrent(p);
	}

	/// <summary>Spectators / dead players can't load savelocs (they can still save them)</summary>
	private static bool CanLoadSaveloc(Player p)
	{
		if (p.Controller.PawnIsAlive)
			return true;
		p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_spectating"]}");
		return false;
	}

	/// <summary>Puts an alive player into a saveloc - refused while spectating or dead</summary>
	private void LoadSaveloc(Player p, Saveloc saveloc)
	{
		var controller = p.Controller;
		if (p.ReplayRecorder.IsSaving)
		{
			controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["reset_delay"]}");
			return;
		}

		// Spectators can save locations (of who they watch) but must join a team before loading one
		if (!CanLoadSaveloc(p))
			return;

		ClaimPlacement(p); // Puts the player somewhere - a saved run not restored yet is given up (RunResume.cs)
		ApplySaveloc(p, saveloc);
	}

	private void ApplySaveloc(Player p, Saveloc saveloc)
	{
		var controller = p.Controller;
		var pawn = controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;

		p.CourseBonus = saveloc.CourseBonus; // Locked to the saveloc's course
		if (saveloc.Run != null)
		{
			saveloc.Run.Apply(p);
			p.ReplayRecorder.DropForRun(); // Practice - nothing of this run is recorded or saved
		}
		else
		{
			p.Timer.Reset();
			p.Stats.ThisRun.Checkpoints.Clear();
		}

		saveloc.ApplyPawn(pawn);

		// Landing inside a zone isn't entering it (no stray start / stop) - only moving out of / into one counts
		// (now, as the teleport moved the pawn, and next frame in case the position settles a frame later)
		ResyncZoneTouches(p);
		Server.NextFrame(() => ResyncZoneTouches(p));

		// Saved standing in a start zone: entered like walking in, so leaving starts that zone's run
		if (saveloc.StartZone is var (type, number))
			EnterStartZoneFromSaveloc(p, type, number);

		// One message per burst: loads within SavelocMessageQuietSeconds of the previous one stay silent
		int now = Server.TickCount;
		bool quiet = now - p.LastSavelocLoadTick < SavelocMessageQuietSeconds * 64;
		p.LastSavelocLoadTick = now;
		if (!quiet && p.Options.ChatSaveloc)
			controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_teleported", saveloc.Id, saveloc.Describe()]}");
	}

	private const int SavelocMessageQuietSeconds = 5;
	private const float SavelocStillSpeed = 5f;

	/// <summary>
	/// The start zone a player stands in where a run starts when leaving it - map start, bonus start, or a
	/// stage start in stage mode (passing a stage start during a map run isn't one). Null otherwise.
	/// </summary>
	private static (ZoneType Type, short Number)? ResettingStartZone(Player player)
	{
		foreach (var zone in player.TouchingTriggers.Values)
		{
			if (zone.Type is ZoneType.MapStart or ZoneType.BonusStart
				|| (zone.Type == ZoneType.StageStart && player.Timer.IsStageMode && player.Timer.Stage == zone.Number))
				return (zone.Type, zone.Number);
		}
		return null;
	}

	/// <summary>Like !startpos: on the ground, not crouched, (almost) not moving</summary>
	private static bool IsStandingStill(CCSPlayerPawn pawn)
	{
		bool onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
		bool crouched = (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0;
		return onGround && !crouched && pawn.AbsVelocity.ToVector_t().velMag() <= SavelocStillSpeed;
	}

	/// <summary>The run-starting start zone a replay frame is in (map / bonus start, a stage replay's own stage start)</summary>
	private static (ZoneType Type, short Number)? ReplayStartZone(ReplayPlayer slot, int frame)
	{
		var map = CurrentMap;
		if (map == null || frame < 0 || frame >= slot.Frames.Count)
			return null;

		var position = slot.Frames[frame].GetPos();
		foreach (var zone in map.ActiveZones)
		{
			bool starts = zone.Type switch
			{
				ZoneType.MapStart => slot.Type is 0 or 2,
				ZoneType.BonusStart => slot.Type == 1 && zone.Number == slot.Stage,
				ZoneType.StageStart => slot.Type == 2 && zone.Number == slot.Stage,
				_ => false,
			};
			if (starts && zone.Contains(position))
				return (zone.Type, zone.Number);
		}
		return null;
	}

	/// <summary>A recorded frame on the ground, not crouched and (almost) not moving</summary>
	private static bool ReplayFrameIsStill(List<ReplayFrame> frames, int frame)
	{
		var current = frames[frame];
		var next = frames[Math.Min(frame + 1, frames.Count - 1)].GetPos();
		float speed = ((next - current.GetPos()) * 64).velMag();
		return (current.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0 && (current.Flags & (uint)PlayerFlags.FL_DUCKING) == 0
			&& speed <= SavelocStillSpeed;
	}

	/// <summary>
	/// A start zone saveloc was loaded: the player enters that zone like walking in (timer reset, the run's
	/// mode set), so leaving it starts that run - map, bonus N, or stage N in stage mode.
	/// </summary>
	private void EnterStartZoneFromSaveloc(Player p, ZoneType type, short number)
	{
		var map = CurrentMap;
		var zone = map?.FindNearestZone(type, number, p.Controller.PlayerPawn.Value?.AbsOrigin?.ToVector_t());
		if (zone == null)
			return;

		if (type == ZoneType.StageStart)
		{
			p.Timer.IsStageMode = true; // The stage start handler resets into a stage-mode run of this stage
			p.Timer.Stage = number;
		}
		p.TouchingTriggers.Remove(zone.ZoneId);
		HandleZoneEnter(p, zone);
	}
}
