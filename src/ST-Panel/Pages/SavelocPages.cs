using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

/// <summary>
/// !saveloclist / !slm - the saveloc menu: your set (cursor marked, save / prev / next), the latest savelocs of
/// everyone (loading one joins its set) and the session's info. Rows teleport and close the popup.
/// </summary>
public partial class SurfTimer
{
	private const int RecentSavelocs = 50;

	[ConsoleCommand("css_saveloclist", "Saveloc menu: your set and everyone's latest savelocs")]
	[ConsoleCommand("css_slm", "Saveloc menu: your set and everyone's latest savelocs")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void SavelocMenuCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var p) || CurrentMap == null)
			return;

		var session = new PanelSession("Savelocs", p,
		[
			new("My set", null, SavelocSetPage),
			new("Recent", null, SavelocRecentPage),
			new("Import", null, SavelocImportPage),
			new("Info", null, SavelocInfoPage),
		]);
		PanelReopen(session);
	}

	private HudMenuItem SavelocRow(PanelContext ctx, Saveloc saveloc, bool current)
	{
		string who = saveloc.Source == SavelocSource.Replay || saveloc.Source == SavelocSource.Player
			? $"{saveloc.OwnerName} · of {saveloc.SourceName}"
			: saveloc.OwnerName;
		int id = saveloc.Id;
		return ctx.Act($"#{id}  {saveloc.Describe()}", current ? "current" : "", who, () => TeleToId(ctx.Player, id), closes: true) with
		{
			Style = current ? HudMenuItemStyle.On : HudMenuItemStyle.Normal,
		};
	}

	private PanelPage SavelocSetPage() => new("My set", ctx =>
	{
		var session = CurrentMap?.Savelocs;
		if (session == null)
			return [PanelContext.Info("No map loaded")];

		var set = session.SetOf(ctx.Player.Controller.SteamID);
		var rows = new List<HudMenuItem>
		{
			ctx.Act("Save here", $"next #{session.NextId}", ctx.Player.Controller.PawnIsAlive ? "your position" : "the player / bot you spectate",
				() => SaveLocation(ctx.Player)),
		};
		if (set.Ids.Count == 0)
		{
			rows.Add(PanelContext.Info("No savelocs in your set", "", "!saveloc, or load someone's from Recent"));
			return rows;
		}

		rows.Add(ctx.Act("Previous", "", "!teleprev", () => TeleStep(ctx.Player, -1), closes: true));
		rows.Add(ctx.Act("Next", "", "!telenext", () => TeleStep(ctx.Player, 1), closes: true));
		for (int i = set.Ids.Count - 1; i >= 0; i--)
		{
			if (session.All.TryGetValue(set.Ids[i], out var saveloc))
				rows.Add(SavelocRow(ctx, saveloc, i == set.Cursor));
		}
		return rows;
	});

	private PanelPage SavelocRecentPage() => new("Recent", ctx =>
	{
		var session = CurrentMap?.Savelocs;
		if (session == null || session.All.Count == 0)
			return [PanelContext.Info("No savelocs on this map yet")];

		int? current = session.SetOf(ctx.Player.Controller.SteamID).Current;
		return session.All.Values
			.OrderByDescending(s => s.Id)
			.Take(RecentSavelocs)
			.Select(s => SavelocRow(ctx, s, s.Id == current))
			.ToList();
	});

	/// <summary>
	/// Record runs as savelocs: the run start and every checkpoint / stage entry of the map WR or the viewer's
	/// own map PB (from their replays) - a new set, cursor on the start, !telenext walks the run.
	/// </summary>
	private PanelPage SavelocImportPage() => new("Import", ctx =>
	{
		var map = CurrentMap;
		if (map == null)
			return [PanelContext.Info("No map loaded")];

		var rows = new List<HudMenuItem>();
		int style = ctx.Style;

		var wrTemplate = map.ReplayManager?.MapWR;
		var wr = map.WR.GetValueOrDefault(style);
		if (wr == null || wr.ID <= 0)
			rows.Add(PanelContext.Info("Map WR", "none yet"));
		else if (wrTemplate == null || wrTemplate.Frames.Count == 0 || style != 0)
			rows.Add(PanelContext.Info("Map WR", PlayerHud.FormatTime(wr.RunTime), "no replay stored"));
		else
		{
			string holder = wr.Name ?? "?";
			rows.Add(ctx.Act("Map WR", PlayerHud.FormatTime(wr.RunTime), $"{holder} · start + every checkpoint", () =>
				ctx.Session.Status = ImportRecordSavelocs(ctx.Player, wrTemplate, wr.Checkpoints, $"WR of {holder}")));
		}

		var pb = ctx.Player.Stats.PB.GetValueOrDefault(style);
		if (pb == null || pb.ID <= 0)
			rows.Add(PanelContext.Info("My PB", "no time yet"));
		else if (pb.ReplayId is not int replayId)
			rows.Add(PanelContext.Info("My PB", PlayerHud.FormatTime(pb.RunTime), "no replay stored"));
		else
		{
			var splits = pb.Checkpoints;
			rows.Add(ctx.Act("My PB", PlayerHud.FormatTime(pb.RunTime), "start + every checkpoint", () =>
			{
				List<ReplayFrame>? frames = null;
				ctx.Run("Loading your PB replay…", async () =>
				{
					var data = await TimeRepository.GetReplayDataAsync(replayId);
					frames = data != null ? await Task.Run(() => ReplayCodec.Decode(data)) : null;
					return frames is { Count: > 0 } ? "" : "Your PB replay couldn't be loaded";
				}, status =>
				{
					if (frames is { Count: > 0 } && CurrentMap == map)
					{
						var template = new ReplayPlayer { Type = 0, Stage = 0, Style = style, Frames = frames };
						ctx.Session.Status = ImportRecordSavelocs(ctx.Player, template, splits, "my PB");
					}
					PanelRefresh(ctx.Session);
				});
			}));
		}
		return rows;
	});

	/// <summary>Creates the import savelocs and makes them the player's set - returns the status line</summary>
	private string ImportRecordSavelocs(Player player, ReplayPlayer replay, Dictionary<int, CheckpointEntity>? splits, string source)
	{
		var session = CurrentMap!.Savelocs;
		var (start, end) = replay.GetRunWindow();
		var enter = CurrentMap.Stages > 0 ? ReplayFrameSituation.STAGE_ZONE_ENTER : ReplayFrameSituation.CHECKPOINT_ZONE_ENTER;

		var points = new List<int> { start };
		for (int i = start + 1; i < end && i < replay.Frames.Count; i++)
		{
			if (replay.Frames[i].Situation == enter)
				points.Add(i);
		}

		if (session.All.Count + points.Count > Config.SavelocLimit)
			return LocalizationService.LocalizerNonNull["saveloc_limit", Config.SavelocLimit];

		var savelocs = new List<Saveloc>();
		foreach (int frame in points)
		{
			var saveloc = Saveloc.FromReplayFrame(session.NextId, player, replay, frame, splits, source);
			if (saveloc == null)
				continue;
			session.TakeId();
			savelocs.Add(saveloc);
		}
		if (savelocs.Count == 0)
			return "Nothing to import - the replay has no run";

		session.ReplaceSet(player.Controller.SteamID, savelocs);
		return $"Imported {savelocs.Count} savelocs (#{savelocs[0].Id}-#{savelocs[^1].Id}) from {source} - !tele, then !telenext";
	}

	private PanelPage SavelocInfoPage() => new("Info", ctx =>
	{
		var session = CurrentMap?.Savelocs;
		if (session == null)
			return [PanelContext.Info("No map loaded")];

		var set = session.SetOf(ctx.Player.Controller.SteamID);
		return
		[
			PanelContext.Info("Session", session.SessionId.ToString()[..8], "new on every map start"),
			PanelContext.Info("Savelocs", $"{session.All.Count} / {Config.SavelocLimit}", "all players, this map"),
			PanelContext.Info("Your set", set.Ids.Count.ToString(), set.Current is int id ? $"on #{id}" : ""),
			PanelContext.Info("Commands", "!sl  !tele #id", "!teleprev  !telenext  !slm"),
		];
	});
}
