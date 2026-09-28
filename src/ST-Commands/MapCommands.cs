using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.RegularExpressions;

namespace SurfTimer;

public partial class SurfTimer
{
	// All map-related commands here
	[ConsoleCommand("css_tier", "Display the current map tier.")]
	[ConsoleCommand("css_mapinfo", "Display the current map tier.")]
	[ConsoleCommand("css_mi", "Display the current map tier.")]
	[ConsoleCommand("css_difficulty", "Display the current map tier.")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void MapTier(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		char rankedColor = CurrentMap.Ranked ? ChatColors.Green : ChatColors.Red;
		string rankedStatus = CurrentMap.Ranked ? "Yes" : "No";

		string msg = $"{Config.PluginPrefix} " + LocalizationService.LocalizerNonNull["map_info",
			CurrentMap.Name!,
			$"{Extensions.GetTierColor(CurrentMap.Tier)}{CurrentMap.Tier}",
			CurrentMap.Author ?? "Unknown",
			$"{rankedColor}{rankedStatus}",
			DateTimeOffset.FromUnixTimeSeconds(CurrentMap.DateAdded).DateTime.ToString("dd.MM.yyyy HH:mm")
		];

		if (CurrentMap.Stages > 1)
		{
			msg += LocalizationService.LocalizerNonNull["map_info_stages", CurrentMap.Stages];
		}
		else
		{
			msg += LocalizationService.LocalizerNonNull["map_info_linear", CurrentMap.TotalCheckpoints];
		}

		if (CurrentMap.Bonuses > 0)
		{
			msg += LocalizationService.LocalizerNonNull["map_info_bonuses", CurrentMap.Bonuses];
		}

		player.PrintToChat(msg);
	}

	[ConsoleCommand("css_map", "Change to a map from the workshop collection.")]
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
