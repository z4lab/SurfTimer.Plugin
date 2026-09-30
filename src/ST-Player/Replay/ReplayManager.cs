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
	/// <summary>The requester's own bot got the new content (one requested bot per player).</summary>
	Replaced,
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
	/// On-demand pool of replay bots, capped at Config.ReplayPoolCap (plus the optional permanent map
	/// WR bot, IsPermanent, which isn't counted). Each slot either awaits a
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
			this.AllCheckpointWR = new Dictionary<int, ReplayPlayer>[SurfTimer.CurrentMap.CheckpointSegments + 1];

			for (int i = 1; i <= SurfTimer.CurrentMap.CheckpointSegments; i++)
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

	/// <summary>
	/// Replay type of the "best segments" replay: every stage (staged maps) or checkpoint segment
	/// (linear maps) WR chained into one run.
	/// </summary>
	internal const int BestSegmentsType = 4;

	internal static string BestSegmentsLabel => SurfTimer.CurrentMap.Stages > 0 ? "Best Stage WRs" : "Best Checkpoint WRs";

	/// <summary>One segment WR inside the best segments replay - StartFrame is where it begins in the chained frames</summary>
	internal sealed record BestSegmentPart(int Number, string Holder, int Time, int StartFrame);

	/// <summary>
	/// The WR replay of every segment in order - null unless every segment has one.
	/// </summary>
	private List<ReplayPlayer>? BestSegmentTemplates(int style)
	{
		var map = SurfTimer.CurrentMap;
		Dictionary<int, ReplayPlayer>[] source;
		int count;
		if (map.Stages > 0)
		{
			source = this.AllStageWR;
			count = map.Stages;
		}
		else
		{
			source = this.AllCheckpointWR;
			count = map.CheckpointSegments;
		}

		if (count < 2)
			return null;

		var templates = new List<ReplayPlayer>(count);
		for (int i = 1; i <= count; i++)
		{
			if (i >= source.Length || source[i] == null || !source[i].TryGetValue(style, out var template)
				|| template.MapTimeID == -1 || template.Frames.Count == 0 || template.RecordRunTime <= 0)
				return null;
			templates.Add(template);
		}
		return templates;
	}

	/// <summary>
	/// What the segment WRs add up to - null when a segment has no WR replay.
	/// </summary>
	internal int? BestSegmentsTime(int style) => BestSegmentTemplates(style)?.Sum(t => t.RecordRunTime);

	/// <summary>
	/// Builds one replay out of every segment's WR replay: the first segment from its pre-start
	/// frames, every segment's own run (zone exit to next zone enter), and the last one through its
	/// end zone. Frames are copied (the templates keep their own sync data) and only the overall start
	/// and end are kept as zone markers, so it plays and times like a normal map run. Built on demand.
	/// </summary>
	internal ReplayPlayer? BuildBestSegmentsReplay(int style)
	{
		var segments = BestSegmentTemplates(style);
		if (segments == null)
			return null;

		var frames = new List<ReplayFrame>();
		var parts = new List<BestSegmentPart>(segments.Count);
		for (int i = 0; i < segments.Count; i++)
		{
			var segment = segments[i];
			parts.Add(new BestSegmentPart(i + 1, segment.RecordPlayerName, segment.RecordRunTime, frames.Count));
			var (start, end) = segment.GetRunWindow();
			bool first = i == 0;
			bool last = i == segments.Count - 1;

			int from = first ? 0 : start;
			int to = last ? segment.Frames.Count : end; // Exclusive - the next segment starts where this one ends

			for (int f = from; f < to; f++)
			{
				var source = segment.Frames[f];
				var situation = ReplayFrameSituation.NONE;
				if (first && f == start)
					situation = ReplayFrameSituation.START_ZONE_EXIT;
				else if (last && f == end)
					situation = ReplayFrameSituation.END_ZONE_ENTER;

				frames.Add(new ReplayFrame
				{
					pos = source.pos,
					ang = source.ang,
					Situation = situation,
					Flags = source.Flags,
					Buttons = source.Buttons,
				});
			}
		}

		ReplayFrame.PrepareSync(frames);

		return new ReplayPlayer
		{
			Type = BestSegmentsType,
			Stage = 0,
			Style = style,
			MapID = SurfTimer.CurrentMap.ID,
			MapTimeID = -10 - style, // Not a stored run - unique per style so the pool can reuse a playing one
			RecordRank = 1,
			RecordPlayerName = BestSegmentsLabel,
			RecordRunTime = segments.Sum(s => s.RecordRunTime),
			Frames = frames,
			BestSegmentParts = parts,
		};
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

	/// <summary>Requested (non-permanent) bots - what Config.ReplayPoolCap limits</summary>
	internal int RequestedCount => this.Pool.Count(s => !s.IsPermanent);

	internal ReplayPlayer? PermanentSlot => this.Pool.Find(s => s.IsPermanent);

	/// <summary>
	/// Decides whether the requested content is already playing, goes into the requester's own bot,
	/// needs a fresh bot spawned, can reclaim an idle pool slot, or must be refused because the pool is
	/// full and everything in it is actively playing something else.
	/// </summary>
	/// <param name="contentTemplate">A content template (MapWR / AllStageWR[..][..] / AllBonusWR[..][..] / AllCheckpointWR[..][..], or an ad-hoc PB template) to load into a pool slot.</param>
	/// <param name="requestedByPlayerId">-1 for WR content, else the PB owner's Profile.ID.</param>
	/// <param name="repeatCount">How many times the replay should play before going idle.</param>
	/// <param name="requesterUserId">UserId of the player asking - each player has at most one requested bot.</param>
	internal (ReplayReuseResult Result, ReplayPlayer? Slot) RequestReplay(ReplayPlayer contentTemplate, int requestedByPlayerId, int repeatCount, int? requesterUserId)
	{
		// 1. Already playing this exact content (the permanent map bot too) - just spectate it
		foreach (var slot in this.Pool)
		{
			if (slot.IsPlaying && slot.MapTimeID == contentTemplate.MapTimeID)
				return (ReplayReuseResult.AlreadyPlaying, slot);
		}

		// 2. The requester already has a bot - it gets the new content (side effects: caller)
		var own = requesterUserId == null ? null : this.Pool.Find(s => !s.IsPermanent && s.RequesterUserId == requesterUserId);
		if (own != null)
		{
			own.LoadContentFrom(contentTemplate, requestedByPlayerId);
			own.LoadReplayData(repeatCount);
			own.IdleSince = null;
			own.LastWatchedAt = DateTime.UtcNow;
			return (ReplayReuseResult.Replaced, own);
		}

		// 3. Room in the pool - append a new slot, awaiting a freshly-spawned bot
		if (this.RequestedCount < Config.ReplayPoolCap)
		{
			var newSlot = new ReplayPlayer { RequesterUserId = requesterUserId };
			newSlot.LoadContentFrom(contentTemplate, requestedByPlayerId);
			this.Pool.Add(newSlot);
			return (ReplayReuseResult.Spawning, newSlot);
		}

		// 4. Pool full - try to reclaim a genuinely idle slot (has a live Controller but isn't playing;
		//    distinct from a slot still awaiting spawn, which has Controller == null). Only data is
		//    updated here - the caller (which has access to AddTimer, unlike this plain class) is
		//    responsible for the team switch / RemoveWeapons / Start / FormatBotName side effects.
		foreach (var slot in this.Pool)
		{
			if (!slot.IsPermanent && slot.Controller != null && !slot.IsPlaying)
			{
				slot.LoadContentFrom(contentTemplate, requestedByPlayerId);
				slot.LoadReplayData(repeatCount);
				slot.IdleSince = null;
				slot.RequesterUserId = requesterUserId;
				slot.LastWatchedAt = DateTime.UtcNow;

				return (ReplayReuseResult.ReclaimedIdle, slot);
			}
		}

		// 5. Pool full, nothing idle to reclaim - refuse
		return (ReplayReuseResult.CapReached, null);
	}
}
