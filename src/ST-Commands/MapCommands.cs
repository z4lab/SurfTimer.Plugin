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

	[ConsoleCommand("css_amt", "Set the tier of the map, or of one of its stages / bonuses.")]
	[ConsoleCommand("css_addmaptier", "Set the tier of the map, or of one of its stages / bonuses.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(minArgs: 1, usage: "<tier 0-8> [stage|bonus <number>]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void AddMapTier(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		string usage = $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage", "!amt <tier 0-8> [stage|bonus <number>]"]}";
		if (!byte.TryParse(command.GetArg(1), out byte tier) || tier > 8)
		{
			player.PrintToChat(usage);
			return;
		}

		// The map itself, or a stage / bonus course (e.g. "!amt 4 bonus 2")
		CourseKind kind = CourseKind.Map;
		short number = 0;
		if (command.ArgCount >= 4)
		{
			kind = command.GetArg(2).ToLowerInvariant() switch
			{
				"stage" or "s" => CourseKind.Stage,
				"bonus" or "b" => CourseKind.Bonus,
				_ => CourseKind.Map,
			};
			if (kind == CourseKind.Map || !short.TryParse(command.GetArg(3), out number))
			{
				player.PrintToChat(usage);
				return;
			}
		}

		var course = CurrentMap.Course(kind, number);
		if (course == null)
		{
			player.PrintToChat($"{Config.PluginPrefix} This map has no {kind.ToString().ToLowerInvariant()} {number}.");
			return;
		}

		byte? newTier = tier == 0 ? null : tier; // 0 = unset
		CurrentMap.SetCourseTier(kind, number, newTier);

		int courseId = course.Id;
		int mapId = CurrentMap.ID;
		Task.Run(async () =>
		{
			await MapRepository.SetCourseTierAsync(courseId, newTier);
			// Points depend on the tier
			foreach (int style in Config.Styles)
				await PointsService.RecalculateMapAsync(mapId, style);
		});

		string target = kind == CourseKind.Map ? CurrentMap.Name! : $"{CurrentMap.Name} {kind.ToString().ToLowerInvariant()} {number}";
		player.PrintToChat($"{Config.PluginPrefix} {ChatColors.Yellow}{target}{ChatColors.Default} - Set Tier to {Extensions.GetTierColor(tier)}{tier}{ChatColors.Default}.");
	}

	[ConsoleCommand("css_amn", "Set the map's author(s), comma separated.")]
	[ConsoleCommand("css_addmappername", "Set the map's author(s), comma separated.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(minArgs: 1, usage: "<author>[, <author> ...]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void AddMapAuthor(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		var authors = command.ArgString.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

		// Letters, numbers, spaces, dashes and dots, up to 64 characters each
		if (authors.Count == 0 || authors.Count > 16 || authors.Any(a => a.Length > 64 || !Regex.IsMatch(a, @"^[\w\s\-\.]+$")))
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage", "!amn <author>[, <author> ...]"]}");
			return;
		}

		CurrentMap.Author = string.Join(", ", authors);

		int mapId = CurrentMap.ID;
		Task.Run(() => MapRepository.SetAuthorsAsync(mapId, authors));

		player.PrintToChat($"{Config.PluginPrefix} {ChatColors.Yellow}{CurrentMap.Name}{ChatColors.Default} - Set Author to {ChatColors.Green}{CurrentMap.Author}{ChatColors.Default}.");
	}

	[ConsoleCommand("css_amr", "Toggle whether the map is ranked (gives points).")]
	[ConsoleCommand("css_addmapranked", "Toggle whether the map is ranked (gives points).")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void AddMapRanked(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		CurrentMap.Ranked = !CurrentMap.Ranked;

		bool ranked = CurrentMap.Ranked;
		int mapId = CurrentMap.ID;
		Task.Run(async () =>
		{
			await MapRepository.SetRankedAsync(mapId, ranked);
			// Only ranked maps give points
			foreach (int style in Config.Styles)
				await PointsService.RecalculateMapAsync(mapId, style);
		});

		player.PrintToChat($"{Config.PluginPrefix} {ChatColors.Yellow}{CurrentMap.Name}{ChatColors.Default} - Set Ranked to {(ranked ? ChatColors.Green : ChatColors.Red)}{ranked}{ChatColors.Default}.");
	}

	[ConsoleCommand("css_mapsetting", "List, set or remove a setting of this map (e.g. staged_linear 1).")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(usage: "[key] [value]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void MapSetting(CCSPlayerController? player, CommandInfo command)
	{
		if (CurrentMap == null || CurrentMap.ID <= 0)
			return;

		// No arguments: list
		if (command.ArgCount < 2)
		{
			command.ReplyToCommand($"{Config.PluginPrefix} {CurrentMap.Name}: {CurrentMap.Settings.Count} setting(s)");
			foreach (var (key, value) in CurrentMap.Settings.OrderBy(s => s.Key))
				command.ReplyToCommand($"  {key} = {value}");
			return;
		}

		string settingKey = command.GetArg(1).ToLowerInvariant();
		if (!Regex.IsMatch(settingKey, "^[a-z0-9_]{1,64}$"))
		{
			command.ReplyToCommand($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage", "!mapsetting [key] [value]"]}");
			return;
		}

		int mapId = CurrentMap.ID;
		if (command.ArgCount < 3)
		{
			// Key only: remove
			CurrentMap.Settings.Remove(settingKey);
			Task.Run(() => MapRepository.DeleteSettingAsync(mapId, settingKey));
			command.ReplyToCommand($"{Config.PluginPrefix} {CurrentMap.Name}: removed {settingKey}");
		}
		else
		{
			string value = string.Join(' ', Enumerable.Range(2, command.ArgCount - 2).Select(command.GetArg)).Trim();
			if (value.Length > 255)
				value = value[..255];

			CurrentMap.Settings[settingKey] = value;
			Task.Run(() => MapRepository.SetSettingAsync(mapId, settingKey, value));
			command.ReplyToCommand($"{Config.PluginPrefix} {CurrentMap.Name}: {settingKey} = {value}");
		}

		CurrentMap.ApplySettings();
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

	[ConsoleCommand("css_recalcpoints", "Recalculate everyone's points on every map.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void RecalculatePoints(CCSPlayerController? player, CommandInfo command)
	{
		command.ReplyToCommand($"{Config.PluginPrefix} Recalculating points for all maps...");

		Task.Run(async () =>
		{
			int maps = await PointsService.RecalculateAllAsync();
			// Back on the main thread for chat / console output
			Server.NextFrame(() =>
			{
				string done = $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["points_recalculated", maps]}";
				if (player != null && player.IsValid)
					player.PrintToChat(done);
				else
					Server.PrintToConsole(done);
			});
		});
	}

	[ConsoleCommand("css_map", "Change to a map from the workshop collection.")]
	[ConsoleCommand("css_changemap", "Change to a map from the workshop collection.")]
	[RequiresPermissions("@css/root")]
	[CommandHelper(minArgs: 1, usage: "<map name>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void ChangeMap(CCSPlayerController? player, CommandInfo command)
	{
		string mapName = command.GetArg(1).Trim();

		// Only a map name - it goes into a server command, so nothing like "surf_x; quit" gets through
		if (!Regex.IsMatch(mapName, "^[A-Za-z0-9_\\-]+$"))
		{
			command.ReplyToCommand($"{Config.PluginPrefix} Invalid map name '{mapName}'.");
			return;
		}

		Server.PrintToChatAll($"{Config.PluginPrefix} Changing map to {ChatColors.Green}{mapName}{ChatColors.Default}...");

		// A moment for the chat message to arrive; ds_workshop_changelevel needs the map in the
		// server's workshop collection (host_workshop_collection)
		AddTimer(2.0f, () => Server.ExecuteCommand($"ds_workshop_changelevel {mapName}"));
	}
}
