using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// !map - a map's stats and records in the panel popup: Map (stats), Records (map board), Stages,
/// Checkpoints and Bonuses (course list, then a board). Boards and stats load from the database only
/// when their tab / page is opened. The current map by default, another one by name.
/// </summary>
public partial class SurfTimer
{
	private enum MapTab
	{
		Stats,
		Records,
		Stages,
		Checkpoints,
		Bonuses,
	}

	/// <summary>A course of the shown map with its WR and completions (in the viewer's style)</summary>
	private sealed record CourseSummary(CourseKind Kind, short Number, int CourseId, byte? Tier, string? Name, int Completions,
		int? WrTime, string? WrHolder);

	/// <summary>The map !map shows - built from memory for the current map, from the database for others</summary>
	private sealed record MapTarget(int Id, string Name, bool IsCurrent, short Tier, string? Author, bool Ranked, int DateAdded,
		int LastPlayed, ulong? WorkshopId, List<CourseSummary> Courses)
	{
		internal CourseSummary? Course(CourseKind kind, short number) =>
			Courses.FirstOrDefault(c => c.Kind == kind && (kind == CourseKind.Map || c.Number == number));

		internal List<CourseSummary> Of(CourseKind kind) => Courses.Where(c => c.Kind == kind).OrderBy(c => c.Number).ToList();
	}

	// ---- Commands ----

