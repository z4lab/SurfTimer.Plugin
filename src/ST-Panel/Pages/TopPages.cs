using System.Globalization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

/// <summary>
/// !top - players by server rank (normal style) in the panel popup, 10 per page. Only the shown page is
/// loaded from the database. A player opens their profile.
/// </summary>
public partial class SurfTimer
{
	private const int TopPageSize = 10;

	[ConsoleCommand("css_top", "Top players by server rank")]
	[CommandHelper(usage: "[page]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void TopCommand(CCSPlayerController? controller, CommandInfo command)
	{
		if (controller == null || !playerList.TryGetValue(controller.UserId ?? 0, out var player))
			return;

		int page = command.ArgCount > 1 && int.TryParse(command.GetArg(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 1 ? n : 1;
		var session = new PanelSession("Top players", player, [new PanelSection("Server rank", null, () => TopPage(page))]);
		PanelReopen(session);
	}

	private sealed class TopData(List<PlayerRepository.TopRow> rows, long total)
	{
		internal List<PlayerRepository.TopRow> Rows { get; } = rows;
		internal long Total { get; } = total;
	}

	private PanelPage TopPage(int page)
	{
		int offset = (page - 1) * TopPageSize;
		return new PanelPage("Server rank", ctx =>
		{
			var profile = ctx.Player.Profile;
			var rows = new List<HudMenuItem>
			{
				PanelContext.Info("You", profile.ServerRank is int rank ? $"#{rank}" : "unranked",
					profile.ServerRank != null ? $"{AdminFormat.Number(profile.ServerPoints)} points" : ""),
			};

			var data = ctx.Load("page", async () =>
			{
				var (top, total) = await PlayerRepository.GetTopAsync(offset, TopPageSize);
				return new TopData(top, total);
			});
			if (data == null)
			{
				rows.Add(PanelContext.LoadingRow());
				return rows;
			}
			if (data.Rows.Count == 0)
			{
				rows.Add(PanelContext.Info(offset == 0 ? "No ranked players yet" : "No players on this page"));
				if (offset > 0)
					rows.Add(TopPageRow(ctx, "First page", 0, data.Total, () => offset = 0));
				return rows;
			}

			int playerId = profile.ID;
			foreach (var r in data.Rows)
			{
				int id = r.PlayerId;
				string you = id == playerId ? "  (you)" : "";
				rows.Add(ctx.Act($"#{r.Rank}  {r.Name ?? "?"}{you}", AdminFormat.Number(r.Points), r.CountryCode ?? "",
					() => OpenProfileById(ctx.Player.Controller, id), closes: true));
			}

			if (offset > 0)
				rows.Add(TopPageRow(ctx, "Previous page", offset - TopPageSize, data.Total, () => offset -= TopPageSize));
			if (offset + TopPageSize < data.Total)
				rows.Add(TopPageRow(ctx, "Next page", offset + TopPageSize, data.Total, () => offset += TopPageSize));
			return rows;
		});
	}

	/// <summary>A paging row: shows the ranks it goes to, then loads only that page</summary>
	private static HudMenuItem TopPageRow(PanelContext ctx, string text, int to, long total, Action move)
	{
		long pages = (total + TopPageSize - 1) / TopPageSize;
		return ctx.Act(text, $"{to + 1}-{Math.Min(to + TopPageSize, total)}", $"page {to / TopPageSize + 1} of {pages}", () =>
		{
			move();
			ctx.Reload("page");
		});
	}
}
