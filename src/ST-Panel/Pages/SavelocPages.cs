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
