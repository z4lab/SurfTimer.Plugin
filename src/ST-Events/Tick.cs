using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;

namespace SurfTimer;

public partial class SurfTimer
{
	public void OnTick()
	{
		if (CurrentMap == null)
			return;

		foreach (var player in playerList.Values)
		{
			if (!player.Controller.IsValid)
				continue;

			// Spectators/dead players have no live PlayerPawn - the timer, recorder and speed cap
			// all read it, and one exception here would abort the tick for everyone.
			if (player.Controller.PawnIsAlive)
			{
				player.Timer.Tick();
				player.ReplayRecorder.Tick(player);
				player.TickStartZoneSpeedCap();
			}

			player.HUD.Display();
		}

		// Need to disable maps from executing their cfgs. Currently idk how (But seriusly it a security issue)
		ConVar? bot_quota = ConVar.Find("bot_quota");

		if (bot_quota != null)
		{
			int cbq = bot_quota.GetPrimitiveValue<int>();
			int replaybot_count = CurrentMap.ReplayManager.Pool.Count;

			if (cbq != replaybot_count)
			{
				bot_quota.SetValue(replaybot_count);
			}
		}

		// Iterate backwards - KickReplayBot removes from the pool by index mid-loop
		for (int i = CurrentMap.ReplayManager.Pool.Count - 1; i >= 0; i--)
		{
			var slot = CurrentMap.ReplayManager.Pool[i];

			if (slot.Controller == null)
				continue; // Still awaiting the bot to actually spawn - claimed in Players.cs OnPlayerSpawn

			slot.Tick();

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
	}
}
