using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

public partial class SurfTimer
{
	// !map and the record boards (!mtop, !stop, ...) are in MapInfoPages.cs

	[ConsoleCommand("css_changemap", "Change to a map from the workshop collection.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(minArgs: 1, usage: "<map name>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void ChangeMap(CCSPlayerController? player, CommandInfo command)
	{
		string mapName = command.GetArg(1).Trim();
		if (!IsValidMapName(mapName))
		{
			command.ReplyToCommand($"{Config.PluginPrefix} Invalid map name '{mapName}'.");
			return;
		}

		ChangeLevelTo(mapName); // ServerPages.cs - also used by the admin panel
	}
}
