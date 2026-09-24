using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using SurfTimer.Shared.DTO;
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
			CurrentMap.Author!,
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

	[ConsoleCommand("css_amt", "Set the Tier of the map.")]
	[ConsoleCommand("css_addmaptier", "Set the Tier of the map.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(minArgs: 1, usage: "<Tier Number> [1-8]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void AddMapTier(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		short tier;
		try
		{
			tier = short.Parse(command.ArgByIndex(1));
		}
		catch (System.Exception)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage",
				"!amt <tier> [1-8]"]}"
			);
			return;
		}

		if (tier > 8)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage",
				"!amt <tier> [1-8]"]}"
			);
			return;
		}

		var mapInfo = new MapDto
		{
			Name = CurrentMap.Name!,
			Author = CurrentMap.Author!,
			Tier = tier,
			Stages = CurrentMap.Stages,
			Bonuses = CurrentMap.Bonuses,
			Ranked = CurrentMap.Ranked,
			LastPlayed = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		};

		CurrentMap.Tier = tier;

		Task.Run(async () =>
		{
			await _dataService!.UpdateMapInfoAsync(mapInfo, CurrentMap.ID);
		});

		string msg = $"{Config.PluginPrefix} {ChatColors.Yellow}{CurrentMap.Name}{ChatColors.Default} - Set Tier to {Extensions.GetTierColor(CurrentMap.Tier)}{CurrentMap.Tier}{ChatColors.Default}.";

		player.PrintToChat(msg);
	}

	[ConsoleCommand("css_amn", "Set the Name of the map author.")]
	[ConsoleCommand("css_addmappername", "Set the Name of the map author.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(minArgs: 1, usage: "<Author Name>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void AddMapAuthor(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		string author = command.ArgString.Trim();

		// Validate: letters, numbers, intervals, dashes and up to 50 symbols
		if (string.IsNullOrWhiteSpace(author) || author.Length > 50 || !Regex.IsMatch(author, @"^[\w\s\-\.]+$"))
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage",
				"!amn <author name>"]}"
			);
			return;
		}

		var mapInfo = new MapDto
		{
			Name = CurrentMap.Name!,
			Author = author,
			Tier = CurrentMap.Tier,
			Stages = CurrentMap.Stages,
			Bonuses = CurrentMap.Bonuses,
			Ranked = CurrentMap.Ranked,
			LastPlayed = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		};

		CurrentMap.Author = author;

		Task.Run(async () =>
		{
			await _dataService!.UpdateMapInfoAsync(mapInfo, CurrentMap.ID);
		});

		string msg = $"{Config.PluginPrefix} {ChatColors.Yellow}{CurrentMap.Name}{ChatColors.Default} - Set Author to {ChatColors.Green}{CurrentMap.Author}{ChatColors.Default}.";

		player.PrintToChat(msg);
	}

	[ConsoleCommand("css_amr", "Set the Ranked option of the map.")]
	[ConsoleCommand("css_addmapranked", "Set the Ranked option of the map.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void AddMapRanked(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		if (CurrentMap.Ranked)
			CurrentMap.Ranked = false;
		else
			CurrentMap.Ranked = true;

		var mapInfo = new MapDto
		{
			Name = CurrentMap.Name!,
			Author = CurrentMap.Author!,
			Tier = CurrentMap.Tier,
			Stages = CurrentMap.Stages,
			Bonuses = CurrentMap.Bonuses,
			Ranked = CurrentMap.Ranked,
			LastPlayed = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		};

		Task.Run(async () =>
		{
			await _dataService!.UpdateMapInfoAsync(mapInfo, CurrentMap.ID);
		});

		string msg = $"{Config.PluginPrefix} {ChatColors.Yellow}{CurrentMap.Name}{ChatColors.Default} - Set Ranked to {(CurrentMap.Ranked ? ChatColors.Green : ChatColors.Red)}{CurrentMap.Ranked}{ChatColors.Default}.";

		player.PrintToChat(msg);
	}

	[ConsoleCommand("css_listtriggers", "Server console: list every trigger_multiple with name, zone type and bounds.")]
	[CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
	public void ListTriggers(CCSPlayerController? player, CommandInfo command)
	{
		var triggers = Utilities.FindAllEntitiesByDesignerName<CBaseTrigger>("trigger_multiple")
			.Select(t => (Trigger: t, Name: t.Entity?.Name ?? ""))
			.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		command.ReplyToCommand($"[{Config.PluginName}] {triggers.Count} trigger_multiple on {CurrentMap.Name}:");
		foreach (var (trigger, name) in triggers)
		{
			string zone = ZoneName.TryParse(name, out var type, out var number) ? $"{type} {number}" : "-";
			command.ReplyToCommand(
				$"  #{trigger.Index,-5} name='{(string.IsNullOrEmpty(name) ? "<unnamed>" : name)}' zone={zone} " +
				$"origin=({trigger.AbsOrigin}) mins=({trigger.Collision.Mins}) maxs=({trigger.Collision.Maxs})");
		}
	}

	[ConsoleCommand("css_triggers", "List all registered zones in the map.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void Triggers(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		player.PrintToChat($"{Config.PluginPrefix} {CurrentMap.Zones.Values.Sum(z => z.Count)} zone triggers | Stages: {CurrentMap.Stages} | Bonuses: {CurrentMap.Bonuses} | Checkpoints: {CurrentMap.TotalCheckpoints}");
		foreach (var ((type, number), zones) in CurrentMap.Zones.OrderBy(z => z.Key.Type).ThenBy(z => z.Key.Number))
		{
			player.PrintToChat($"{type} {number} ({zones.Count}x):");
			foreach (var zone in zones)
				player.PrintToChat($"  #{zone.TriggerIndex} '{zone.Name}' -> teleport {zone.Teleport}{(zone.Angles is { } angles ? $" angles {angles}" : "")}");
		}
	}
}
