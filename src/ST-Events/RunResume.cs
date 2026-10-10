using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// Where players are put: their first spawn on a map (their saved run restored, else the map start - like !r once),
/// leaving spectator (back to their stage / bonus, or the paused run goes on like !back), and commands used from
/// spectator. A respawn puts the pawn at the map's spawn point after a teleport in the same frame and that spot is
/// usually inside the map start - so while a player is placed, zones are ignored and the timer doesn't count
/// (Player.IsPlacementPending), and the teleport waits until the respawn has settled.
/// </summary>
public partial class SurfTimer
{
	/// <summary>Zones count again this long after the placing teleport (it runs a frame later)</summary>
	private const int PlacementSettleTicks = 4;
	/// <summary>A respawn that never happens stops waiting after this</summary>
	private const int PlacementTimeoutTicks = 64 * 3;
	/// <summary>The first spawn waits this long at most for the map and the saved run to load</summary>
	private const int FirstSpawnAttempts = 50;
	private const float FirstSpawnPollSeconds = 0.2f;

	/// <summary>Zones count again shortly - the placing teleport has landed by then</summary>
	private static void ReleasePlacement(Player p) => p.PlacementPendingUntilTick = Server.TickCount + PlacementSettleTicks;

	/// <summary>
	/// A command puts the player somewhere now: waiting placements (first spawn, leaving spectator) are cancelled, and a
	/// saved run that wasn't restored yet is given up - the player starts over.
	/// </summary>
	private static void ClaimPlacement(Player p)
	{
		p.PlacementToken++;
		p.PlacementPendingUntilTick = 0;
		p.SpecReturn = null;
		p.FirstSpawnHandled = true;
		if (!p.RunState.Settled)
			PlayerStateService.Discard(p, "started over");
	}

	/// <summary>
	/// Runs place now - or, for a spectator, after joining CT and respawning (the spawn point would win over a teleport
	/// in the same frame, and its start zone would reset the run).
	/// </summary>
	private void RespawnThenPlace(Player p, Action place)
	{
		var controller = p.Controller;
		if (controller.Team != CsTeam.Spectator && controller.Team != CsTeam.None)
		{
			place();
			return;
		}

		int token = ++p.PlacementToken;
		p.PlacementPendingUntilTick = Server.TickCount + PlacementTimeoutTicks;
		Server.NextFrame(() =>
		{
			if (controller.IsValid && p.PlacementToken == token)
				RejoinCounterTerrorist(controller);
		});
		PlaceWhenSpawned(p, token, place, attempts: 20);
	}

	/// <summary>Weird CS2 bug that requires doing this twice to show the Joined X team in chat and not stay in limbo</summary>
	private static void RejoinCounterTerrorist(CCSPlayerController controller)
	{
		controller.ChangeTeam(CsTeam.CounterTerrorist);
		controller.Respawn();

		controller.ChangeTeam(CsTeam.Spectator);

		controller.ChangeTeam(CsTeam.CounterTerrorist);
		controller.Respawn();
	}

	/// <summary>Places the player once their pawn has been alive for a moment (the respawn has settled)</summary>
	private void PlaceWhenSpawned(Player p, int token, Action place, int attempts, bool aliveBefore = false)
	{
		AddTimer(0.1f, () =>
		{
			var controller = p.Controller;
			if (!controller.IsValid || p.PlacementToken != token)
				return;

			bool alive = controller.PawnIsAlive;
			if ((!alive || !aliveBefore) && attempts > 0)
			{
				PlaceWhenSpawned(p, token, place, attempts - 1, alive);
				return;
			}

			if (alive && CurrentMap != null)
				place();
			ReleasePlacement(p);
		}, TimerFlags.STOP_ON_MAPCHANGE);
	}

	// ---- First spawn: resume the saved run, else the map start ----

