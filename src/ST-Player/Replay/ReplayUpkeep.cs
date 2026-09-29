using CounterStrikeSharp.API;

namespace SurfTimer;

/// <summary>
/// Replay pool upkeep, once per second: requested bots nobody spectates are removed after
/// Config.ReplayUnwatchedTimeoutSeconds, slots whose bot never arrived are dropped, and the permanent
/// map WR bot (Config.ReplayPermanentMapBot) is kept in place.
/// </summary>
public partial class SurfTimer
{
	private const int ReplaySpawnTimeoutSeconds = 15;

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
			var slot = new ReplayPlayer { IsPermanent = true };
			slot.LoadContentFrom(wr!);
			manager.Pool.Add(slot);
			SpawnReplayBotDirectly(slot);
			return;
		}

		// A new map WR - the bot switches to it right away
		if (permanent.Controller != null && permanent.MapTimeID != wr!.MapTimeID)
		{
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
		}
	}
}
