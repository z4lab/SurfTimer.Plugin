using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// What map filters check on a player: their targetname (filter_activator_name, given by triggers with
/// "!activator AddOutput targetname X") and response contexts (filter_activator_context, AddContext). Maps gate
/// later parts with it so they can't be skipped - savelocs, resumed runs and the timer's start teleports carry or
/// reset it (README: Map filters). Classname and pending delayed outputs aren't reachable from a plugin.
/// </summary>
internal sealed record FilterState(string Name, IReadOnlyList<(string Name, string Value)> Contexts)
{
	/// <summary>A player's state on spawn: no name, no contexts</summary>
	internal static readonly FilterState Default = new("", []);

	internal static FilterState Capture(CCSPlayerPawn pawn)
	{
		var contexts = new List<(string, string)>();
		foreach (var context in pawn.ResponseContexts)
		{
			if (!string.IsNullOrEmpty(context.Name))
				contexts.Add((context.Name, context.Value ?? ""));
		}
		return new FilterState(pawn.Entity?.Name ?? "", contexts);
	}

	/// <summary>Gives the pawn this name and exactly these contexts</summary>
	internal void Apply(CCSPlayerPawn pawn)
	{
		if (!pawn.IsValid || pawn.Entity == null)
			return;

		if ((pawn.Entity.Name ?? "") != Name)
			pawn.Entity.Name = Name;

		// Contexts only change when they differ - most maps never use them
		var current = Capture(pawn).Contexts;
		if (current.SequenceEqual(Contexts))
			return;
		if (current.Count > 0)
			pawn.AcceptInput("ClearContext", pawn, pawn, "");
		foreach (var (name, value) in Contexts)
			pawn.AcceptInput("AddContext", pawn, pawn, $"{name}:{value}");
	}

	/// <summary>"name 'stage3'", "no name" - for admin messages</summary>
	internal string Describe() =>
		(Name.Length > 0 ? $"name '{Name}'" : "no name")
		+ (Contexts.Count > 0 ? $", contexts {string.Join(", ", Contexts.Select(c => $"{c.Name}:{c.Value}"))}" : "");

	internal bool SameAs(FilterState other) => Name == other.Name && Contexts.SequenceEqual(other.Contexts);
}
