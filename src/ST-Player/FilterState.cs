using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// What map filters check on a player: their targetname (filter_activator_name, given by triggers with
/// "!activator AddOutput targetname X"). Maps gate later parts with it so they can't be skipped - savelocs, resumed
/// runs and the timer's start teleports carry or reset it (README: Map filters). !mapfilters shows what a map
/// really checks - CS2 maps can't set names with AddOutput any more, so many use other filters.
/// </summary>
internal sealed record FilterState(string Name)
{
	/// <summary>A player's state on spawn: no name</summary>
	internal static readonly FilterState Default = new("");

	internal static FilterState Capture(CCSPlayerPawn pawn) => new(pawn.Entity?.Name ?? "");

	/// <summary>
	/// Gives the pawn this name. Written directly: Source 2 dropped AddOutput keyvalue changes (AddOutput targetname X
	/// does nothing in CS2).
	/// </summary>
	internal void Apply(CCSPlayerPawn pawn)
	{
		if (!pawn.IsValid || pawn.Entity == null)
			return;
		if ((pawn.Entity.Name ?? "") != Name)
			pawn.Entity.Name = Name;
	}

	/// <summary>"name 'stage3'", "no name" - for admin messages</summary>
	internal string Describe() => Name.Length > 0 ? $"name '{Name}'" : "no name";
}
