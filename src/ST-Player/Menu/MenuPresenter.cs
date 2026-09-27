using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;

namespace SurfTimer;

/// <summary>
/// Shows a HudMenu: as the clickable centre popup when the custom HUD is active (and popup_menus is
/// on), otherwise as the classic chat menu (!1, !2, ...) with the same entries.
/// </summary>
internal static class MenuPresenter
{
	internal static void Show(Player player, HudMenu menu)
	{
		if (menu.IsEmpty || !player.Controller.IsValid)
			return;

		if (Config.PopupMenus && CustomHud.IsActive)
		{
			player.HUD.OpenMenu(menu);
			return;
		}

		// Chat fallback: tabs in order, one line per item
		var chatMenu = new ChatMenu(menu.Title);
		foreach (var item in menu.Tabs.SelectMany(t => t.Items))
		{
			string line = string.Join(" - ", new[] { item.Text, item.Sub, item.RightText() }.Where(s => !string.IsNullOrEmpty(s)));
			chatMenu.AddMenuOption(line, (p, _) => item.OnSelect(p));
		}
		chatMenu.Open(player.Controller);
	}
}
