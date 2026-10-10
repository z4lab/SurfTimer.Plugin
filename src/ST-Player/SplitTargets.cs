using CounterStrikeSharp.API;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Splits of the map run at a leaderboard rank (#10, a group's last rank, the rank above a player) for the
/// HUD splits panel. Loaded the first time a rank is asked for, dropped whenever the map's records reload
/// (ranks moved). Main thread only.
/// </summary>
internal sealed class SplitTargets
{
	/// <param name="RunTime">The run's time (ticks)</param>
	/// <param name="ReplayId">Its stored replay - the live pace matches positions on it</param>
	internal sealed record Target(int Rank, string Holder, Dictionary<int, CheckpointEntity> Splits, int RunTime, int? ReplayId);

	private readonly Dictionary<(int Style, int Rank), Target?> _loaded = new();
	private readonly HashSet<(int Style, int Rank)> _loading = new();
	private int _version;

	internal void Clear()
	{
		_loaded.Clear();
		_loading.Clear();
		_version++; // Loads still running belong to the old ranks
	}

	/// <summary>
	/// The run at that rank of the map course - null while it loads (started here) or when there's none.
	/// </summary>
	internal Target? Get(int courseId, int style, int rank)
	{
		var key = (style, rank);
		if (_loaded.TryGetValue(key, out var target))
			return target;
		if (courseId <= 0 || rank < 1 || !_loading.Add(key))
			return null;

		int version = _version;
		_ = Task.Run(async () =>
		{
			Target? result = null;
			try
			{
				var row = await TimeRepository.GetTimeAtRankAsync(courseId, style, rank);
				if (row != null)
					result = new Target(rank, row.PlayerName, await TimeRepository.GetSplitsAsync(row.Id), row.RunTime, row.ReplayId);
			}
			catch (Exception ex)
			{
				// Cached as "none" until the next reload - no retry every HUD update
				Logger.LogError(ex, "[SplitTargets] Loading the splits of rank {Rank} (course {CourseId}, style {Style}) failed", rank, courseId, style);
			}

			Server.NextFrame(() =>
			{
				if (version != _version)
					return;
				_loading.Remove(key);
				_loaded[key] = result;
			});
		});
		return null;
	}

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SplitTargets");
}
