using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// !mapfilters (map admins): how this map gates players - its filter entities, the triggers using them, and every
/// trigger / relay output aimed at the player (!activator, !player ...) - plus the caller's own name and contexts. Printed
/// to the caller's console and the server log, to see which state a map's filters check (README: Map filters).
/// </summary>
public partial class SurfTimer
{
	private const int MapFilterDumpMaxLines = 400;

	private static readonly string[] TriggerOutputs = ["OnStartTouch", "OnEndTouch", "OnStartTouchAll", "OnEndTouchAll", "OnTouching", "OnTrigger"];

	[ConsoleCommand("css_mapfilters", "Map admins: print the map's filters and what its triggers do to players (console + server log)")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void MapFiltersCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null && !AdminPermissions.Has(player, AdminPermissions.Map))
			return;

		var lines = new List<string> { $"---- map filters: {CurrentMap?.Name} ----" };
		try
		{
			DumpMapFilters(lines, player);
		}
		catch (Exception ex)
		{
			lines.Add($"(dump failed: {ex.Message})");
		}

		foreach (var line in lines.Take(MapFilterDumpMaxLines))
		{
			player?.PrintToConsole(line);
			_logger.LogInformation("[MapFilters] {Line}", line);
		}
		if (lines.Count > MapFilterDumpMaxLines)
			player?.PrintToConsole($"... {lines.Count - MapFilterDumpMaxLines} more lines in the server log");
		player?.PrintToChat($"{Config.PluginPrefix} Map filters printed to your console ({lines.Count} lines).");
	}

	private static void DumpMapFilters(List<string> lines, CCSPlayerController? player)
	{
		var entities = Utilities.GetAllEntities().Where(e => e != null && e.IsValid).ToList();

		// The caller's own state
		if (player?.PlayerPawn.Value is { IsValid: true } pawn)
		{
			lines.Add($"you: targetname '{pawn.Entity?.Name}', classname '{pawn.DesignerName}', response context '{pawn.ResponseContext}'");
			lines.Add($"you: contexts [{string.Join(", ", ReadContexts(pawn).Select(c => $"{c.Name}:{c.Value}"))}]");
		}

		// Filters
		var filters = entities.Where(e => e.DesignerName.StartsWith("filter_", StringComparison.Ordinal)).ToList();
		lines.Add($"filters: {filters.Count}");
		foreach (var entity in filters)
			lines.Add($"  {entity.DesignerName} '{entity.Entity?.Name}': {DescribeFilter(entity)}");

		// Triggers that use a filter
		var triggers = entities.Where(e => e.DesignerName.StartsWith("trigger_", StringComparison.Ordinal)).ToList();
		var filtered = triggers.Select(e => (Entity: e, Filter: new CBaseTrigger(e.Handle).FilterName)).Where(t => !string.IsNullOrEmpty(t.Filter)).ToList();
		lines.Add($"triggers: {triggers.Count}, with a filter: {filtered.Count}");
		foreach (var (entity, filter) in filtered)
			lines.Add($"  {entity.DesignerName} '{entity.Entity?.Name}' filter '{filter}'{Where(entity)}");

		// Outputs aimed at the player (or anything that changes names / contexts / attributes)
		lines.Add("outputs to the player / state changes:");
		var inputs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var entity in entities.Where(e => e.DesignerName.StartsWith("trigger_", StringComparison.Ordinal) || e.DesignerName.StartsWith("logic_", StringComparison.Ordinal)))
		{
			foreach (string output in TriggerOutputs)
			{
				foreach (var c in Connections(entity, output))
				{
					bool atPlayer = c.TargetDesc.StartsWith('!') || c.TargetDesc.Equals("player", StringComparison.OrdinalIgnoreCase);
					bool stateInput = c.TargetInput.Contains("Context", StringComparison.OrdinalIgnoreCase)
						|| c.TargetInput.Contains("Attribute", StringComparison.OrdinalIgnoreCase)
						|| c.TargetInput.Contains("Name", StringComparison.OrdinalIgnoreCase)
						|| c.TargetInput.Equals("AddOutput", StringComparison.OrdinalIgnoreCase)
						|| c.TargetInput.Equals("KeyValue", StringComparison.OrdinalIgnoreCase);
					if (!atPlayer && !stateInput)
						continue;
					inputs[c.TargetInput] = inputs.GetValueOrDefault(c.TargetInput) + 1;
					lines.Add($"  {entity.DesignerName} '{entity.Entity?.Name}' {output} -> {c.TargetDesc}.{c.TargetInput}('{c.ValueOverride}') delay {c.Delay:0.##}{Where(entity)}");
				}
			}
		}
		lines.Add($"inputs used: {string.Join(", ", inputs.OrderByDescending(i => i.Value).Select(i => $"{i.Key} x{i.Value}"))}");
	}

	private static string DescribeFilter(CEntityInstance entity)
	{
		var filter = new CBaseFilter(entity.Handle);
		string mode = filter.Negated ? "disallow" : "allow";
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

	private sealed record Connection(string TargetDesc, string TargetInput, string ValueOverride, float Delay);

	/// <summary>The I/O connections of one output field of an entity (none when its class doesn't have it)</summary>
	private static List<Connection> Connections(CEntityInstance entity, string output)
	{
		var result = new List<Connection>();
		string? className = output == "OnTrigger"
			? entity.DesignerName == "trigger_multiple" || entity.DesignerName == "trigger_once" ? "CTriggerMultiple"
				: entity.DesignerName == "logic_relay" ? "CLogicRelay" : null
			: entity.DesignerName.StartsWith("trigger_", StringComparison.Ordinal) ? "CBaseTrigger" : null;
		if (className == null)
			return result;

		int offset;
		try
		{
			offset = Schema.GetSchemaOffset(className, "m_" + output);
		}
		catch (Exception)
		{
			return result; // Not a field of that class
		}
		var io = new CEntityIOOutput(entity.Handle + offset);
		var c = io.Connections;
		for (int i = 0; i < 64 && c != null && c.Handle != IntPtr.Zero; i++, c = c.Next)
			result.Add(new Connection(c.TargetDesc ?? "", c.TargetInput ?? "", c.ValueOverride ?? "", c.Delay));
		return result;
	}

	/// <summary>
	/// The pawn's response contexts. CounterStrikeSharp's NetworkedVector only reads handles, so the CUtlVector is read
	/// directly: size at +0, element pointer at +8, ResponseContext_t elements.
	/// </summary>
	internal static List<(string Name, string Value)> ReadContexts(CBaseEntity entity)
	{
		var result = new List<(string, string)>();
		var vector = entity.ResponseContexts;
		int count = vector.Count;
		if (count <= 0 || count > 64)
			return result;

		IntPtr elements = Marshal.ReadIntPtr(vector.Handle + 8);
		int size = Schema.GetClassSize("ResponseContext_t");
		if (elements == IntPtr.Zero || size <= 0)
			return result;

		for (int i = 0; i < count; i++)
		{
			var context = new ResponseContext_t(elements + i * size);
			result.Add((context.Name ?? "", context.Value ?? ""));
		}
		return result;
	}
}
