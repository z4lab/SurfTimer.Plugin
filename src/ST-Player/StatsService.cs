using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Writes a player's buffered stats in batches - every minute, on disconnect and on map end:
/// the session heartbeat (playtime survives a crash up to the last minute), runs started per course
/// (player_course_attempts), and on leaving closes the session.
/// </summary>
internal static class StatsService
{
	internal const int FlushIntervalTicks = 64 * 60;

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StatsService");

	private const string AddAttempts = @"
		INSERT INTO `{p}player_course_attempts` (`player_id`, `course_id`, `style_id`, `started`)
		VALUES (@PlayerId, @CourseId, @StyleId, @Started)
		ON DUPLICATE KEY UPDATE `started` = `started` + VALUES(`started`)";

	/// <summary>
	/// Takes what's pending on the main thread and writes it in the background.
	/// </summary>
	/// <param name="final">The player leaves (disconnect / map end) - closes their session</param>
	internal static void Flush(Player player, bool final)
	{
		int playerId = player.Profile.ID;
		long sessionId = player.Profile.SessionId;
		if (playerId <= 0)
			return;

		short style = player.Timer.Style;
		var map = SurfTimer.CurrentMap;
		var attempts = player.PendingAttempts
			.Where(a => a.Value.Started > 0)
			.Select(a => (CourseId: map?.CourseId(a.Key.Type, a.Key.Stage) ?? 0, a.Value.Started))
			.Where(a => a.CourseId > 0)
			.ToList();
		player.PendingAttempts.Clear();

		if (final)
			player.Profile.SessionId = 0; // Closed below - never twice

		Task.Run(async () =>
		{
			try
			{
				foreach (var (courseId, started) in attempts)
				{
					await SurfTimer.DB.ExecuteAsync(AddAttempts,
						new { PlayerId = playerId, CourseId = courseId, StyleId = style, Started = started });
				}

				if (sessionId > 0)
				{
					if (final)
						await PlayerRepository.CloseSessionAsync(sessionId);
					else
						await PlayerRepository.HeartbeatAsync([sessionId]);
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "[StatsService] Saving stats for player {PlayerId} failed", playerId);
			}
		});
	}
}
