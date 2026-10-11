using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Map filter state (FilterState) around the timer: the timer's teleports to a start give the player that start's state,
/// teleports back to a stage start mid-run the state they left it with, and a run started with another name than the
/// map's configured start name gets that name (README: Map filters).
/// </summary>
public partial class SurfTimer
{
	/// <summary>The state a run from this start begins with - null when the map keeps names (filter_reset 0)</summary>
	internal static FilterState? StartFilterOf(ZoneType type, short number) => CurrentMap?.FilterStartState(type, number);

	/// <summary>
	/// The state for a teleport back to the start of the stage / bonus the player is in (!rs, teleport-back zones): the
	/// course start's for a bonus, a stage run or the map start; the one they left that stage start with during a map run
	/// (null - kept as is - when this run didn't pass it, e.g. after a saveloc).
	/// </summary>
	private static FilterState? CurrentStartFilterOf(Player p, ZoneType type, short number)
	{
		if (type == ZoneType.StageStart && !p.Timer.IsStageMode && p.Timer.IsRunning)
			return CurrentMap?.FilterResets == true ? p.StageFilters.GetValueOrDefault(number) : null;
		return StartFilterOf(type, number);
	}

	/// <summary>A map run leaves a stage start: !rs back to it gives this state again</summary>
	private static void RecordStageFilter(Player player, short stage)
	{
		if (player.Timer.IsRunning && !player.Timer.IsStageMode && player.Controller.PlayerPawn.Value is { IsValid: true } pawn)
			player.StageFilters[stage] = FilterState.Capture(pawn);
	}

	/// <summary>
	/// A run starts (its start zone left): on maps with a configured start name, a player with another name (walked back
	/// in from a later part, which the map's start doesn't reset) gets the start's state - logged.
	/// </summary>
	private static void CheckRunFilter(Player player, ZoneType type, short number)
	{
		player.StageFilters.Clear();
		var map = CurrentMap;
		if (map == null || !map.HasFilterStartName(type, number) || map.FilterStartState(type, number) is not FilterState start)
			return;
		if (player.Controller.PlayerPawn.Value is not { IsValid: true } pawn)
			return;

		var current = FilterState.Capture(pawn);
		if (current.Name == start.Name)
			return;

		start.Apply(pawn);
		FilterLogger.LogInformation("[{ClassName}] {Player} started a run on {Map} ({Zone} {Number}) with {Had} - set to {Start}",
			nameof(SurfTimer), player.Controller.PlayerName, map.Name, type, number, current.Describe(), start.Describe());
	}

	private static ILogger? _filterLogger;
	private static ILogger FilterLogger => _filterLogger ??=
		ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MapFilters");
}
