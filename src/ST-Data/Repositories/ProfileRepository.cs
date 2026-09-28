namespace SurfTimer;

/// <summary>
/// Everything !profile shows about a player, for one style - loaded in one go off the main thread.
/// </summary>
internal sealed class ProfileData
{
	internal sealed class PointsRow
	{
		public long Points { get; set; }
		public long PlayTime { get; set; } // Seconds, all sessions
		public long ServerRank { get; set; }
		public long RankedPlayers { get; set; }
	}

	internal sealed class TypeCountsRow
	{
		public byte KindId { get; set; }
		public long Completions { get; set; }
		public long Records { get; set; }
		public long Top10 { get; set; }

		/// <summary>Plugin run type (0 map, 1 bonus, 2 stage, 3 checkpoint segment)</summary>
		public short Type => CourseKinds.ToRunType((CourseKind)KindId);
	}

	internal sealed class RankedTotalsRow
	{
		public long Maps { get; set; }
		public long Stages { get; set; }
		public long Bonuses { get; set; }
	}

	internal sealed class ExtrasRow
	{
		public decimal? AverageSync { get; set; }
		public long RunsStarted { get; set; }
		public long RunsFinished { get; set; }
	}

	internal sealed class PointsBucketsRow
	{
		public long MapPoints { get; set; }
		public long WrPoints { get; set; }
		public long Top10Points { get; set; }
		public long GroupPoints { get; set; }
		public long BonusPoints { get; set; }
		public long BonusWrPoints { get; set; }
		public long SegmentWrPoints { get; set; }
	}

	internal sealed class RunRow
	{
		public int Id { get; set; }
		public int MapId { get; set; }
		public string MapName { get; set; } = "";
		public byte KindId { get; set; }
		public short Stage { get; set; } // Course number
		public int RunTime { get; set; }
		public decimal? Sync { get; set; }
		public DateTime RunAt { get; set; }
		public int? ReplayId { get; set; }
		public long Rank { get; set; }
		public long Total { get; set; }

		/// <summary>Plugin run type (0 map, 1 bonus, 2 stage, 3 checkpoint segment)</summary>
		public short Type => CourseKinds.ToRunType((CourseKind)KindId);
		public int RunDate => PlayerRepository.ToUnix(RunAt);
	}

	internal sealed class TierRow
	{
		public int Tier { get; set; }
		public long Total { get; set; }
		public long Completed { get; set; }
	}

	public PointsRow Points { get; set; } = new();
	public List<TypeCountsRow> TypeCounts { get; set; } = [];
	public RankedTotalsRow RankedTotals { get; set; } = new();
	public ExtrasRow Extras { get; set; } = new();
	public PointsBucketsRow PointsBuckets { get; set; } = new();
	public List<RunRow> Records { get; set; } = [];
	public List<RunRow> Recent { get; set; } = [];
	public List<TierRow> Tiers { get; set; } = [];
	/// <summary>The player's times on the current map, with rank</summary>
	public List<RunRow> CurrentMapRuns { get; set; } = [];

	internal TypeCountsRow CountsFor(short type) =>
		TypeCounts.FirstOrDefault(c => c.Type == type) ?? new TypeCountsRow { KindId = (byte)CourseKinds.FromRunType(type) };
}

internal static class ProfileRepository
{
	private const string PointsSql = @"
		SELECT CAST(COALESCE(ps.`points`, 0) AS SIGNED) AS Points,
			(SELECT COUNT(*) FROM `{p}player_stats` WHERE `style_id` = @Style AND `points` > COALESCE(ps.`points`, 0)) + 1 AS ServerRank,
			(SELECT COUNT(*) FROM `{p}player_stats` WHERE `style_id` = @Style AND `points` > 0) AS RankedPlayers,
			(SELECT CAST(COALESCE(SUM(COALESCE(s.`duration_seconds`, TIMESTAMPDIFF(SECOND, s.`joined_at`, s.`last_heartbeat_at`))), 0) AS SIGNED)
				FROM `{p}player_sessions` s WHERE s.`player_id` = @PlayerId) AS PlayTime
		FROM (SELECT 1) AS one
		LEFT JOIN `{p}player_stats` ps ON ps.`player_id` = @PlayerId AND ps.`style_id` = @Style";

