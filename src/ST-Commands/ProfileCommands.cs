using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using SurfTimer.Data;

namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>Who a profile is about - an online player's profile or a DB row for offline players.</summary>
	private sealed record ProfileTarget(int Id, string Name, string Country, int JoinDate, int LastSeen, int Connections, bool Online);

	[ConsoleCommand("css_profile", "Show your profile or another player's (online or offline)")]
	[ConsoleCommand("css_p", "Show your profile or another player's (online or offline)")]
	[CommandHelper(usage: "[name]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void ShowProfile(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var viewer))
			return;

		string search = command.ArgCount > 1 ? command.ArgString.Trim() : "";
		int style = viewer.Timer.Style;
		int currentMapId = CurrentMap?.ID ?? 0;

		// Online players first (self when no name given), otherwise the database
		Player? online = search.Length == 0
			? viewer
			: playerList.Values.FirstOrDefault(p => p.Controller.IsValid
				&& p.Controller.PlayerName.Contains(search, StringComparison.OrdinalIgnoreCase));

		ProfileTarget? onlineTarget = online == null ? null : new ProfileTarget(online.Profile.ID, online.Profile.Name ?? online.Controller.PlayerName,
			online.Profile.Country ?? "", online.Profile.JoinDate, online.Profile.LastSeen, online.Profile.Connections, true);

		Task.Run(async () =>
		{
			try
			{
				var target = onlineTarget;
				if (target == null)
				{
					var row = await ProfileRepository.FindPlayerByNameAsync(search);
					if (row != null)
						target = new ProfileTarget(row.ID, row.Name ?? search, row.Country ?? "", row.JoinDate, row.LastSeen, row.Connections, false);
				}

				if (target == null)
				{
					Server.NextFrame(() =>
					{
						if (player.IsValid)
							player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["profile_not_found", search]}");
					});
					return;
				}

				var data = await ProfileRepository.LoadAsync(target.Id, style, currentMapId);

				// The menu reads map / replay state - main thread
				Server.NextFrame(() =>
				{
					if (!player.IsValid || !playerList.TryGetValue(player.UserId ?? 0, out var stillViewer))
						return;
					MenuPresenter.Show(stillViewer, BuildProfileMenu(target, data, style));
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[{ClassName}] Loading the profile for '{Search}' failed", nameof(SurfTimer), search);
			}
		});
	}

	private HudMenu BuildProfileMenu(ProfileTarget target, ProfileData data, int style)
	{
		string title = string.IsNullOrEmpty(target.Country) ? $"Profile · {target.Name}" : $"Profile · {target.Name} · {target.Country}";

		return new HudMenu(title,
		[
			new HudMenuTab("Overview", ProfileOverview(target, data)),
			new HudMenuTab("Points", ProfilePoints(data)),
			new HudMenuTab("Records", data.Records.Select(r => ProfileRunRow(target, r, style, showRank: false)).ToList()),
			new HudMenuTab("Recent", data.Recent.Select(r => ProfileRunRow(target, r, style, showRank: true)).ToList()),
			new HudMenuTab("This map", ProfileThisMap(target, data, style)),
			new HudMenuTab("Tiers", data.Tiers.Select(t =>
				HudMenuItem.Info($"Tier {t.Tier}", $"{t.Completed} / {t.Total}  ·  {Percent(t.Completed, t.Total)}")).ToList()),
		]);
	}

	private static List<HudMenuItem> ProfileOverview(ProfileTarget target, ProfileData data)
	{
		var maps = data.CountsFor(0);
		var bonuses = data.CountsFor(1);
		var stages = data.CountsFor(2);
		var checkpoints = data.CountsFor(3);
		long records = maps.Records + bonuses.Records + stages.Records + checkpoints.Records;

		var rows = new List<HudMenuItem>
		{
			HudMenuItem.Info("Server rank", data.Points.Points > 0 ? $"#{data.Points.ServerRank} of {data.Points.RankedPlayers}" : "unranked"),
			HudMenuItem.Info("Points", data.Points.Points.ToString("N0", CultureInfo.InvariantCulture)),
			HudMenuItem.Info("Maps completed", $"{maps.Completions} / {data.RankedTotals.Maps}  ·  {Percent(maps.Completions, data.RankedTotals.Maps)}"),
		};

		if (data.RankedTotals.Stages > 0)
			rows.Add(HudMenuItem.Info("Stages completed", $"{stages.Completions} / {data.RankedTotals.Stages}  ·  {Percent(stages.Completions, data.RankedTotals.Stages)}"));
		if (data.RankedTotals.Bonuses > 0)
			rows.Add(HudMenuItem.Info("Bonuses completed", $"{bonuses.Completions} / {data.RankedTotals.Bonuses}  ·  {Percent(bonuses.Completions, data.RankedTotals.Bonuses)}"));

		rows.Add(HudMenuItem.Info("World records", records.ToString(),
			$"map {maps.Records} · stage {stages.Records} · bonus {bonuses.Records} · cp {checkpoints.Records}"));
		rows.Add(HudMenuItem.Info("Top 10 finishes", maps.Top10.ToString(), "maps"));
		rows.Add(HudMenuItem.Info("Average sync", data.Extras.AverageSync is { } sync ? $"{sync.ToString("0.0", CultureInfo.InvariantCulture)}%" : "-", "map & bonus runs"));
		rows.Add(HudMenuItem.Info("Runs finished", $"{data.Extras.RunsFinished} / {data.Extras.RunsStarted}", "finished / started"));
		rows.Add(HudMenuItem.Info("Playtime", FormatPlaytime(data.Points.PlayTime)));
		rows.Add(HudMenuItem.Info("Joined", FormatDate(target.JoinDate)));
		rows.Add(HudMenuItem.Info("Last seen", target.Online ? "online now" : FormatRelative(target.LastSeen), $"{target.Connections} visits"));

		return rows;
	}

	private static List<HudMenuItem> ProfilePoints(ProfileData data)
	{
		var b = data.PointsBuckets;
		static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

		return
		[
			HudMenuItem.Info("Map completions", N(b.MapPoints), "by tier"),
			HudMenuItem.Info("Map WRs", N(b.WrPoints)),
			HudMenuItem.Info("Map top 10", N(b.Top10Points), "ranks 2-10"),
			HudMenuItem.Info("Map groups", N(b.GroupPoints), "ranks 11+ (G1-G5)"),
			HudMenuItem.Info("Bonus WRs", N(b.BonusWrPoints)),
			HudMenuItem.Info("Bonus ranks", N(b.BonusPoints), "ranks 2+"),
			HudMenuItem.Info("Stage / CP WRs", N(b.SegmentWrPoints)),
			HudMenuItem.Info("Total", N(data.Points.Points), "ranked maps only"),
		];
	}

	/// <summary>
	/// A run in the Records / Recent tabs - on the current map it plays that run's replay.
	/// </summary>
	private HudMenuItem ProfileRunRow(ProfileTarget target, ProfileData.RunRow run, int style, bool showRank)
	{
		string kind = RunKindLabel(run.Type, run.Stage);
		string sub = showRank
			? $"{kind} · #{run.Rank} of {run.Total} · {FormatDate(run.RunDate)}"
			: $"{kind} WR";
		string right = FormatRunTime(run.RunTime, run.Sync);

		Action<CCSPlayerController>? onSelect = null;
		if (CurrentMap != null && run.MapId == CurrentMap.ID)
			onSelect = ReplayActionFor(target, run.Id, run.Type, run.Stage, style, isRecord: run.Rank == 1);

		return new HudMenuItem(run.MapName, onSelect, sub, () => right);
	}

	/// <summary>
	/// Every map / stage / bonus / checkpoint segment of the current map, with the target's PB or
	/// "not completed". PBs play their replay.
	/// </summary>
	private List<HudMenuItem> ProfileThisMap(ProfileTarget target, ProfileData data, int style)
	{
		var rows = new List<HudMenuItem>();
		if (CurrentMap == null)
			return rows;

		void Add(short type, short stage)
		{
			var run = data.CurrentMapRuns.FirstOrDefault(r => r.Type == type && r.Stage == stage);
			string kind = RunKindLabel(type, stage);
			if (run == null)
			{
				rows.Add(HudMenuItem.Info(kind, "not completed"));
				return;
			}

			string right = FormatRunTime(run.RunTime, run.Sync);
			rows.Add(new HudMenuItem(kind, ReplayActionFor(target, run.Id, type, stage, style, isRecord: run.Rank == 1),
				$"#{run.Rank} of {run.Total}", () => right));
		}

		Add(0, 0);
		for (short stage = 1; stage <= CurrentMap.Stages; stage++)
			Add(2, stage);
		for (short bonus = 1; bonus <= CurrentMap.Bonuses; bonus++)
			Add(1, bonus);
		for (short cp = 1; cp <= CurrentMap.CheckpointSegments; cp++)
			Add(3, cp);

		return rows;
	}

	/// <summary>
	/// Plays a run of the current map: the loaded WR replay for a record, otherwise that PB's replay.
	/// </summary>
	private Action<CCSPlayerController> ReplayActionFor(ProfileTarget target, int mapTimeId, short type, short stage, int style, bool isRecord)
	{
		if (isRecord)
		{
			var template = WrTemplateFor(type, stage, style);
			if (template != null && template.MapTimeID == mapTimeId && template.Frames.Count > 0)
				return p => _ = HandleWrReplaySelection(p, template);
		}

		return p => _ = HandlePbReplaySelection(p, new PersonalBest { ID = mapTimeId, Type = type }, type, stage, style,
			ownerName: target.Name, ownerId: target.Id);
	}

	private static ReplayPlayer? WrTemplateFor(short type, short stage, int style)
	{
		var replays = CurrentMap.ReplayManager;
		Dictionary<int, ReplayPlayer>[]? byNumber = type switch
		{
			1 => replays.AllBonusWR,
			2 => replays.AllStageWR,
			3 => replays.AllCheckpointWR,
			_ => null,
		};

		if (type == 0)
			return replays.MapWR;
		if (byNumber == null || stage < 1 || stage >= byNumber.Length || byNumber[stage] == null)
			return null;
		return byNumber[stage].TryGetValue(style, out var template) ? template : null;
	}

	private static string RunKindLabel(short type, short stage) => type switch
	{
		0 => "Map",
		1 => $"Bonus {stage}",
		2 => $"Stage {stage}",
		3 => $"Checkpoint {stage}",
		_ => "Run",
	};

	private static string FormatRunTime(int ticks, decimal? sync)
	{
		string time = PlayerHud.FormatTime(ticks, PlayerTimer.TimeFormatStyle.Full);
		return sync is { } s ? $"{time} · {s.ToString("0.0", CultureInfo.InvariantCulture)}%" : time;
	}

	private static string Percent(long part, long total) =>
		total > 0 ? $"{(100.0 * part / total).ToString("0", CultureInfo.InvariantCulture)}%" : "-";

	private static string FormatPlaytime(long minutes) =>
		minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";

	private static string FormatDate(int unix) => unix <= 0
		? "-"
		: DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

	private static string FormatRelative(int unix)
	{
		if (unix <= 0)
			return "-";

		var ago = DateTime.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
		if (ago.TotalDays < 1)
			return "today";
		if (ago.TotalDays < 2)
			return "yesterday";
		if (ago.TotalDays < 30)
			return $"{(int)ago.TotalDays} days ago";
		return FormatDate(unix);
	}
}
