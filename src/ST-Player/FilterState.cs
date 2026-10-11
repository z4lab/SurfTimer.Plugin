using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// What map filters check on a player: their targetname (filter_activator_name, given by triggers with
/// "!activator AddOutput targetname X"). Maps gate later parts with it so they can't be skipped - savelocs, resumed
/// runs and the timer's start teleports carry or reset it (README: Map filters). Response contexts can't be read
/// (CounterStrikeSharp's networked vectors only support handles), classname and pending delayed outputs aren't
/// reachable from a plugin.
/// </summary>
internal sealed record FilterState(string Name)
{
	/// <summary>A player's state on spawn: no name</summary>
	internal static readonly FilterState Default = new("");

	internal static FilterState Capture(CCSPlayerPawn pawn) => new(pawn.Entity?.Name ?? "");

	/// <summary>
	/// Gives the pawn this name - the way maps do it (AddOutput targetname): the engine keeps names as symbols, so
	/// writing CEntityIdentity.Name directly isn't seen by the map's filters.
	/// </summary>
	internal void Apply(CCSPlayerPawn pawn)
	{
		if (!pawn.IsValid || pawn.Entity == null)
			return;
		if ((pawn.Entity.Name ?? "") == Name)
			return;

		pawn.AcceptInput("AddOutput", pawn, pawn, $"targetname {Name}");
		if ((pawn.Entity.Name ?? "") != Name)
			Logger.LogWarning("[{ClassName}] Setting the targetname '{Name}' didn't take (it's '{Now}')",
				nameof(FilterState), Name, pawn.Entity.Name ?? "");
	}

	/// <summary>"name 'stage3'", "no name" - for admin messages</summary>
	internal string Describe() => Name.Length > 0 ? $"name '{Name}'" : "no name";

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MapFilters");
}