	// Rank = strictly faster times + 1 (ties share a rank) - a range scan on the leaderboard index
	private const string RankOf = @"
		(SELECT COUNT(*) FROM `{p}times` o
			WHERE o.`course_id` = mine.`course_id` AND o.`style_id` = mine.`style_id` AND o.`hidden` = 0
				AND o.`run_time_ticks` < mine.`run_time_ticks`) + 1";

	// Completions / WRs / top 10s per course kind, ranked maps only
	private const string TypeCountsSql = @"
		SELECT r.`kind_id` AS KindId, COUNT(*) AS Completions,
			CAST(SUM(r.`rnk` = 1) AS SIGNED) AS Records, CAST(SUM(r.`rnk` <= 10) AS SIGNED) AS Top10
		FROM (
			SELECT c.`kind_id`, " + RankOf + @" AS rnk
			FROM `{p}times` mine
			JOIN `{p}courses` c ON c.`id` = mine.`course_id`
			JOIN `{p}maps` m ON m.`id` = c.`map_id`
			WHERE mine.`player_id` = @PlayerId AND mine.`style_id` = @Style AND mine.`hidden` = 0 AND m.`ranked` = 1
		) AS r
		GROUP BY r.`kind_id`";

	private const string RankedTotalsSql = @"
		SELECT CAST(COALESCE(SUM(c.`kind_id` = 1), 0) AS SIGNED) AS Maps,
			CAST(COALESCE(SUM(c.`kind_id` = 2), 0) AS SIGNED) AS Stages,
			CAST(COALESCE(SUM(c.`kind_id` = 3), 0) AS SIGNED) AS Bonuses
		FROM `{p}courses` c JOIN `{p}maps` m ON m.`id` = c.`map_id`
		WHERE m.`ranked` = 1";

	// Average sync of map and bonus PBs; runs finished = every finish in the history
	private const string ExtrasSql = @"
		SELECT
			(SELECT AVG(t.`sync`) FROM `{p}times` t JOIN `{p}courses` c ON c.`id` = t.`course_id`
				WHERE t.`player_id` = @PlayerId AND t.`style_id` = @Style AND c.`kind_id` IN (1, 3) AND t.`sync` IS NOT NULL) AS AverageSync,
			(SELECT CAST(COALESCE(SUM(`started`), 0) AS SIGNED) FROM `{p}player_course_attempts`
				WHERE `player_id` = @PlayerId AND `style_id` = @Style) AS RunsStarted,
			(SELECT COUNT(*) FROM `{p}run_history` WHERE `player_id` = @PlayerId AND `style_id` = @Style) AS RunsFinished";

	// Buckets: 1 wr, 2 top 10, 3 group (see PointsService)
	private const string PointsBucketsSql = @"
		SELECT
			CAST(COALESCE(SUM(CASE WHEN c.`kind_id` = 1 THEN pcp.`completion_points` END), 0) AS SIGNED) AS MapPoints,
			CAST(COALESCE(SUM(CASE WHEN c.`kind_id` = 1 AND pcp.`rank_bucket` = 1 THEN pcp.`rank_points` END), 0) AS SIGNED) AS WrPoints,
			CAST(COALESCE(SUM(CASE WHEN c.`kind_id` = 1 AND pcp.`rank_bucket` = 2 THEN pcp.`rank_points` END), 0) AS SIGNED) AS Top10Points,
			CAST(COALESCE(SUM(CASE WHEN c.`kind_id` = 1 AND pcp.`rank_bucket` = 3 THEN pcp.`rank_points` END), 0) AS SIGNED) AS GroupPoints,
			CAST(COALESCE(SUM(CASE WHEN c.`kind_id` = 3 AND pcp.`rank_bucket` <> 1 THEN pcp.`rank_points` END), 0) AS SIGNED) AS BonusPoints,
			CAST(COALESCE(SUM(CASE WHEN c.`kind_id` = 3 AND pcp.`rank_bucket` = 1 THEN pcp.`rank_points` END), 0) AS SIGNED) AS BonusWrPoints,
			CAST(COALESCE(SUM(CASE WHEN c.`kind_id` IN (2, 4) THEN pcp.`rank_points` END), 0) AS SIGNED) AS SegmentWrPoints
		FROM `{p}player_course_points` pcp
		JOIN `{p}courses` c ON c.`id` = pcp.`course_id`
		WHERE pcp.`player_id` = @PlayerId AND pcp.`style_id` = @Style";

