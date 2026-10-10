using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	// Tick errors are logged at most once per 5s, so a broken piece shows up without flooding the console
	private int _nextTickErrorLogTick;

	public void OnTick()
	{
		if (CurrentMap == null)
			return;

		// No "Game commencing" match restart when bots / the first player join (see MapCommandFilter.cs)
		KeepGameCommenced();

		// CS2's round timer stands at 13:37 (see RoundTimer.cs)
		TickRoundTimer();

		// Pawns that viewers with a hide option don't receive (see Visibility.cs)
		RefreshTransmitTargets();

		foreach (var player in playerList.Values)
		{
			if (!player.Controller.IsValid)
				continue;

			// One player's failure must not stop the rest of the tick (other players, bot quota,
			// replay playback) - it's logged instead.
			try
			{
				// Zones entered / left since the last tick, before the timer counts it (ZoneTracker.cs)
				TickZones(player);

				// Spectators/dead players have no live PlayerPawn - the timer, recorder and speed cap
				// all read it.
				if (player.Controller.PawnIsAlive)
				{
					player.Timer.Tick();
					player.ReplayRecorder.Tick(player);
					player.TickIdle();
					player.TickPendingStartRecording();
					player.TickStartZoneSpeedCap();
					player.TickRemoveLandingSlowdown();
					player.TickSync();
				}

				player.HUD.Display(playerList.Values);
				player.HUD.TickMenuKeys();

				// Playtime / attempts in batches
				if (Server.TickCount % StatsService.FlushIntervalTicks == 0)
					StatsService.Flush(player, final: false);
			}
			catch (Exception ex)
			{
				LogTickError(ex, $"player '{player.Controller.PlayerName}'");
			}
		}

		// Maps can change bot settings so bots can't join - set them back about once a second.
		// Scoreboard score = -rank (see Scoreboard.cs), kept in line at the same pace.
		if (Server.TickCount % 64 == 0)
		{
			EnforceBotConVars();
			UpdateScoreboard();
			UpdateClanTags();
		}

		// Need to disable maps from executing their cfgs. Currently idk how (But seriusly it a security issue)
		ConVar? bot_quota = ConVar.Find("bot_quota");

		if (bot_quota != null)
		{
			int cbq = bot_quota.GetPrimitiveValue<int>();
			int replaybot_count = CurrentMap.ReplayManager.Pool.Count;

			if (cbq != replaybot_count)
			{
				// If a replay bot never joins after this, the server refused it (e.g. the map has no nav mesh)
				_logger.LogDebug("[{ClassName}] bot_quota {Old} -> {New} (replay pool slots: {Slots}, awaiting a bot: {Awaiting})",
					nameof(SurfTimer), cbq, replaybot_count, replaybot_count, CurrentMap.ReplayManager.Pool.Count(s => s.Controller == null));
				bot_quota.SetValue(replaybot_count);
			}
		}

		// Iterate backwards - KickReplayBot removes from the pool by index mid-loop
		for (int i = CurrentMap.ReplayManager.Pool.Count - 1; i >= 0; i--)
		{
			var slot = CurrentMap.ReplayManager.Pool[i];

			if (slot.Controller == null)
				continue; // Still awaiting the bot to actually spawn - claimed in Players.cs OnPlayerSpawn

			try
			{
				slot.Tick();
			}
			catch (Exception ex)
			{
				LogTickError(ex, $"replay '{slot.RecordPlayerName}' (type {slot.Type}, frame {slot.CurrentFrameTick}/{slot.Frames.Count})");
			}

			if (slot.IsPlaying && slot.RepeatCount == 0)
			{
				// Finished its repeats - go idle in spectator instead of auto-advancing to another replay
				slot.GoIdle();
			}
			else if (!slot.IsPlaying && slot.IdleSince.HasValue &&
					(DateTime.UtcNow - slot.IdleSince.Value).TotalSeconds >= Config.ReplayIdleTimeoutSeconds)
			{
				// Idle and unclaimed for too long - free the slot
				CurrentMap.KickReplayBot(i);
			}
		}

		// Trail segments (Trails.cs) - draws every trail_settings segment_ticks
		try
		{
			TickTrails();
		}
		catch (Exception ex)
		{
			LogTickError(ex, "trails");
		}

		// The zone editor's noclip and undone map teleports (ZoneEditor.cs)
		try
		{
			TickZoneEditor();
		}
		catch (Exception ex)
		{
			LogTickError(ex, "zone editor");
		}

		// Zone outlines (ZoneDrawing.cs) - built while someone wants them, redrawn after round restarts
		if (Server.TickCount % 32 == 0)
		{
			try
			{
				TickZoneOutlines();
			}
			catch (Exception ex)
			{
				LogTickError(ex, "zone outlines");
			}
		}

		// Bots never carry weapons (round restarts / respawns hand out the default loadout) - Weapons.cs
		if (Server.TickCount % 16 == 0)
		{
			try
			{
				StripBots();
			}
			catch (Exception ex)
			{
				LogTickError(ex, "bot weapons");
			}
		}

		// Once per second: unwatched bots, stuck slots, the permanent map bot (ReplayUpkeep.cs)
		if (Server.TickCount % 64 == 0)
		{
			try
			{
				TickReplayUpkeep();
			}
			catch (Exception ex)
			{
				LogTickError(ex, "replay upkeep");
			}
		}
	}

	private void LogTickError(Exception ex, string what)
	{
		if (Server.TickCount < _nextTickErrorLogTick)
			return;

		_nextTickErrorLogTick = Server.TickCount + 64 * 5;
		_logger.LogError(ex, "[{ClassName}] OnTick failed for {What}", nameof(SurfTimer), what);
	}
}
