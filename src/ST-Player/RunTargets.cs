namespace SurfTimer;

/// <summary>
/// The PB and WR of the run a player is in right now - the map's, or in stage / bonus mode that stage's / bonus's. Used
/// by the HUD (PB / WR / rank, the timer turning yellow) and the missed PB / WR sounds. Ticks, 0 when there is none.
/// </summary>
internal static class RunTargets
{
	// The HUD runs every tick, so a bonus/stage index that's unset (0) or has no data must fall back to the map values
	// instead of throwing - one exception there aborts the whole tick for everyone.
	internal static bool HasBonusData(Player p, int style) =>
		HasEntry(p.Stats.BonusPB, p.Timer.Bonus, style)
		&& HasEntry(SurfTimer.CurrentMap.BonusWR, p.Timer.Bonus, style)
		&& HasEntry(SurfTimer.CurrentMap.BonusCompletions, p.Timer.Bonus, style);

	internal static bool HasStageData(Player p, int style) =>
		HasEntry(p.Stats.StagePB, p.Timer.Stage, style)
		&& HasEntry(SurfTimer.CurrentMap.StageWR, p.Timer.Stage, style)
		&& HasEntry(SurfTimer.CurrentMap.StageCompletions, p.Timer.Stage, style);

	internal static bool HasEntry<T>(Dictionary<int, T>[] byIndex, int index, int style) =>
		index > 0 && index < byIndex.Length && byIndex[index] != null && byIndex[index].ContainsKey(style);

	/// <summary>The player's PB of the current run (map / stage / bonus)</summary>
	internal static int PbTicks(Player p)
	{
		int style = p.Timer.Style;
		if (p.Timer.IsBonusMode && HasBonusData(p, style))
			return Math.Max(0, p.Stats.BonusPB[p.Timer.Bonus][style].RunTime);
		if (p.Timer.IsStageMode && HasStageData(p, style))
			return Math.Max(0, p.Stats.StagePB[p.Timer.Stage][style].RunTime);
		return p.Stats.PB.TryGetValue(style, out var pb) ? Math.Max(0, pb.RunTime) : 0;
	}

	/// <summary>The WR of the current run (map / stage / bonus)</summary>
	internal static int WrTicks(Player p)
	{
		int style = p.Timer.Style;
		var map = SurfTimer.CurrentMap;
		if (map == null)
			return 0;
		if (p.Timer.IsBonusMode && HasBonusData(p, style))
			return Math.Max(0, map.BonusWR[p.Timer.Bonus][style].RunTime);
		if (p.Timer.IsStageMode && HasStageData(p, style))
			return Math.Max(0, map.StageWR[p.Timer.Stage][style].RunTime);
		return map.WR.TryGetValue(style, out var wr) ? Math.Max(0, wr.RunTime) : 0;
	}

	/// <summary>A running, counting run that is already slower than this target (ticks, 0 = none)</summary>
	internal static bool Missed(Player p, int target) =>
		target > 0 && p.Timer.IsRunning && !p.Timer.IsPracticeMode && p.Timer.Ticks > target;
}
