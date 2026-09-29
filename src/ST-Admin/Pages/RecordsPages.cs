namespace SurfTimer;

/// <summary>
/// Admin panel - Records tab: the current map's leaderboards per course, a time's details, playing its
/// replay and deleting it.
/// </summary>
public partial class SurfTimer
{
	private const int BoardPageSize = 50;

	private PanelPage AdminRecordsRoot() => new("Records", ctx =>
	{
		var map = CurrentMap;
		if (map == null || map.ID <= 0)
			return [PanelContext.Info("No map loaded")];

		int style = ctx.Style;
		var rows = new List<HudMenuItem>();

		void Add(CourseKind kind, short number, PersonalBest? wr, int completions)
		{
			var course = map.Course(kind, number);
			if (course == null)
				return;
			string sub = wr != null && wr.ID != -1 ? $"WR {AdminFormat.Time(wr.RunTime)} · {wr.Name}" : "no times";
			int courseId = course.Id;
			rows.Add(ctx.Nav(CourseLabel(kind, number), $"{completions} times", sub, () => AdminBoardPage(courseId, kind, number)));
		}

		Add(CourseKind.Map, 0, map.WR.GetValueOrDefault(style), map.MapCompletions.GetValueOrDefault(style));
		for (short s = 1; s <= map.Stages; s++)
			Add(CourseKind.Stage, s, map.StageWR[s]?.GetValueOrDefault(style), map.StageCompletions[s]?.GetValueOrDefault(style) ?? 0);
		for (short b = 1; b <= map.Bonuses; b++)
			Add(CourseKind.Bonus, b, map.BonusWR[b]?.GetValueOrDefault(style), map.BonusCompletions[b]?.GetValueOrDefault(style) ?? 0);
		for (short c = 1; c < map.CheckpointWR.Length; c++)
			Add(CourseKind.Checkpoint, c, map.CheckpointWR[c]?.GetValueOrDefault(style), map.CheckpointCompletions[c]?.GetValueOrDefault(style) ?? 0);
		return rows;
	});

	private sealed class BoardData(List<TimeRepository.BoardRow> rows)
	{
		internal List<TimeRepository.BoardRow> Rows { get; } = rows;
	}

	private PanelPage AdminBoardPage(int courseId, CourseKind kind, short number)
	{
		int limit = BoardPageSize;
		return new PanelPage(CourseLabel(kind, number), ctx =>
		{
			int style = ctx.Style;
			var board = ctx.Load("board", async () => new BoardData(await TimeRepository.GetLeaderboardAsync(courseId, style, 0, limit)));
			if (board == null)
				return [PanelContext.LoadingRow()];
			if (board.Rows.Count == 0)
				return [PanelContext.Info("No times yet")];

			var rows = board.Rows.Select(r => ctx.Nav($"#{r.Rank}  {r.PlayerName}", AdminFormat.Time(r.RunTime),
				AdminFormat.Date(r.UpdatedAt), () => AdminTimePage(r.Id, kind, number))).ToList();

			if (board.Rows.Count >= limit)
			{
				rows.Add(ctx.Act("Load more", "", $"showing {limit}", () =>
				{
					limit += BoardPageSize;
					ctx.Reload("board");
				}));
			}
			return rows;
		});
	}

	private PanelPage AdminTimePage(int timeId, CourseKind kind, short number) => new("Time", ctx =>
	{
		var time = ctx.Load("time", () => TimeRepository.GetTimeAsync(timeId));
		if (time == null)
			return ctx.IsLoading("time") ? [PanelContext.LoadingRow()] : [PanelContext.Info("This time no longer exists")];

		string label = CourseLabel(kind, number);
		float startSpeed = MathF.Sqrt(time.StartVelX * time.StartVelX + time.StartVelY * time.StartVelY);
		float endSpeed = MathF.Sqrt(time.EndVelX * time.EndVelX + time.EndVelY * time.EndVelY);

		var rows = new List<HudMenuItem>
		{
			PanelContext.Info(time.PlayerName ?? "?", AdminFormat.Time(time.RunTime), $"#{time.Rank} of {time.TotalCount} · {label}"),
			PanelContext.Info("Set", AdminFormat.Date(time.UpdatedAt), AdminFormat.Ago(time.UpdatedAt) + " ago"),
			PanelContext.Info("Sync", time.Sync.HasValue ? $"{time.Sync:0.00}%" : "N/A"),
			PanelContext.Info("Start / end speed", $"{startSpeed:0} / {endSpeed:0} u/s"),
			PanelContext.Info("Replay", time.ReplayId != null ? "stored" : "none"),
		};

		if (time.ReplayId != null && CurrentMap.ID == time.MapId)
		{
			var pb = new PbReplayRef(time.Id, time.ReplayId, time.RunTime, (int)time.Rank);
			short type = CourseKinds.ToRunType(kind);
			string owner = time.PlayerName ?? "?";
			int ownerId = time.PlayerId;
			int style = time.Style;
			rows.Add(ctx.Act("Play replay", "", "", () =>
				_ = HandlePbReplaySelection(ctx.Player.Controller, pb, type, number, style, ownerName: owner, ownerId: ownerId), closes: true));
		}

		rows.Add(ctx.Nav("Player", time.PlayerName ?? "?", "", () => AdminPlayerPage(time.PlayerId, time.PlayerName ?? "?")));
		rows.Add(ctx.Danger("Delete time", "and its replay", () => AdminWipeConfirm("Delete time",
			new TimeRepository.WipeScope(TimeId: time.Id), $"{time.PlayerName} {AdminFormat.Time(time.RunTime)} on {CurrentMap.Name} {label}")));
		return rows;
	});
}
