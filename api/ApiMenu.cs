using CounterStrikeSharp.API.Core;

namespace SurfTimer.Api;

/// <summary>How a row looks: danger rows are red, On / Off rows show their value as a toggle chip</summary>
public enum ApiMenuItemStyle
{
	Normal,
	Danger,
	On,
	Off,
}

/// <summary>
/// One row of a menu - pickable, or an info row (OnSelect null).
/// </summary>
/// <param name="Text">Main text</param>
/// <param name="OnSelect">Runs on the main thread when picked (after the popup closed, unless KeepOpen)</param>
/// <param name="Sub">Dim text next to it</param>
/// <param name="Right">Right-aligned value - re-evaluated while the popup is open, so it can be live</param>
public sealed record ApiMenuItem(string Text, Action<CCSPlayerController>? OnSelect, string Sub = "", Func<string>? Right = null)
{
	/// <summary>The popup stays open when this row is picked</summary>
	public bool KeepOpen { get; init; }

	public ApiMenuItemStyle Style { get; init; }

	/// <summary>Picking this row opens a sub-page (shown as "›")</summary>
	public bool Opens { get; init; }

	public static ApiMenuItem Info(string text, string value = "", string sub = "") => new(text, null, sub, () => value);
}

public sealed record ApiMenuTab(string Name, List<ApiMenuItem> Items);

/// <summary>
/// A menu for the centre popup or, without the custom HUD, the chat menu. Tabs without items are dropped; a single
/// tab shows no tab row.
/// </summary>
public sealed class ApiMenu(string title, List<ApiMenuTab> tabs)
{
	public string Title { get; } = title;
	public List<ApiMenuTab> Tabs { get; } = tabs;

	/// <summary>Line under the title</summary>
	public string Status { get; init; } = "";

	public int ActiveTab { get; init; }

	/// <summary>The header's back button - hidden when null</summary>
	public Action<CCSPlayerController>? OnBack { get; init; }

	/// <summary>The player switched to another tab (index into Tabs)</summary>
	public Action<CCSPlayerController, int>? OnTabChanged { get; init; }

	public ApiMenu(string title, List<ApiMenuItem> items) : this(title, [new ApiMenuTab("", items)]) { }
}