	/// <summary>OnPlayerSpawn: the player's first spawn on this map (also after a reconnect / map change)</summary>
	private void BeginFirstSpawn(Player p)
	{
		if (p.FirstSpawnHandled || p.IsPlacementPending || p.Controller.IsBot)
			return;

		p.FirstSpawnHandled = true;
		int token = ++p.PlacementToken;
		p.PlacementPendingUntilTick = Server.TickCount + (int)(FirstSpawnAttempts * FirstSpawnPollSeconds * 64) + 64;
		PlaceFirstSpawn(p, token, FirstSpawnAttempts);
	}

	private void PlaceFirstSpawn(Player p, int token, int attempts)
	{
		AddTimer(FirstSpawnPollSeconds, () =>
		{
			var controller = p.Controller;
			if (!controller.IsValid || p.PlacementToken != token)
				return;

			var map = CurrentMap;
			var tracker = p.RunState;
			bool mapReady = map != null && map.ID > 0;
			if (mapReady)
				PlayerStateService.BeginLoad(p); // Once
			bool waiting = !mapReady || tracker.LoadStage != 2;
			if (waiting && attempts > 0)
			{
				PlaceFirstSpawn(p, token, attempts - 1);
				return;
			}

			// Went to spectator meanwhile: their next spawn is placed instead
			if (!controller.PawnIsAlive)
			{
				p.FirstSpawnHandled = false;
				p.PlacementPendingUntilTick = 0;
				return;
			}

			var candidate = tracker.Loaded;
			tracker.Loaded = null;
			if (waiting || tracker.LoadFailed || candidate == null || map == null)
			{
				// Nothing saved - or the map / database too slow: the saved run stays for another time
				tracker.Settled = true;
				if (map != null)
					ResetToMapStart(p);
				ReleasePlacement(p);
				return;
			}

			string? problem = ResumeProblem(p, candidate, map);
			if (problem != null)
			{
				PlayerStateService.Discard(p, problem);
				controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["resume_discarded", problem]}");
				ResetToMapStart(p);
				ReleasePlacement(p);
				return;
			}

			ApplyResume(p, candidate);
		}, TimerFlags.STOP_ON_MAPCHANGE);
	}

	/// <summary>Why a saved run can't be resumed here - null when it can</summary>
	private static string? ResumeProblem(Player p, ResumeCandidate candidate, Map map)
	{
		var state = candidate.State;
		if (state == null || state.TickRate != ReplayCodec.TickRate || !state.IsSane())
			return "it was saved by another version";
		if (!string.Equals(state.MapName, map.Name, StringComparison.OrdinalIgnoreCase))
			return "it belongs to another map";

		int days = IsVip(p.Controller) ? Config.ResumeExpiryVipDays : Config.ResumeExpiryDays;
		if (days > 0 && candidate.AgeSeconds > days * 86400L)
			return days == 1 ? "it's older than 1 day" : $"it's older than {days} days";

		if (state.ZonesHash != map.ZonesHash)
			return "the map's zones changed";
		if (!Config.Styles.Contains(state.Style))
			return "its style isn't available";

		bool courseExists = state.CourseBonus > 0
			? state.CourseBonus <= map.Bonuses && map.HasZone(ZoneType.BonusStart, state.CourseBonus)
			: state.Stage <= map.Stages && state.Checkpoint <= map.TotalCheckpoints;
		return courseExists ? null : "its stage or bonus doesn't exist any more";
	}

	/// <summary>VIPs keep saved runs longer - VIP flag, or admin / root (chat_settings.json flags)</summary>
	private static bool IsVip(CCSPlayerController controller)
	{
		var flags = ChatSettings.Current.Flags;
		return AdminManager.PlayerHasPermissions(controller, flags.Vip)
			|| AdminManager.PlayerHasPermissions(controller, flags.Admin)
			|| AdminManager.PlayerHasPermissions(controller, flags.Root);
	}

	/// <summary>
	/// The saved run continues - like loading a saveloc (SavelocCommands.ApplySaveloc), but it counts: not practice
	/// (unless it was), and its replay so far is back in the recorder. Landing inside zones isn't entering them.
	/// </summary>
	private void ApplyResume(Player p, ResumeCandidate candidate)
	{
		var state = candidate.State!;
		var controller = p.Controller;
		var pawn = controller.PlayerPawn.Value;
		var tracker = p.RunState;
		if (pawn == null || !pawn.IsValid)
		{
			// No pawn after all: kept for the next spawn
			tracker.Loaded = candidate;
			p.FirstSpawnHandled = false;
			p.PlacementPendingUntilTick = 0;
			return;
		}

		p.PlacementPendingUntilTick = 0;
		p.CourseBonus = state.CourseBonus;
		state.ToRun().Apply(p, practice: state.Practice);
		p.Timer.StageFailures = state.StageFailures;
		if (state.Paused)
			p.Timer.Pause();

		// The replay goes on where it was - without the saved frames (only a periodic save survived, e.g. a crash) the
		// run still counts but gets no replay, like a run that was idle
		bool withReplay = !state.ReplayDropped && (candidate.Frames != null || state.FrameCount == 0);
		if (withReplay)
			p.ReplayRecorder.RestoreForRun(candidate.Frames ?? new List<ReplayFrame>(), state);
		else
			p.ReplayRecorder.DropForRun();

		state.ToSaveloc().ApplyPawn(pawn);

		// Landing inside a zone isn't entering it (no stray start / stop) - now and next frame, as with savelocs
		ResyncZoneTouches(p);
		Server.NextFrame(() => ResyncZoneTouches(p));

		tracker.Settled = true;
		tracker.RowExists = true;
		tracker.LastSnapshot = state;

		p.HUD.Notify("Run restored");
		string key = withReplay || state.Practice ? "resume_restored" : "resume_restored_no_replay";
		controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull[key, state.Describe()]}");
	}

	// ---- Spectator ----

	/// <summary>Going to spectator: where to put the player when they come back (the first time counts)</summary>
	private static void RememberSpecReturn(Player p)
	{
		if (p.IsPlacementPending || !p.FirstSpawnHandled)
			return; // The !r workaround passes through spectator, a first spawn isn't placed yet
		p.SpecReturn ??= new SpecReturn(p.CourseBonus, p.Timer.Stage, p.Timer.IsStageMode);
	}

	/// <summary>
	/// OnPlayerTeam: a player joined T / CT from spectator by themselves (M menu, jointeam) - placed once respawned,
	/// before the spawn point's start zone could reset their run.
	/// </summary>
	private void BeginReturnFromSpectator(Player p)
	{
		if (!p.FirstSpawnHandled || p.IsPlacementPending)
			return; // Joining for the first time (first spawn places them), or a command is placing them

		var ret = p.SpecReturn;
		p.SpecReturn = null;
		int token = ++p.PlacementToken;
		p.PlacementPendingUntilTick = Server.TickCount + PlacementTimeoutTicks;
		PlaceWhenSpawned(p, token, () => ReturnFromSpectator(p, ret), attempts: 20);
	}

	/// <summary>
	/// Back from spectator: a paused run (spectator through the M menu - the timer only stops counting) goes on from its
	/// stage / bonus start like !back. Otherwise (!spec ended the run) the bonus, or on staged maps the stage, they were
	/// on - its start in stage mode - else the map start.
	/// </summary>
	private void ReturnFromSpectator(Player p, SpecReturn? ret)
	{
		var map = CurrentMap;
		if (map == null)
			return;

		if (p.Timer.IsRunning)
		{
			ResetToCurrentStart(p);
			return;
		}

		if (ret != null)
		{
			if (ret.CourseBonus > 0 && map.HasZone(ZoneType.BonusStart, ret.CourseBonus))
			{
				EnterBonus(p, ret.CourseBonus);
				return;
			}
			if (map.Stages > 0 && ret.Stage > 1 && map.HasZone(ZoneType.StageStart, ret.Stage))
			{
				TeleportToStage(p.Controller, ret.Stage);
				return;
			}
		}

		ResetToMapStart(p);
	}
}
