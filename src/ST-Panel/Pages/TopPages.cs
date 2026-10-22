using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

/// <summary>
/// !top - players by server rank (normal style) in the panel popup, 50 at a time like the record boards.
/// A player opens their profile.
/// </summary>
public partial class SurfTimer
{
	[ConsoleCommand("css_top", "Top players by server rank")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void TopCommand(CCSPlayerController? controller, CommandInfo command)
	{
		if (controller == null || !playerList.TryGetValue(controller.UserId ?? 0, out var player))
			return;

		PanelReopen(new PanelSession("Top players", player, [new PanelSection("Server rank", null, TopPage)]));
	}

	private sealed class TopData(List<PlayerRepository.TopRow> rows)
	{
		internal List<PlayerRepository.TopRow> Rows { get; } = rows;
	}

	private PanelPage TopPage()
	{
		int limit = BoardPageSize;
		return new PanelPage("Server rank", ctx =>
		{
			var profile = ctx.Player.Profile;
			var rows = new List<HudMenuItem>
			{
				PanelContext.Info("You", profile.ServerRank is int rank ? $"#{rank}" : "unranked",
					profile.ServerRank != null ? $"{AdminFormat.Number(profile.ServerPoints)} points" : ""),
			};

			var top = ctx.Load("top", async () => new TopData(await PlayerRepository.GetTopAsync(limit)));
			if (top == null)
			{
				rows.Add(PanelContext.LoadingRow());
				return rows;
			}
			if (top.Rows.Count == 0)
			{
				rows.Add(PanelContext.Info("No ranked players yet"));
				return rows;
			}

			int playerId = profile.ID;
			foreach (var r in top.Rows)
			{
				int id = r.PlayerId;
				string you = id == playerId ? "  (you)" : "";
				rows.Add(ctx.Act($"#{r.Rank}  {r.Name ?? "?"}{you}", AdminFormat.Number(r.Points), r.CountryCode ?? "",
					() => OpenProfileById(ctx.Player.Controller, id), closes: true));
			}

			if (top.Rows.Count >= limit)
			{
				rows.Add(ctx.Act("Load more", "", $"showing {limit}", () =>
				{
					limit += BoardPageSize;
					ctx.Reload("top");
				}));
			}
			return rows;
		});
	}
}
