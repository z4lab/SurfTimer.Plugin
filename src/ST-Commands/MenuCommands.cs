using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

/// <summary>
/// The popup menu's cursor mode: !cursor toggles it (bindable: bind &lt;key&gt; css_cursor), !close closes the popup. ESC
/// never reaches the server - E or a movement key close the popup too (PlayerHud.TickMenuKeys).
/// </summary>
public partial class SurfTimer
{
	[ConsoleCommand("css_cursor", "Mouse cursor on / off - click the popup menu, or move with it open.")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void CursorCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || PlayerOf(player) is not { } target)
			return;

		if (!CustomHud.IsActive)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["cursor_no_hud"]}");
			return;
		}

		bool on = target.HUD.ToggleCursor();
		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull[on ? "cursor_on" : "cursor_off"]}");
	}

	[ConsoleCommand("css_close", "Close the popup menu (and the mouse cursor).")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void CloseMenuCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || PlayerOf(player) is not { } target)
			return;

		target.HUD.CloseMenu();
	}
}
