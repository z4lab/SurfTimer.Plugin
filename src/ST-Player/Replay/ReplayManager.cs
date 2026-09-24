using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// Outcome of a ReplayManager.RequestReplay call.
/// </summary>
public enum ReplayReuseResult
{
	/// <summary>The exact content requested is already playing - caller should just spectate the returned slot.</summary>
	AlreadyPlaying,
	/// <summary>A new pool slot was created and awaits a freshly-spawned bot (claimed in Players.cs OnPlayerSpawn).</summary>
	Spawning,
	/// <summary>An idle slot was reclaimed and is playing the requested content immediately.</summary>
	ReclaimedIdle,
	/// <summary>The pool is full and every slot is actively playing something else - request refused.</summary>
	CapReached,
}

public class ReplayManager
{
	public ReplayPlayer MapWR { get; set; }
	/// <summary>
	/// Contains all Stage records for all styles - Refer to as AllStageWR[stage#][style]
	/// </summary>
	public Dictionary<int, ReplayPlayer>[] AllStageWR { get; set; } = Array.Empty<Dictionary<int, ReplayPlayer>>();
	/// <summary>
	/// Contains all Bonus records for all styles - Refer to as AllBonusWR[bonus#][style]
	/// </summary>
	public Dictionary<int, ReplayPlayer>[] AllBonusWR { get; set; } = Array.Empty<Dictionary<int, ReplayPlayer>>();
	/// <summary>
	/// Contains all Checkpoint segment records for all styles (non-staged maps only) - Refer to as AllCheckpointWR[checkpoint#][style]
	/// </summary>
	public Dictionary<int, ReplayPlayer>[] AllCheckpointWR { get; set; } = Array.Empty<Dictionary<int, ReplayPlayer>>();
	/// <summary>
	/// On-demand pool of replay bots, capped at Config.ReplayPoolCap. Each slot either awaits a
	/// spawning bot (Controller == null), is actively playing (IsPlaying == true), or is idle
	/// (Controller != null, IsPlaying == false, IdleSince set) awaiting reclaim or a kick timeout.
	/// </summary>
	public List<ReplayPlayer> Pool { get; set; }


	/// <param name="map_id">ID of the map</param>
	/// <param name="staged">Does the map have Stages</param>
	/// <param name="bonused">Does the map have Bonuses</param>
	/// <param name="checkpointed">Does the map have Checkpoint segments (non-staged maps only)</param>
	/// <param name="frames">Frames for the replay</param>
	/// <param name="run_time">Run time (Ticks) for the run</param>
	/// <param name="playerName">Name of the player</param>
	/// <param name="map_time_id">ID of the run</param>
	/// <param name="style">Style of the run</param>
	/// <param name="stage">Stage/Bonus of the run</param>
	internal ReplayManager(int map_id, bool staged, bool bonused, bool checkpointed, List<ReplayFrame> frames, int run_time = 0, string playerName = "", int map_time_id = -1, int style = 0, int stage = 0)
	{
		MapWR = new ReplayPlayer
		{
			Type = 0,
			Stage = 0,
			RecordRank = 1,
			MapID = map_id,
			Frames = frames,
			RecordRunTime = run_time,
			RecordPlayerName = playerName,
			MapTimeID = map_time_id
		};

		if (staged)
		{
			this.AllStageWR = new Dictionary<int, ReplayPlayer>[SurfTimer.CurrentMap.Stages + 1];

			for (int i = 1; i <= SurfTimer.CurrentMap.Stages; i++)
			{
				AllStageWR[i] = new Dictionary<int, ReplayPlayer>();
				foreach (int x in Config.Styles)
				{
					AllStageWR[i][x] = new ReplayPlayer();
				}
			}
		}

		if (bonused)
		{
			this.AllBonusWR = new Dictionary<int, ReplayPlayer>[SurfTimer.CurrentMap.Bonuses + 1];

			for (int i = 1; i <= SurfTimer.CurrentMap.Bonuses; i++)
			{
				AllBonusWR[i] = new Dictionary<int, ReplayPlayer>();
				foreach (int x in Config.Styles)
				{
					AllBonusWR[i][x] = new ReplayPlayer();
				}
			}
		}

		if (checkpointed)
		{
			this.AllCheckpointWR = new Dictionary<int, ReplayPlayer>[SurfTimer.CurrentMap.TotalCheckpoints + 1];

			for (int i = 1; i <= SurfTimer.CurrentMap.TotalCheckpoints; i++)
			{
				AllCheckpointWR[i] = new Dictionary<int, ReplayPlayer>();
				foreach (int x in Config.Styles)
				{
					AllCheckpointWR[i][x] = new ReplayPlayer();
				}
			}
		}

		Pool = new List<ReplayPlayer>();
	}

	public bool IsControllerConnectedToReplayPlayer(CCSPlayerController controller)
	{
		foreach (var slot in this.Pool)
		{
			if (slot.Controller?.Equals(controller) == true)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Decides whether the requested content is already playing, needs a fresh bot spawned,
	/// can reclaim an idle pool slot, or must be refused because the pool is full and everything
	/// in it is actively playing something else.
	/// </summary>
	/// <param name="contentTemplate">A content template (MapWR / AllStageWR[..][..] / AllBonusWR[..][..] / AllCheckpointWR[..][..], or an ad-hoc PB template) to load into a pool slot.</param>
	/// <param name="requestedByPlayerId">-1 for WR content, else the PB owner's Profile.ID.</param>
	/// <param name="repeatCount">How many times the replay should play before going idle.</param>
	internal (ReplayReuseResult Result, ReplayPlayer? Slot) RequestReplay(ReplayPlayer contentTemplate, int requestedByPlayerId, int repeatCount)
	{
		// 1. Already playing this exact content - just spectate it, don't touch the pool
		foreach (var slot in this.Pool)
		{
			if (slot.IsPlaying && slot.MapTimeID == contentTemplate.MapTimeID)
				return (ReplayReuseResult.AlreadyPlaying, slot);
		}

		// 2. Room in the pool - append a new slot, awaiting a freshly-spawned bot
		if (this.Pool.Count < Config.ReplayPoolCap)
		{
			var newSlot = new ReplayPlayer();
			newSlot.LoadContentFrom(contentTemplate, requestedByPlayerId);
			this.Pool.Add(newSlot);
			return (ReplayReuseResult.Spawning, newSlot);
		}

		// 3. Pool full - try to reclaim a genuinely idle slot (has a live Controller but isn't playing;
		//    distinct from a slot still awaiting spawn, which has Controller == null). Only data is
		//    updated here - the caller (which has access to AddTimer, unlike this plain class) is
		//    responsible for the team switch / RemoveWeapons / Start / FormatBotName side effects.
		foreach (var slot in this.Pool)
		{
			if (slot.Controller != null && !slot.IsPlaying)
			{
				slot.LoadContentFrom(contentTemplate, requestedByPlayerId);
				slot.LoadReplayData(repeatCount);
				slot.IdleSince = null;

				return (ReplayReuseResult.ReclaimedIdle, slot);
			}
		}

		// 4. Pool full, nothing idle to reclaim - refuse
		return (ReplayReuseResult.CapReached, null);
	}
}
