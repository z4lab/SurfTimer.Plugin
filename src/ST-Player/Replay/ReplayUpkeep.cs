using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Replay pool upkeep, once per second: requested bots nobody spectates are removed after
/// Config.ReplayUnwatchedTimeoutSeconds, slots whose bot never arrived are dropped, and the permanent
/// map WR bot (Config.ReplayPermanentMapBot) is kept in place.
/// </summary>
public partial class SurfTimer
{
	private const int ReplaySpawnTimeoutSeconds = 15;

	// The permanent bot's watchdog: how long it may stay not-playing before it's restarted, and the
	// pause between restarts (a respawn takes ~1.5 s to start playing)
	private const int PermanentStallSeconds = 3;
	private const int PermanentRestartCooldownSeconds = 5;
	private DateTime? _permanentStalledSince;
	private DateTime _permanentLastRestart;

	// The permanent bot is created this long after a player is first alive on a team (map load)
	private const int PermanentJoinDelaySeconds = 3;
	private DateTime? _permanentHumanSince;

	private void TickReplayUpkeep()
	{
		var manager = CurrentMap?.ReplayManager;
		if (manager == null)
			return;

		var now = DateTime.UtcNow;
		var pool = manager.Pool;

		// Pawns someone is spectating right now
		var watched = new HashSet<uint>();
		foreach (var player in playerList.Values)
		{
			var controller = player.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;

			var target = controller.ObserverPawn.Value?.ObserverServices?.ObserverTarget.Value;
			if (target != null && target.IsValid)
				watched.Add(target.Index);
		}

		// Backwards - KickReplayBot removes from the pool by index
		for (int i = pool.Count - 1; i >= 0; i--)
		{
			var slot = pool[i];

			if (slot.Controller == null)
			{
				// The bot never arrived (refused by the server) - free the slot
				if ((now - slot.CreatedAt).TotalSeconds > ReplaySpawnTimeoutSeconds)
					pool.RemoveAt(i);
				continue;
			}

			var pawn = slot.Controller.PlayerPawn.Value;
			if (pawn != null && pawn.IsValid && watched.Contains(pawn.Index))
				slot.LastWatchedAt = now;

			if (slot.IsPermanent)
				continue;

			if ((now - slot.LastWatchedAt).TotalSeconds > Config.ReplayUnwatchedTimeoutSeconds)
				CurrentMap!.KickReplayBot(i);
		}

		UpdatePermanentReplayBot();
	}

	/// <summary>
	/// Keeps the permanent map WR bot in line with the setting and the current map WR: spawns it,
	/// switches it to a new WR, or removes it (turned off / no WR).
	/// </summary>
	internal void UpdatePermanentReplayBot()
	{
		var manager = CurrentMap?.ReplayManager;
		if (manager == null)
			return;

		var permanent = manager.PermanentSlot;
		var wr = manager.MapWR;
		bool wanted = Config.ReplayPermanentMapBot && Config.ReplaysEnabled
			&& wr != null && wr.IsPlayable && wr.Frames.Count > 0 && wr.MapTimeID != -1;

		if (!wanted)
		{
			if (permanent != null)
			{
				int index = manager.Pool.IndexOf(permanent);
				if (permanent.Controller != null)
					CurrentMap!.KickReplayBot(index);
				else
					manager.Pool.RemoveAt(index);
			}
			return;
		}

		if (permanent == null)
		{
			// On map load the WR is ready before anyone is in. A bot created then either can't join
			// (bot_join_after_player) or is kicked by the round restart CS2 does when the first player
			// joins ("BeginMatch") - so wait until a player is alive on a team, and a moment longer
			bool humanPlaying = playerList.Values.Any(p => p.Controller.IsValid && !p.Controller.IsBot
				&& p.Controller.PawnIsAlive && p.Controller.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist);
			if (!humanPlaying)
			{
				_permanentHumanSince = null;
				return;
			}
			_permanentHumanSince ??= DateTime.UtcNow;
			if ((DateTime.UtcNow - _permanentHumanSince.Value).TotalSeconds < PermanentJoinDelaySeconds)
				return;

			var slot = new ReplayPlayer { IsPermanent = true };
			slot.LoadContentFrom(wr!);
			manager.Pool.Add(slot);
			_permanentStalledSince = null;
			_logger.LogInformation("[Replay] Permanent map bot: spawning for the map WR ({Player}, time {TimeId})", wr!.RecordPlayerName, wr.MapTimeID);
			SpawnReplayBotDirectly(slot);
			return;
		}

		// A new map WR - the bot switches to it right away
		if (permanent.Controller != null && permanent.MapTimeID != wr!.MapTimeID)
		{
			_logger.LogInformation("[Replay] Permanent map bot: new map WR ({Player}, time {TimeId})", wr.RecordPlayerName, wr.MapTimeID);
			permanent.LoadContentFrom(wr);
			permanent.LoadReplayData(-1);
			if (permanent.Controller.PawnIsAlive)
			{
				permanent.Start();
				permanent.FormatBotName();
			}
			else
			{
				RestartIdleReplayBot(permanent);
			}
			return;
		}

		// Watchdog: a permanent bot that exists but isn't playing (dead, moved to spectator by a round
		// restart, never started) is respawned and started again
		var controller = permanent.Controller;
		if (controller == null || !controller.IsValid)
			return;

		bool playing = permanent.IsPlaying && controller.PawnIsAlive
			&& controller.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist;
		if (playing)
		{
			_permanentStalledSince = null;
			return;
		}

		var now = DateTime.UtcNow;
		_permanentStalledSince ??= now;
		if ((now - _permanentStalledSince.Value).TotalSeconds < PermanentStallSeconds
			|| (now - _permanentLastRestart).TotalSeconds < PermanentRestartCooldownSeconds)
			return;

		_logger.LogInformation("[Replay] Permanent map bot isn't playing (playing {Playing}, alive {Alive}, team {Team}) - restarting it",
			permanent.IsPlaying, controller.PawnIsAlive, controller.Team);
		_permanentLastRestart = now;
		_permanentStalledSince = null;
		permanent.LoadReplayData(-1);
		RestartIdleReplayBot(permanent);
	}
}
