using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// !mapfilters (map admins): how this map gates players - its filter entities and the triggers using them - plus the
/// caller's own contexts and name. Printed line by line to the caller's console and the server log (README: Map
/// filters). The triggers' outputs aren't listed: reading entity I/O connections crashed the server (CS2 layout differs
/// from CounterStrikeSharp's schema classes) - a source viewer shows them.
/// </summary>
public partial class SurfTimer
{
	[ConsoleCommand("css_mapfilters", "Map admins: print the map's filters and the triggers using them (console + server log)")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void MapFiltersCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null && !AdminPermissions.Has(player, AdminPermissions.Map))
			return;

		int count = 0;
		// Each line goes out right away - if something fails, what came before is there
		void Print(string line)
		{
			count++;
			player?.PrintToConsole(line);
			_logger.LogInformation("[MapFilters] {Line}", line);
		}

		Print($"---- map filters: {CurrentMap?.Name} ----");
		try
		{
			DumpMapFilters(Print, player);
		}
		catch (Exception ex)
		{
			Print($"(dump failed: {ex.Message})");
		}
		player?.PrintToChat($"{Config.PluginPrefix} Map filters printed to your console ({count} lines).");
	}

	private static void DumpMapFilters(Action<string> print, CCSPlayerController? player)
	{
		// The caller's own state
		if (player?.PlayerPawn.Value is { IsValid: true } pawn)
		{
			print($"you: targetname '{pawn.Entity?.Name}', classname '{pawn.DesignerName}'");
			print($"you: contexts [{string.Join(", ", FilterState.Capture(pawn).ContextStrings())}]");
		}

		var entities = Utilities.GetAllEntities().Where(e => e != null && e.IsValid).ToList();

		// Filters
		var filters = entities.Where(e => e.DesignerName.StartsWith("filter_", StringComparison.Ordinal)).ToList();
		print($"filters: {filters.Count}");
		foreach (var entity in filters)
			print($"  {entity.DesignerName} '{entity.Entity?.Name}': {DescribeFilter(entity)}");

		// Triggers that use a filter
		var triggers = entities.Where(e => e.DesignerName.StartsWith("trigger_", StringComparison.Ordinal)).ToList();
		var filtered = triggers.Select(e => (Entity: e, Filter: new CBaseTrigger(e.Handle).FilterName))
			.Where(t => !string.IsNullOrEmpty(t.Filter)).ToList();
		print($"triggers: {triggers.Count}, with a filter: {filtered.Count}");
		foreach (var (entity, filter) in filtered)
			print($"  {entity.DesignerName} '{entity.Entity?.Name}' filter '{filter}'{Where(entity)}");
	}

	private static string DescribeFilter(CEntityInstance entity)
	{
		string mode = new CBaseFilter(entity.Handle).Negated ? "disallow" : "allow";
		string what = entity.DesignerName switch
		{
			"filter_activator_name" => $"name '{new CFilterName(entity.Handle).FilterName}'",
			"filter_activator_context" => $"context '{new CFilterContext(entity.Handle).FilterContext}'",
			"filter_activator_class" => $"class '{new CFilterClass(entity.Handle).FilterClass}'",
			"filter_activator_attribute_int" => $"attribute '{new CFilterAttributeInt(entity.Handle).AttributeName}'",
			"filter_activator_model" => $"model '{new CFilterModel(entity.Handle).FilterModel}'",
			"filter_activator_team" => $"team {new CFilterTeam(entity.Handle).FilterTeam}",
			"filter_activator_mass_greater" => $"mass > {new CFilterMassGreater(entity.Handle).FilterMass}",
			"filter_multi" => $"multi type {new CFilterMultiple(entity.Handle).FilterType}: "
				+ string.Join(", ", new CFilterMultiple(entity.Handle).FilterName.ToArray().Where(n => !string.IsNullOrEmpty(n)).Select(n => $"'{n}'")),
			_ => "",
		};
		return $"{mode} {what}";
	}

	private static string Where(CEntityInstance entity) =>
		new CBaseEntity(entity.Handle).AbsOrigin is { } o ? $" at ({o.X:0} {o.Y:0} {o.Z:0})" : "";
}
