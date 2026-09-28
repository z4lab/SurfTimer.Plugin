using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// How a row looks: danger rows are red (they open a confirmation), On / Off rows show their right
/// value as a toggle chip.
/// </summary>
internal enum HudMenuItemStyle
{
	Normal,
	Danger,
	On,
	Off,
}

/// <summary>
/// One row of a menu - pickable, or an info row (no action, not clickable).
/// </summary>
/// <param name="Text">Main text, e.g. "Stage 3 WR" or a player name</param>
/// <param name="OnSelect">Runs on the main thread (after the popup has closed unless KeepOpen) - null for an info row</param>
/// <param name="Sub">Dim secondary text next to it, e.g. the record holder</param>
/// <param name="Right">Right-aligned value - re-evaluated while the popup is open, so it can be live</param>
internal sealed record HudMenuItem(string Text, Action<CCSPlayerController>? OnSelect, string Sub = "", Func<string>? Right = null)
{
	/// <summary>
	/// The popup stays open when this row is picked - the action refreshes the menu itself.
	/// </summary>
	internal bool KeepOpen { get; init; }

	internal HudMenuItemStyle Style { get; init; }

	/// <summary>
	/// Picking this row opens a sub-page - shown as a "›" after the value.
	/// </summary>
	internal bool Opens { get; init; }

	/// <summary>
	/// A row that only shows something (label, optional detail, value).
	/// </summary>
	internal static HudMenuItem Info(string text, string value, string sub = "") => new(text, null, sub, () => value);

	internal string RightText()
	{
		string value;
		try
		{
			value = Right?.Invoke() ?? "";
		}
		catch
		{
			value = ""; // A stale target (e.g. a player who left) must not break the menu
		}
		return Opens ? (value.Length > 0 ? $"{value}  ›" : "›") : value;
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

	/// <summary>Line under the title - the result of the last action, "Loading…" etc.</summary>
	internal string Status { get; init; } = "";

	/// <summary>Tab shown when the menu is opened / updated</summary>
	internal int ActiveTab { get; init; }

	/// <summary>The header's back button - hidden when null (root pages)</summary>
	internal Action<CCSPlayerController>? OnBack { get; init; }

	/// <summary>Called after the player switched to another tab (index into Tabs)</summary>
	internal Action<CCSPlayerController, int>? OnTabChanged { get; init; }

	internal HudMenu(string title, IEnumerable<HudMenuTab> tabs)
	{
		Title = title;
		Tabs = tabs.Where(t => t.Items.Count > 0).Take(CustomHud.MenuTabCount).ToList();
	}

	internal bool IsEmpty => Tabs.Count == 0;
}