	[ConsoleCommand("css_map", "Stats and records of this map (or another map by name)")]
	[ConsoleCommand("css_mapinfo", "Stats and records of this map (or another map by name)")]
	[ConsoleCommand("css_mi", "Stats and records of this map (or another map by name)")]
	[ConsoleCommand("css_tier", "Stats and records of this map (or another map by name)")]
	[ConsoleCommand("css_difficulty", "Stats and records of this map (or another map by name)")]
	[CommandHelper(usage: "[map]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void MapInfoCommand(CCSPlayerController? player, CommandInfo command) => OpenMapPanel(player, MapTab.Stats, command);

	[ConsoleCommand("css_maptop", "Map records (of this map or another map by name)")]
	[ConsoleCommand("css_mtop", "Map records (of this map or another map by name)")]
	[CommandHelper(usage: "[map]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void MapTopCommand(CCSPlayerController? player, CommandInfo command) => OpenMapPanel(player, MapTab.Records, command);

	[ConsoleCommand("css_stagetop", "Stage records - a number opens that stage's board")]
	[ConsoleCommand("css_stop", "Stage records - a number opens that stage's board")]
	[CommandHelper(usage: "[map] [stage]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void StageTopCommand(CCSPlayerController? player, CommandInfo command) => OpenMapPanel(player, MapTab.Stages, command);

	[ConsoleCommand("css_cptop", "Checkpoint records - a number opens that checkpoint's board")]
	[ConsoleCommand("css_ctop", "Checkpoint records - a number opens that checkpoint's board")]
	[CommandHelper(usage: "[map] [checkpoint]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void CheckpointTopCommand(CCSPlayerController? player, CommandInfo command) => OpenMapPanel(player, MapTab.Checkpoints, command);

	[ConsoleCommand("css_btop", "Bonus records - a number opens that bonus' board")]
	[ConsoleCommand("css_bonustop", "Bonus records - a number opens that bonus' board")]
	[CommandHelper(usage: "[map] [bonus]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void BonusTopCommand(CCSPlayerController? player, CommandInfo command) => OpenMapPanel(player, MapTab.Bonuses, command);

	/// <summary>
	/// Arguments: a number is the stage / checkpoint / bonus to open, everything else the map name.
	/// </summary>
	private void OpenMapPanel(CCSPlayerController? controller, MapTab tab, CommandInfo command)
	{
		if (controller == null || !playerList.TryGetValue(controller.UserId ?? 0, out var player))
			return;

		short? number = null;
		var nameParts = new List<string>();
		for (int i = 1; i < command.ArgCount; i++)
		{
			string arg = command.GetArg(i).Trim();
			if (arg.Length == 0)
				continue;
			if (number == null && short.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
				number = n;
			else
				nameParts.Add(arg);
		}

		string name = string.Join(" ", nameParts);
		if (name.Length > 0 && ResolveMap(name) is { } listed)
			name = listed; // "summit" -> surf_summit (the server's map list)
		int style = player.Timer.Style;

		if (name.Length == 0 || (CurrentMap != null && name.Equals(CurrentMap.Name, StringComparison.OrdinalIgnoreCase)))
		{
			var current = CurrentMapTarget(style);
			if (current != null)
				ShowMapPanel(player, current, tab, number);
			return;
		}

		Task.Run(async () =>
		{
			try
			{
				var row = await MapStatsRepository.FindMapAsync(name);
				MapTarget? target = null;
				if (row != null && row.Id != (CurrentMap?.ID ?? 0))
					target = await LoadMapTargetAsync(row, style);

				Server.NextFrame(() =>
				{
					if (!controller.IsValid || !playerList.TryGetValue(controller.UserId ?? 0, out var stillPlayer))
						return;

					target ??= row != null ? CurrentMapTarget(style) : null; // The name matched the current map
					if (target == null)
					{
						if (IsOnMapList(name))
						{
							controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["map_not_played", name]}");
							return;
						}
						var suggestions = SuggestMaps(name);
						controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["map_not_found", name]}"
							+ (suggestions.Count > 0 ? $" {LocalizationService.LocalizerNonNull["map_suggestions", string.Join(", ", suggestions)]}" : ""));
						return;
					}
					ShowMapPanel(stillPlayer, target, tab, number);
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[{ClassName}] Loading map '{Map}' for !map failed", nameof(SurfTimer), name);
			}
		});
	}

	/// <summary>The current map from memory - no database queries</summary>
	private MapTarget? CurrentMapTarget(int style)
	{
		var map = CurrentMap;
		if (map == null || map.ID <= 0)
			return null;

		var courses = new List<CourseSummary>();
		void Add(CourseKind kind, short number, PersonalBest? wr, int completions)
		{
			var course = map.Course(kind, number);
			if (course == null)
				return;
			bool hasWr = wr != null && wr.ID != -1;
			courses.Add(new CourseSummary(kind, number, course.Id, course.Tier, course.Name, completions,
				hasWr ? wr!.RunTime : null, hasWr ? wr!.Name : null));
		}

		Add(CourseKind.Map, 0, map.WR.GetValueOrDefault(style), map.MapCompletions.GetValueOrDefault(style));
		for (short s = 1; s <= map.Stages; s++)
			Add(CourseKind.Stage, s, map.StageWR[s]?.GetValueOrDefault(style), map.StageCompletions[s]?.GetValueOrDefault(style) ?? 0);
		for (short b = 1; b <= map.Bonuses; b++)
			Add(CourseKind.Bonus, b, map.BonusWR[b]?.GetValueOrDefault(style), map.BonusCompletions[b]?.GetValueOrDefault(style) ?? 0);
		for (short c = 1; c < map.CheckpointWR.Length; c++)
			Add(CourseKind.Checkpoint, c, map.CheckpointWR[c]?.GetValueOrDefault(style), map.CheckpointCompletions[c]?.GetValueOrDefault(style) ?? 0);

		return new MapTarget(map.ID, map.Name ?? "?", true, map.Tier, map.Author, map.Ranked, map.DateAdded, map.LastPlayed,
			map.WorkshopId, courses);
	}

	/// <summary>Another map from the database (off the main thread)</summary>
	private static async Task<MapTarget> LoadMapTargetAsync(MapRepository.MapRow row, int style)
	{
		var authors = await MapRepository.GetAuthorsAsync(row.Id);
		var settings = await MapRepository.GetSettingsAsync(row.Id);
		var rows = await MapStatsRepository.GetCourseSummariesAsync(row.Id, style);

		// Maps that switched between stages and stages-as-checkpoints keep both kinds of courses - show the active one
		bool stagesAsCheckpoints = settings.TryGetValue(Map.SettingStagesAsCheckpoints, out var value) && value == "1";
		bool hasStages = rows.Any(r => r.Kind == CourseKind.Stage);
		var courses = rows
			.Where(r => stagesAsCheckpoints ? r.Kind != CourseKind.Stage : !(hasStages && r.Kind == CourseKind.Checkpoint))
			.Select(r => new CourseSummary(r.Kind, r.Number, r.Id, r.Tier, r.Name, r.Completions, r.WrTime, r.WrHolder))
			.ToList();

		short tier = courses.FirstOrDefault(c => c.Kind == CourseKind.Map)?.Tier ?? 0;
		return new MapTarget(row.Id, row.Name, false, tier, authors.Count > 0 ? string.Join(", ", authors) : null, row.Ranked,
			PlayerRepository.ToUnix(row.CreatedAt), PlayerRepository.ToUnix(row.LastPlayedAt), row.WorkshopId, courses);
	}

	private void ShowMapPanel(Player player, MapTarget target, MapTab tab, short? number)
	{
		var sections = new List<PanelSection>();
		var tabs = new List<MapTab>();
		void Add(MapTab t, string name, Func<PanelPage> root)
		{
			sections.Add(new PanelSection(name, null, root));
			tabs.Add(t);
		}

		Add(MapTab.Stats, "Map", () => MapStatsPage(target));
		if (target.Course(CourseKind.Map, 0) is { } mapCourse)
			Add(MapTab.Records, "Records", () => MapBoardPage(target, mapCourse));
		if (target.Of(CourseKind.Stage).Count > 0)
			Add(MapTab.Stages, "Stages", () => MapCourseListPage(target, CourseKind.Stage, "Stages"));
		if (target.Of(CourseKind.Checkpoint).Count > 0)
			Add(MapTab.Checkpoints, "Checkpoints", () => MapCourseListPage(target, CourseKind.Checkpoint, "Checkpoints"));
		if (target.Of(CourseKind.Bonus).Count > 0)
			Add(MapTab.Bonuses, "Bonuses", () => MapCourseListPage(target, CourseKind.Bonus, "Bonuses"));

		var session = new PanelSession($"Map · {target.Name}", player, sections);
		int index = tabs.IndexOf(tab);
		if (index < 0)
		{
			index = 0;
			session.Status = $"{target.Name} has no {tab.ToString().ToLowerInvariant()}";
		}
		session.ActiveTab = index;

		CourseKind? kind = tab switch
		{
			MapTab.Stages => CourseKind.Stage,
			MapTab.Checkpoints => CourseKind.Checkpoint,
			MapTab.Bonuses => CourseKind.Bonus,
			_ => null,
		};
		if (number != null && kind != null && tabs[index] == tab)
		{
			if (target.Course(kind.Value, number.Value) is { } course)
				session.Stack.Add(MapBoardPage(target, course));
			else
				session.Status = $"{CourseLabel(kind.Value, number.Value)} doesn't exist";
		}

		PanelReopen(session);
	}

	// ---- Pages ----

	private PanelPage MapStatsPage(MapTarget target) => new("Map", ctx =>
	{
		int stages = target.Of(CourseKind.Stage).Count;
		int checkpoints = target.Of(CourseKind.Checkpoint).Count;
		int bonuses = target.Of(CourseKind.Bonus).Count;
		string type = stages > 0 ? $"Staged · {stages} stages"
			: checkpoints > 0 ? $"Linear · {checkpoints - 1} checkpoints"
			: "Linear";
		if (bonuses > 0)
			type += $" · {bonuses} {(bonuses == 1 ? "bonus" : "bonuses")}";

		var rows = new List<HudMenuItem>
		{
			PanelContext.Info("Tier", target.Tier > 0 ? $"T{target.Tier}" : "none", type),
			PanelContext.Info("Author", target.Author ?? "unknown"),
			PanelContext.Info("Ranked", target.Ranked ? "yes" : "no"),
			PanelContext.Info("Added", FormatDate(target.DateAdded),
				target.IsCurrent ? "playing now" : $"last played {FormatRelative(target.LastPlayed)}"),
		};
		if (target.WorkshopId is { } workshopId)
			rows.Add(PanelContext.Info("Workshop id", workshopId.ToString(CultureInfo.InvariantCulture)));

		if (target.Course(CourseKind.Map, 0) is not { } course)
			return rows;

		rows.Add(course.WrTime is { } wr
			? ctx.Nav("World record", AdminFormat.Time(wr), course.WrHolder ?? "?", () => MapBoardPage(target, course))
			: PanelContext.Info("World record", "none yet"));

		int style = ctx.Style;
		int playerId = ctx.Player.Profile.ID;
		var stats = ctx.Load("stats", () => MapStatsRepository.GetMapStatsAsync(target.Id, course.CourseId, style, playerId));
		if (stats == null)
		{
			if (ctx.IsLoading("stats"))
				rows.Add(PanelContext.LoadingRow());
			return rows;
		}

		if (stats.Mine is { } mine)
		{
			string gap = course.WrTime is { } wrTime && mine.RunTime > wrTime ? $" · +{AdminFormat.Time(mine.RunTime - wrTime)}" : "";
			rows.Add(ctx.Nav("Your PB", AdminFormat.Time(mine.RunTime), $"#{mine.Rank} of {mine.TotalCount}{gap}",
				() => MapTimePage(target, mine.Id, course)));
		}
		else
		{
			rows.Add(PanelContext.Info("Your PB", "no time yet"));
		}

		rows.Add(PanelContext.Info("Completions", AdminFormat.Number(stats.Completions),
			$"{AdminFormat.Number(stats.UniquePlayers)} players on any course"));
		rows.Add(PanelContext.Info("Runs finished", $"{AdminFormat.Number(stats.RunsFinished)} / {AdminFormat.Number(stats.RunsStarted)}",
			$"finished / started · {AdminFormat.Number(stats.FinishesWeek)} in the last 7 days"));
		rows.Add(PanelContext.Info("Average time", stats.AverageTime is { } average ? AdminFormat.Time((int)average) : "-",
			stats.MedianTime is { } median ? $"median {AdminFormat.Time(median)}" : ""));
		rows.Add(PanelContext.Info("Average sync",
			stats.AverageSync is { } sync ? $"{sync.ToString("0.0", CultureInfo.InvariantCulture)}%" : "-"));
		return rows;
	});

	private PanelPage MapCourseListPage(MapTarget target, CourseKind kind, string title) => new(title, ctx =>
		target.Of(kind).Select(course =>
		{
			var sub = new List<string>();
			if (!string.IsNullOrEmpty(course.Name))
				sub.Add(course.Name);
			if (course.WrHolder != null)
				sub.Add(course.WrHolder);
			sub.Add($"{course.Completions} {(course.Completions == 1 ? "completion" : "completions")}");
			if (kind == CourseKind.Bonus && course.Tier is > 0)
				sub.Add($"T{course.Tier}");

			return ctx.Nav(CourseLabel(course.Kind, course.Number), course.WrTime is { } wr ? AdminFormat.Time(wr) : "no times",
				string.Join(" · ", sub), () => MapBoardPage(target, course));
		}).ToList());

	private sealed class MapBoardData(List<TimeRepository.BoardRow> rows)
	{
		internal List<TimeRepository.BoardRow> Rows { get; } = rows;
	}

	/// <summary>
	/// A course's leaderboard, 50 times at a time, with the viewer's own PB on top.
	/// </summary>
	private PanelPage MapBoardPage(MapTarget target, CourseSummary course)
	{
		int limit = BoardPageSize;
		return new PanelPage(CourseLabel(course.Kind, course.Number), ctx =>
		{
			int style = ctx.Style;
			int playerId = ctx.Player.Profile.ID;
			var board = ctx.Load("board", async () => new MapBoardData(await TimeRepository.GetLeaderboardAsync(course.CourseId, style, 0, limit)));
			var mine = ctx.Load("mine", () => TimeRepository.GetPlayerTimeAsync(course.CourseId, style, playerId));

			var rows = new List<HudMenuItem>();
			if (mine != null)
				rows.Add(ctx.Nav($"You  #{mine.Rank} of {mine.TotalCount}", AdminFormat.Time(mine.RunTime), AdminFormat.Date(mine.UpdatedAt),
					() => MapTimePage(target, mine.Id, course)));
			else if (!ctx.IsLoading("mine"))
				rows.Add(PanelContext.Info("You", "no time yet"));

			if (board == null)
			{
				rows.Add(PanelContext.LoadingRow());
				return rows;
			}
			if (board.Rows.Count == 0)
			{
				rows.Add(PanelContext.Info("No times yet"));
				return rows;
			}

			int? wr = board.Rows[0].RunTime;
			foreach (var r in board.Rows)
			{
				string you = r.PlayerId == playerId ? "  (you)" : "";
				string sub = r.Rank > 1 && wr is { } wrTime ? $"+{AdminFormat.Time(r.RunTime - wrTime)} · {AdminFormat.Date(r.UpdatedAt)}"
					: AdminFormat.Date(r.UpdatedAt);
				rows.Add(ctx.Nav($"#{r.Rank}  {r.PlayerName}{you}", AdminFormat.Time(r.RunTime), sub, () => MapTimePage(target, r.Id, course)));
			}

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

	/// <summary>
	/// One time: details, its replay (current map only) and the holder's profile.
	/// </summary>
	private PanelPage MapTimePage(MapTarget target, int timeId, CourseSummary course) => new("Time", ctx =>
	{
		var time = ctx.Load("time", () => TimeRepository.GetTimeAsync(timeId));
		if (time == null)
			return ctx.IsLoading("time") ? [PanelContext.LoadingRow()] : [PanelContext.Info("This time no longer exists")];

		string label = CourseLabel(course.Kind, course.Number);
		string owner = time.PlayerName ?? "?";
		float startSpeed = MathF.Sqrt(time.StartVelX * time.StartVelX + time.StartVelY * time.StartVelY);
		float endSpeed = MathF.Sqrt(time.EndVelX * time.EndVelX + time.EndVelY * time.EndVelY);

		var rows = new List<HudMenuItem>
		{
			PanelContext.Info(owner, AdminFormat.Time(time.RunTime), $"#{time.Rank} of {time.TotalCount} · {label}"),
			PanelContext.Info("Set", AdminFormat.Date(time.UpdatedAt), AdminFormat.Ago(time.UpdatedAt) + " ago"),
			PanelContext.Info("Sync", time.Sync.HasValue ? $"{time.Sync.Value.ToString("0.00", CultureInfo.InvariantCulture)}%" : "N/A"),
			PanelContext.Info("Start / end speed", $"{startSpeed:0} / {endSpeed:0} u/s"),
		};

		if (target.IsCurrent && CurrentMap != null && CurrentMap.ID == time.MapId)
		{
			short type = CourseKinds.ToRunType(course.Kind);
			short number = course.Kind == CourseKind.Map ? (short)0 : course.Number;
			var wrTemplate = time.Rank == 1 ? WrTemplateFor(type, number, time.Style) : null;

			if (wrTemplate != null && wrTemplate.MapTimeID == time.Id && wrTemplate.Frames.Count > 0)
			{
				rows.Add(ctx.Act("Play replay", "", "world record bot", () => _ = HandleWrReplaySelection(ctx.Player.Controller, wrTemplate), closes: true));
			}
			else if (time.ReplayId != null)
			{
				var pb = new PbReplayRef(time.Id, time.ReplayId, time.RunTime, (int)time.Rank);
				int ownerId = time.PlayerId;
				int style = time.Style;
				rows.Add(ctx.Act("Play replay", "", "", () =>
					_ = HandlePbReplaySelection(ctx.Player.Controller, pb, type, number, style, ownerName: owner, ownerId: ownerId), closes: true));
			}
			else
			{
				rows.Add(PanelContext.Info("Replay", "none"));
			}
		}

		int profileId = time.PlayerId;
		rows.Add(ctx.Act("Open profile", owner, "", () => OpenProfileById(ctx.Player.Controller, profileId), closes: true));
		return rows;
	});
}
