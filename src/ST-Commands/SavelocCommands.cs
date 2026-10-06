using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

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
			saveloc = Saveloc.Capture(session.NextId, p, pawn, p, p.Timer.IsRunning ? SavelocSource.Run : SavelocSource.Unrun, null, p.CourseBonus);
		}
		else if (ObservedPawn(controller) is { } target)
		{
			var targetPawn = new CCSPlayerPawn(target.Handle);
			var targetController = targetPawn.Controller.Value;
			var slot = map.ReplayManager?.Pool.Find(s => s.Controller != null && s.Controller.IsValid && s.Controller.PlayerPawn.Raw == targetPawn.EntityHandle.Raw);
			if (slot != null)
			{
				saveloc = Saveloc.Capture(session.NextId, p, targetPawn, null, SavelocSource.Replay, slot.RecordPlayerName,
					slot.Type == 1 ? (short)slot.Stage : (short)0);
			}
			else if (targetController != null && playerList.TryGetValue(new CCSPlayerController(targetController.Handle).UserId ?? 0, out var watched))
			{
				saveloc = Saveloc.Capture(session.NextId, p, targetPawn, watched, SavelocSource.Player, watched.Controller.PlayerName, watched.CourseBonus);
			}
		}

		if (saveloc == null)
		{
			controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_nothing"]}");
			return;
		}

		session.TakeId();
		session.Add(saveloc);

		string message = $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_saved", controller.PlayerName, saveloc.Id, saveloc.Describe()]}";
		controller.PrintToChat(message);
		foreach (var spectator in playerList.Values)
		{
			if (!ReferenceEquals(spectator, p) && spectator.Controller.IsValid && spectator.IsSpectating(controller))
				spectator.Controller.PrintToChat(message);
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

		// One message per burst: loads within SavelocMessageQuietSeconds of the previous one stay silent
		int now = Server.TickCount;
		bool quiet = now - p.LastSavelocLoadTick < SavelocMessageQuietSeconds * 64;
		p.LastSavelocLoadTick = now;
		if (!quiet && p.Options.ChatSaveloc)
			controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_teleported", saveloc.Id, saveloc.Describe()]}");
	}

	private const int SavelocMessageQuietSeconds = 5;
}
