using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;

namespace SurfTimer;

/// <summary>
/// Shows a HudMenu: as the clickable centre popup when the custom HUD is active (and popup_menus is
/// on), otherwise as the classic chat menu (!1, !2, ...) with the same entries.
/// </summary>
internal static class MenuPresenter
{
	internal static bool UsesPopup => Config.PopupMenus && CustomHud.IsActive;

	internal static void Show(Player player, HudMenu menu)
	{
		if (menu.IsEmpty || !player.Controller.IsValid)
			return;

		if (UsesPopup)
		{
			player.HUD.OpenMenu(menu);
			return;
		}

		ShowChat(player, menu);
	}

	/// <summary>
	/// Replaces a menu that's already shown (after an action): in place in the popup, a fresh chat
	/// menu otherwise. Nothing happens when the player closed the popup meanwhile.
	/// </summary>
	internal static void Update(Player player, HudMenu menu, bool resetPage, Func<HudMenu?, bool> isSameMenu)
	{
		if (menu.IsEmpty || !player.Controller.IsValid)
			return;

		if (UsesPopup)
		{
			if (isSameMenu(player.HUD.Menu))
				player.HUD.UpdateMenu(menu, resetPage);
			return;
		}

		ShowChat(player, menu);
	}

	private static void ShowChat(Player player, HudMenu menu)
	{
		var chatMenu = new ChatMenu(string.IsNullOrEmpty(menu.Status) ? menu.Title : $"{menu.Title} - {menu.Status}");

		// Menus with navigation (back / tab callbacks): the active tab, then back and the other tabs.
		// Plain menus: all tabs in order, one line per item.
		bool navigable = menu.OnBack != null || menu.OnTabChanged != null;
		int activeTab = Math.Clamp(menu.ActiveTab, 0, menu.Tabs.Count - 1);
		var items = navigable ? menu.Tabs[activeTab].Items : menu.Tabs.SelectMany(t => t.Items);

		foreach (var item in items)
		{
			string line = string.Join(" - ", new[] { item.Text, item.Sub, item.RightText() }.Where(s => !string.IsNullOrEmpty(s)));
			// Info rows are shown but can't be picked
			chatMenu.AddMenuOption(line, (p, _) => item.OnSelect?.Invoke(p), disabled: item.OnSelect == null);
		}

		if (navigable)
		{
			if (menu.OnBack != null)
				chatMenu.AddMenuOption("« Back", (p, _) => menu.OnBack(p));
			if (menu.OnTabChanged != null)
			{
				for (int t = 0; t < menu.Tabs.Count; t++)
				{
					if (t == activeTab)
						continue;
					int tab = t;
					chatMenu.AddMenuOption($"» {menu.Tabs[t].Name}", (p, _) => menu.OnTabChanged(p, tab));
				}
			}
		}

		chatMenu.Open(player.Controller);
	}
}
