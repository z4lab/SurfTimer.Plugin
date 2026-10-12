using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// Run messages of a replay bot for the players spectating it (their "own" toggles, !options - Chat): its splits in a map
/// run, its finish, and the time of a stage / checkpoint replay - each compared with the viewer's own PB there. Played
/// forwards only. Main thread only.
/// </summary>
internal static class ReplayChat
{
	internal static void OnFrame(ReplayPlayer replay, ReplayFrameSituation situation, int frame)
	{
		if (situation is not (ReplayFrameSituation.STAGE_ZONE_ENTER or ReplayFrameSituation.CHECKPOINT_ZONE_ENTER or ReplayFrameSituation.END_ZONE_ENTER))
			return;
		var bot = replay.Controller;
		var map = SurfTimer.CurrentMap;
		if (bot == null || !bot.IsValid || map == null || replay.Type is < 0 or > 3)
			return;

		var (start, end) = replay.GetRunWindow();
		if (frame <= start || frame > end)
			return;

		var viewers = SurfTimer.OnlinePlayers
			.Where(p => p.Controller.IsValid && !p.Controller.IsBot && p.IsSpectating(bot))
			.ToList();
		if (viewers.Count == 0)
			return;

		int ticks = frame - start;
		string label = PlayerHud.ReplayTypeLabel(replay);
		short number = (short)replay.Stage;
		int style = replay.Style;
		bool atEnd = frame == end;

		switch (replay.Type)
		{
			case 0 when situation == ReplayFrameSituation.END_ZONE_ENTER:
				Send(viewers, ChatEvent.MapFinish, v => LocalizationService.LocalizerNonNull["replay_finish", label, PlayerHud.FormatTime(ticks),
					YourPb(ticks, v.Stats.PB.GetValueOrDefault(style)?.RunTime ?? 0)]);
				break;

			case 0:
				// A split of the map run: stage starts on staged maps (stage n+1 = checkpoint n), checkpoints otherwise
				var counted = map.Stages > 0 ? ReplayFrameSituation.STAGE_ZONE_ENTER : ReplayFrameSituation.CHECKPOINT_ZONE_ENTER;
				if (situation != counted)
					return;
				int checkpoint = 0;
				for (int i = start + 1; i <= frame; i++)
				{
					if (replay.Frames[i].Situation == counted)
						checkpoint++;
				}
				Send(viewers, ChatEvent.SplitMap, v =>
				{
					var split = v.Stats.PB.GetValueOrDefault(style)?.Checkpoints?.GetValueOrDefault(checkpoint);
					return LocalizationService.LocalizerNonNull["replay_split", label, checkpoint, PlayerHud.FormatTime(ticks),
						YourPb(ticks, split?.RunTime ?? 0)];
				});
				break;

			case 1 when atEnd:
				Send(viewers, ChatEvent.BonusFinish, v => LocalizationService.LocalizerNonNull["replay_finish", label, PlayerHud.FormatTime(ticks),
					YourPb(ticks, RunTargets.HasEntry(v.Stats.BonusPB, number, style) ? v.Stats.BonusPB[number][style].RunTime : 0)]);
				break;

			case 2 when atEnd:
				Send(viewers, ChatEvent.SplitSegment, v => LocalizationService.LocalizerNonNull["replay_segment", label, $"Stage {number}",
					PlayerHud.FormatTime(ticks), YourPb(ticks, RunTargets.HasEntry(v.Stats.StagePB, number, style) ? v.Stats.StagePB[number][style].RunTime : 0)]);
				break;

			case 3 when atEnd:
				Send(viewers, ChatEvent.SplitSegment, v => LocalizationService.LocalizerNonNull["replay_segment", label, $"CP {number}",
					PlayerHud.FormatTime(ticks), YourPb(ticks, RunTargets.HasEntry(v.Stats.CheckpointPB, number, style) ? v.Stats.CheckpointPB[number][style].RunTime : 0)]);
				break;
		}
	}

	private static void Send(List<Player> viewers, ChatEvent ev, Func<Player, string> message)
	{
		foreach (var viewer in viewers)
		{
			if (viewer.Options.ChatOwn(ev))
				viewer.Controller.PrintToChat($"{Config.PluginPrefix} {message(viewer)}");
		}
	}

	/// <summary>" (your PB -0.321)" - green when the replay is faster than the viewer's PB, empty without one</summary>
	private static string YourPb(int ticks, int pbTicks)
	{
		if (pbTicks <= 0)
			return "";
		int diff = ticks - pbTicks;
		string value = diff <= 0
			? $"{ChatColors.Green}-{PlayerHud.FormatTime(-diff)}"
			: $"{ChatColors.Red}+{PlayerHud.FormatTime(diff)}";
		return $" (your PB {value}{ChatColors.Default})";
	}
}