	// A player's PBs with their rank - wrapped so callers can filter / order on the rank. {filter} is an
	// extra condition (not string.Format - the SQL is full of {p} prefix placeholders)
	private const string RunsSql = @"
		SELECT * FROM (
			SELECT mine.`id` AS Id, c.`map_id` AS MapId, m.`name` AS MapName, c.`kind_id` AS KindId, c.`number` AS Stage,
				mine.`run_time_ticks` AS RunTime, mine.`sync` AS Sync, mine.`updated_at` AS RunAt, mine.`replay_id` AS ReplayId,
				" + RankOf + @" AS `Rank`,
				COALESCE(cs.`completions`, 0) AS Total
			FROM `{p}times` mine
			JOIN `{p}courses` c ON c.`id` = mine.`course_id`
			JOIN `{p}maps` m ON m.`id` = c.`map_id`
			LEFT JOIN `{p}course_stats` cs ON cs.`course_id` = mine.`course_id` AND cs.`style_id` = mine.`style_id`
			WHERE mine.`player_id` = @PlayerId AND mine.`style_id` = @Style AND mine.`hidden` = 0 {filter}
		) AS r ";

	private static readonly string RecordsSql = RunsSql.Replace("{filter}", "") + "WHERE r.`Rank` = 1 ORDER BY r.MapName, r.KindId, r.Stage";
	private static readonly string RecentSql = RunsSql.Replace("{filter}", "") + "ORDER BY r.RunAt DESC LIMIT 24";
	private static readonly string MapRunsSql = RunsSql.Replace("{filter}", "AND c.`map_id` = @MapId") + "ORDER BY r.KindId, r.Stage";

	// Map completions per map tier, ranked maps only
	private const string TiersSql = @"
		SELECT COALESCE(c.`tier`, 0) AS Tier, COUNT(*) AS Total,
			CAST(SUM(EXISTS(SELECT 1 FROM `{p}times` t
				WHERE t.`player_id` = @PlayerId AND t.`course_id` = c.`id` AND t.`style_id` = @Style AND t.`hidden` = 0)) AS SIGNED) AS Completed
		FROM `{p}courses` c JOIN `{p}maps` m ON m.`id` = c.`map_id`
		WHERE m.`ranked` = 1 AND c.`kind_id` = 1
		GROUP BY COALESCE(c.`tier`, 0)
		ORDER BY Tier";

	internal static async Task<ProfileData> LoadAsync(int playerId, int style, int currentMapId)
	{
		var args = new { PlayerId = playerId, Style = style };
		var db = SurfTimer.DB;

		var data = new ProfileData
		{
			Points = await db.QueryFirstOrDefaultAsync<ProfileData.PointsRow>(PointsSql, args) ?? new(),
			TypeCounts = await db.QueryAsync<ProfileData.TypeCountsRow>(TypeCountsSql, args),
			RankedTotals = await db.QueryFirstOrDefaultAsync<ProfileData.RankedTotalsRow>(RankedTotalsSql) ?? new(),
			Extras = await db.QueryFirstOrDefaultAsync<ProfileData.ExtrasRow>(ExtrasSql, args) ?? new(),
			PointsBuckets = await db.QueryFirstOrDefaultAsync<ProfileData.PointsBucketsRow>(PointsBucketsSql, args) ?? new(),
			Records = await db.QueryAsync<ProfileData.RunRow>(RecordsSql, args),
			Recent = await db.QueryAsync<ProfileData.RunRow>(RecentSql, args),
			Tiers = await db.QueryAsync<ProfileData.TierRow>(TiersSql, args),
		};

		if (currentMapId > 0)
		{
			data.CurrentMapRuns = await db.QueryAsync<ProfileData.RunRow>(MapRunsSql,
				new { PlayerId = playerId, Style = style, MapId = currentMapId });
		}

		return data;
	}
}
