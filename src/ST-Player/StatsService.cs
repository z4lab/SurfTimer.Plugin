using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurfTimer.Shared.Sql;

namespace SurfTimer;

/// <summary>
/// Writes a player's buffered stats - playtime (PlayerStats.play_time, minutes) and runs started /
/// finished (PlayerAttempts) - in batches: every minute, on disconnect and on map end.
/// </summary>
internal static class StatsService
{
	internal const int FlushIntervalTicks = 64 * 60;

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StatsService");

	/// <summary>
	/// Takes what's pending on the main thread and writes it in the background.
	/// </summary>
	/// <param name="final">Leaving (disconnect / map end) - leftover seconds are rounded to a minute</param>
	internal static void Flush(Player player, bool final)
	{
		if (player.Profile.ID <= 0 || SurfTimer.CurrentMap == null || SurfTimer.CurrentMap.ID <= 0)
			return;

		int mapId = SurfTimer.CurrentMap.ID;
		int style = player.Timer.Style;

		// Whole minutes only while playing - the rest carries over to the next flush
		var now = DateTime.UtcNow;
		double elapsed = (now - player.PlaytimeCountedUntil).TotalMinutes;
		int minutes = final ? (int)Math.Round(elapsed) : (int)Math.Floor(elapsed);
		player.PlaytimeCountedUntil = final ? now : player.PlaytimeCountedUntil.AddMinutes(minutes);

		var attempts = player.PendingAttempts.ToList();
		player.PendingAttempts.Clear();

		if (minutes <= 0 && attempts.Count == 0)
			return;

		int playerId = player.Profile.ID;
		Task.Run(async () =>
		{
			try
			{
				if (minutes > 0)
				{
					await SurfTimer.DB.ExecuteAsync(Queries.DB_QUERY_STATS_ADD_PLAYTIME,
						new { PlayerId = playerId, Style = style, Minutes = minutes });
				}

				foreach (var ((type, stage), (started, finished)) in attempts)
				{
					await SurfTimer.DB.ExecuteAsync(Queries.DB_QUERY_STATS_ADD_ATTEMPTS, new
					{
						PlayerId = playerId,
						MapId = mapId,
						Style = style,
						Type = type,
						Stage = stage,
						Started = started,
						Finished = finished,
					});
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "[StatsService] Saving playtime / attempts for player {PlayerId} failed", playerId);
			}
		});
	}
}
