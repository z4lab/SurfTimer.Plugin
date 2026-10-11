using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace SurfTimer;

/// <summary>
/// What map filters check on a player: their response contexts (filter_activator_context - the CS2 way, e.g.
/// "!activator AddContext pass:1" and "ClearContext") and their targetname (filter_activator_name - CS:GO maps gave it
/// with AddOutput, which does nothing in CS2). Maps gate later parts with it so they can't be skipped - savelocs, resumed
/// runs and the timer's start teleports carry or reset it (README: Map filters). !mapfilters shows what a map checks.
/// </summary>
internal sealed class FilterState(string name, IReadOnlyList<(string Name, string Value)> contexts)
{
	internal string Name { get; } = name;
	internal IReadOnlyList<(string Name, string Value)> Contexts { get; } = contexts;

	/// <summary>A player's state on spawn: no name, no contexts</summary>
	internal static readonly FilterState Default = new("", []);

	internal static FilterState Capture(CCSPlayerPawn pawn) => new(pawn.Entity?.Name ?? "", ReadContexts(pawn));

	/// <summary>
	/// Gives the pawn this name and exactly these contexts. The name is written directly (no input sets it in CS2), the
	/// contexts through the inputs maps use.
	/// </summary>
	internal void Apply(CCSPlayerPawn pawn)
	{
		if (!pawn.IsValid || pawn.Entity == null)
			return;

		if ((pawn.Entity.Name ?? "") != Name)
			pawn.Entity.Name = Name;

		var current = ReadContexts(pawn);
		if (current.SequenceEqual(Contexts))
			return;
		if (current.Count > 0)
			pawn.AcceptInput("ClearContext", pawn, pawn, "");
		foreach (var (contextName, value) in Contexts)
			pawn.AcceptInput("AddContext", pawn, pawn, $"{contextName}:{value}");
	}

	/// <summary>"contexts pass:1, name 'x'", "nothing" - for admin messages</summary>
	internal string Describe()
	{
		var parts = new List<string>();
		if (Contexts.Count > 0)
			parts.Add($"contexts {string.Join(", ", ContextStrings())}");
		if (Name.Length > 0)
			parts.Add($"name '{Name}'");
		return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
	}

	/// <summary>"pass:1" entries, as stored in resumed runs</summary>
	internal List<string> ContextStrings() => Contexts.Select(c => $"{c.Name}:{c.Value}").ToList();

	internal static List<(string Name, string Value)> ParseContexts(IEnumerable<string>? entries) => (entries ?? [])
		.Select(c => c.Split(':', 2))
		.Where(parts => parts[0].Length > 0)
		.Select(parts => (parts[0], parts.Length > 1 ? parts[1] : ""))
		.ToList();

	/// <summary>
	/// An entity's response contexts. CounterStrikeSharp's NetworkedVector only reads handles, so the CUtlVector is read
	/// directly (hl2sdk cs2: int m_Size at +0, T* m_pMemory at +8) with ResponseContext_t elements.
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
			string contextName = context.Name ?? "";
			if (contextName.Length > 0)
				result.Add((contextName, context.Value ?? ""));
		}
		return result;
	}
}
