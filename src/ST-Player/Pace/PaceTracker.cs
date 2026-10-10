namespace SurfTimer;

/// <summary>
/// What the live pace is measured against: the reference run's total time, its splits (map runs) and - when it has a
/// stored replay - the replay to match positions on.
/// </summary>
/// <param name="Key">Tells reference runs apart - a new one starts the matching over</param>
/// <param name="Type">0 map, 1 bonus, 2 stage</param>
internal sealed record PaceReference(string Key, int TotalTicks, Dictionary<int, CheckpointEntity>? Splits, int? ReplayId,
	IReadOnlyList<ReplayFrame>? InMemoryFrames, short Type, short Stage);

/// <param name="PaceTicks">The projected finish time</param>
/// <param name="DeltaTicks">Ahead (negative) / behind (positive) of the reference at this point</param>
/// <param name="Live">From matching the position on the reference's replay - false: from the splits only</param>
/// <param name="SpeedDiff">Own speed minus the reference's at the same spot (live only)</param>
/// <param name="EnergyDiff">Own energy minus the reference's at the same spot, in units of height (live only)</param>
internal readonly record struct PaceResult(int PaceTicks, int DeltaTicks, bool Live, float? SpeedDiff, float? EnergyDiff);

/// <summary>
/// Live pace of a run (!options - HUD - Pace): where the runner is on the reference run's path - the replay tick
/// closest to their position, searched around the last match (like shavit's replay time difference) - tells how far
/// ahead / behind they are right now, and so the projected finish (reference time + that difference). Without a replay:
/// LiveSplit's "current pace" from the splits. One per viewer's HUD.
/// </summary>
internal sealed class PaceTracker
{
	/// <summary>Searched around the last match: a second back, four ahead</summary>
	private const int SearchBack = 64, SearchAhead = 64 * 4;
	/// <summary>Further away than this from the matched spot (teleport, !back): the whole path is searched - once a second</summary>
	private const float LostDistance = 400f;

	private string? _referenceKey;
	private int _lastFrame = -1;
	private int _lastTicks;
	private int _nextFullScanTick;
	private PaceResult? _last;

	internal PaceResult? Update(Player runner, PaceReference reference, float speed, float gravity, int now)
	{
		var timer = runner.Timer;
		if (!timer.IsRunning || timer.IsPracticeMode || reference.TotalTicks <= 0)
		{
			Reset(null);
			return null;
		}

		// A new run or reference: match from scratch
		if (reference.Key != _referenceKey || timer.Ticks < _lastTicks)
			Reset(reference.Key);
		_lastTicks = timer.Ticks;

		var path = reference.ReplayId is int replayId
			? ReferenceRuns.Get(replayId, reference.Type, reference.Stage, reference.InMemoryFrames)
			: null;
		var pawn = runner.Controller.PlayerPawn.Value;
		var origin = pawn != null && pawn.IsValid ? pawn.AbsOrigin : null;

		if (path != null && origin != null)
		{
			int frame = Match(path, origin.X, origin.Y, origin.Z, now);
			if (frame >= 0)
			{
				int delta = timer.Ticks - frame;
				float refSpeed = path.Speeds[frame];
				float energyDiff = (speed * speed - refSpeed * refSpeed) / (2f * gravity);
				_last = new PaceResult(reference.TotalTicks + delta, delta, true, speed - refSpeed, energyDiff);
				return _last;
			}
			if (_last is { Live: true })
				return _last; // Off the path for a moment - keep the last value
		}

		_last = SplitPace(runner, reference);
		return _last;
	}

	private void Reset(string? key)
	{
		_referenceKey = key;
		_lastFrame = -1;
		_lastTicks = 0;
		_nextFullScanTick = 0;
		_last = null;
	}

	/// <summary>The path tick closest to the position - -1 when it's too far from the path</summary>
	private int Match(ReferencePath path, float x, float y, float z, int now)
	{
		int best = -1;
		float bestDistance = float.MaxValue;

		void Search(int from, int to)
		{
			var p = path.Positions;
			for (int i = Math.Max(0, from); i <= Math.Min(path.Count - 1, to); i++)
			{
				float dx = p[i * 3] - x, dy = p[i * 3 + 1] - y, dz = p[i * 3 + 2] - z;
				float distance = dx * dx + dy * dy + dz * dz;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = i;
				}
			}
		}

		if (_lastFrame >= 0)
			Search(_lastFrame - SearchBack, _lastFrame + SearchAhead);
		if ((best < 0 || bestDistance > LostDistance * LostDistance) && now >= _nextFullScanTick)
		{
			_nextFullScanTick = now + 64;
			Search(0, path.Count - 1);
		}

		if (best < 0 || bestDistance > LostDistance * LostDistance)
			return -1;
		_lastFrame = best;
		return best;
	}

	/// <summary>
	/// LiveSplit's current pace: reference time + how far behind / ahead the last split was - or, already slower than the
	/// reference at the next split, how far behind that is. Without splits (stage / bonus): at least the time so far.
	/// </summary>
	private static PaceResult SplitPace(Player runner, PaceReference reference)
	{
		int ticks = runner.Timer.Ticks;
		var splits = reference.Splits;
		if (splits == null || splits.Count == 0)
		{
			int behind = Math.Max(0, ticks - reference.TotalTicks);
			return new PaceResult(reference.TotalTicks + behind, behind, false, null, null);
		}

		int delta = 0;
		int nextKey = 1;
		var own = runner.Stats.ThisRun.Checkpoints;
		foreach (var (key, split) in own.OrderBy(c => c.Key))
		{
			if (splits.TryGetValue(key, out var target) && target.RunTime > 0)
				delta = split.RunTime - target.RunTime;
			nextKey = key + 1;
		}

		int nextTarget = splits.TryGetValue(nextKey, out var next) && next.RunTime > 0 ? next.RunTime : reference.TotalTicks;
		int live = ticks - nextTarget;
		if (live > delta)
			delta = live;
		return new PaceResult(reference.TotalTicks + delta, delta, false, null, null);
	}
}
