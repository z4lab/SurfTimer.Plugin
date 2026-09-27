using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// One pickable row of a menu.
/// </summary>
/// <param name="Text">Main text, e.g. "Stage 3 WR" or a player name</param>
/// <param name="OnSelect">Runs on the main thread after the popup has closed</param>
/// <param name="Sub">Dim secondary text next to it, e.g. the record holder</param>
/// <param name="Right">Right-aligned value - re-evaluated while the popup is open, so it can be live</param>
internal sealed record HudMenuItem(string Text, Action<CCSPlayerController> OnSelect, string Sub = "", Func<string>? Right = null)
{
	internal string RightText()
	{
		try
		{
			return Right?.Invoke() ?? "";
		}
		catch
		{
			return ""; // A stale target (e.g. a player who left) must not break the menu
		}
	}
}

internal sealed record HudMenuTab(string Name, List<HudMenuItem> Items);

/// <summary>
/// A menu for the centre popup (custom HUD) or, without it, the classic chat menu - see MenuPresenter.
/// Tabs without items are dropped; a single tab shows no tab row.
/// </summary>
internal sealed class HudMenu
{
	internal string Title { get; }
	internal List<HudMenuTab> Tabs { get; }

	internal HudMenu(string title, IEnumerable<HudMenuTab> tabs)
	{
		Title = title;
		Tabs = tabs.Where(t => t.Items.Count > 0).Take(CustomHud.MenuTabCount).ToList();
	}

	internal bool IsEmpty => Tabs.Count == 0;
}
