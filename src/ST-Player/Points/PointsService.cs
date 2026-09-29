using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Keeps player_course_points (points per player / course / style) and player_stats.points (their
/// sum) up to date. A new PB can move everyone on that course a rank down, so the whole map is
/// recalculated - not just the player who finished (the CS:GO SurfTimer only did the latter).
/// Unranked maps, courses with points turned off and hidden times (timer bans) give no points.
/// </summary>
internal static class PointsService
{
	internal const byte BucketNone = 0;
	internal const byte BucketWr = 1;
	internal const byte BucketTop10 = 2;
	internal const byte BucketGroup = 3;

	private sealed class RankRow
	{
		public int PlayerId { get; set; }
		public int CourseId { get; set; }
		public byte KindId { get; set; }
		public int Tier { get; set; }
		public long Rank { get; set; }
		public long Total { get; set; }
	}

	private const string MapRanks = @"
		SELECT t.`player_id`, t.`course_id`, c.`kind_id`, COALESCE(c.`tier`, mc.`tier`, 0) AS Tier,
			RANK() OVER (PARTITION BY t.`course_id` ORDER BY t.`run_time_ticks`) AS `Rank`,
			COUNT(*) OVER (PARTITION BY t.`course_id`) AS Total
		FROM `{p}times` t
		JOIN `{p}courses` c ON c.`id` = t.`course_id`
		LEFT JOIN `{p}courses` mc ON mc.`map_id` = c.`map_id` AND mc.`kind_id` = 1 AND mc.`number` = 0
		WHERE c.`map_id` = @MapId AND t.`style_id` = @Style AND t.`hidden` = 0 AND c.`points_enabled` = 1";

	// One recalculation at a time - a map run and its last stage are saved at the same moment
	private static readonly SemaphoreSlim _lock = new(1, 1);

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PointsService");

	/// <summary>
	/// Recalculates everyone's points on one map for one style. Never throws - points are logged and
	/// skipped on failure so a run save isn't affected.
	/// </summary>
	internal static async Task RecalculateMapAsync(int mapId, int style)
	{
		await _lock.WaitAsync();
		try
		{
			var args = new { MapId = mapId, Style = style };
			bool ranked = await SurfTimer.DB.ExecuteScalarAsync<bool>("SELECT `ranked` FROM `{p}maps` WHERE `id` = @MapId", args);
			var ranks = ranked ? await SurfTimer.DB.QueryAsync<RankRow>(MapRanks, args) : new List<RankRow>();
			var previousPlayers = await SurfTimer.DB.QueryAsync<int>(@"
				SELECT DISTINCT pcp.`player_id` FROM `{p}player_course_points` pcp
				JOIN `{p}courses` c ON c.`id` = pcp.`course_id`
				WHERE c.`map_id` = @MapId AND pcp.`style_id` = @Style", args);

			var rows = new List<object>();
			foreach (var row in ranks)
			{
				var (completion, rankPoints, bucket) = PointsFor((CourseKind)row.KindId, row.Tier, (int)row.Rank, (int)row.Total);
				if (completion == 0 && rankPoints == 0)
					continue;

				rows.Add(new
				{
					row.PlayerId,
					row.CourseId,
					Style = style,
					Completion = completion,
					RankPoints = rankPoints,
					Bucket = bucket,
				});
			}

			// Everyone whose total can change: players with times here, and anyone who had points here before
			var affected = ranks.Select(r => r.PlayerId).Concat(previousPlayers).Distinct().ToList();

			await SurfTimer.DB.InTransactionAsync(async tx =>
			{
				await tx.ExecuteAsync(@"
					DELETE pcp FROM `{p}player_course_points` pcp
					JOIN `{p}courses` c ON c.`id` = pcp.`course_id`
					WHERE c.`map_id` = @MapId AND pcp.`style_id` = @Style", args);

				if (rows.Count > 0)
				{
					await tx.ExecuteAsync(@"
						INSERT INTO `{p}player_course_points`
							(`player_id`, `course_id`, `style_id`, `completion_points`, `rank_points`, `rank_bucket`)
						VALUES (@PlayerId, @CourseId, @Style, @Completion, @RankPoints, @Bucket)", rows);
				}

				if (affected.Count > 0)
				{
					var totals = new { Style = style, PlayerIds = affected };
					await tx.ExecuteAsync("UPDATE `{p}player_stats` SET `points` = 0, `updated_at` = UTC_TIMESTAMP(3) WHERE `style_id` = @Style AND `player_id` IN @PlayerIds", totals);
					await tx.ExecuteAsync(@"
						INSERT INTO `{p}player_stats` (`player_id`, `style_id`, `points`, `updated_at`)
						SELECT `player_id`, `style_id`, SUM(`completion_points` + `rank_points`), UTC_TIMESTAMP(3)
						FROM `{p}player_course_points`
						WHERE `style_id` = @Style AND `player_id` IN @PlayerIds
						GROUP BY `player_id`, `style_id`
						ON DUPLICATE KEY UPDATE `points` = VALUES(`points`), `updated_at` = VALUES(`updated_at`)", totals);
				}
			});

			// Ranks shown in chat move with the points
			SurfTimer.QueueServerRankRefresh();

#if DEBUG
			Logger.LogDebug("[PointsService] Map {MapId} style {Style}: {Rows} point rows, {Affected} totals updated",
				mapId, style, rows.Count, affected.Count);
#endif
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "[PointsService] Recalculating points for map {MapId} (style {Style}) failed", mapId, style);
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>
	/// Points of one time: map completion (by tier) plus rank points - WR / top 10 / group on maps,
	/// the bonus table on bonuses, and the configured WR points on stage / checkpoint segments.
	/// </summary>
	private static (int Completion, int Rank, byte Bucket) PointsFor(CourseKind kind, int tier, int rank, int total)
	{
		switch (kind)
		{
			case CourseKind.Map:
				var (wr, top10, group) = PointsCalculator.RankPoints(tier, rank, total);
				byte bucket = wr > 0 ? BucketWr : top10 > 0 ? BucketTop10 : group > 0 ? BucketGroup : BucketNone;
				return (PointsCalculator.CompletionPoints(tier), wr + top10 + group, bucket);

			case CourseKind.Bonus:
				return (0, PointsCalculator.BonusPoints(rank), rank == 1 ? BucketWr : BucketNone);

			default: // Stage / checkpoint segment
				return rank == 1 && Config.PointsSegmentWr > 0 ? (0, Config.PointsSegmentWr, BucketWr) : (0, 0, BucketNone);
		}
	}

	/// <summary>
	/// Recalculates every map for every style (!recalcpoints) - returns how many maps were done.
	/// </summary>
	internal static async Task<int> RecalculateAllAsync()
	{
		var mapIds = await MapRepository.GetAllIdsAsync();
		foreach (int mapId in mapIds)
		{
			foreach (int style in Config.Styles)
				await RecalculateMapAsync(mapId, style);
		}
		return mapIds.Count;
	}
}
