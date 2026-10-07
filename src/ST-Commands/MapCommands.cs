using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

public partial class SurfTimer
{
	// !map and the record boards (!mtop, !stop, ...) are in MapInfoPages.cs

	[ConsoleCommand("css_changemap", "Change to a map from the workshop collection - without a name it opens the map list.")]
	[CommandHelper(usage: "[map name]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void ChangeMap(CCSPlayerController? player, CommandInfo command)
	{
		// Players need @css/changemap (root has all), the console always may
		if (player != null && !AdminPermissions.Has(player, AdminPermissions.ChangeMap))
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["admin_no_access"]}");
			return;
		}

		if (command.ArgCount < 2 || command.GetArg(1).Trim().Length == 0)
		{
			if (player != null && playerList.TryGetValue(player.UserId ?? 0, out var admin))
				OpenAdminChangeMap(admin);
			else
				command.ReplyToCommand($"{Config.PluginPrefix} Usage: css_changemap <map name>");
			return;
		}

		string mapName = command.GetArg(1).Trim();
		if (!IsValidMapName(mapName))
		{
			command.ReplyToCommand($"{Config.PluginPrefix} Invalid map name '{mapName}'.");
			return;
		}

		// On the server's map list (a part of the name is enough when it's unique)
		if (CheckMapName(mapName, out string resolved) is { } error)
		{
			command.ReplyToCommand($"{Config.PluginPrefix} {error}");
			return;
		}
		mapName = resolved;

		ChangeLevelTo(mapName); // ServerPages.cs - also used by the admin panel
	}
}
