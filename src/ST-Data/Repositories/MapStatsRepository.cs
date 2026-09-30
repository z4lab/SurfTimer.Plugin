namespace SurfTimer;

/// <summary>
/// !map - finding a map by name, its courses with WR and completions, and the map's stats.
/// </summary>
internal static class MapStatsRepository
{
	internal sealed class CourseSummaryRow
	{
		public int Id { get; set; }
		public byte KindId { get; set; }
		public short Number { get; set; }
		public byte? Tier { get; set; }
		public string? Name { get; set; }
		public int Completions { get; set; }
		public int? WrTime { get; set; }
		public string? WrHolder { get; set; }

		internal CourseKind Kind => (CourseKind)KindId;
	}

	internal sealed class MapStats
	{
		public long Completions { get; set; }
		public long UniquePlayers { get; set; }
		public long RunsStarted { get; set; }
		public long RunsFinished { get; set; }
		public long FinishesWeek { get; set; }
		public decimal? AverageTime { get; set; }
		public decimal? AverageSync { get; set; }
		public int? MedianTime { get; set; }
		public TimeRepository.TimeRow? Mine { get; set; }
	}

	private const string SelectMap = "SELECT `id`, `name`, `ranked`, `workshop_id`, `created_at`, `last_played_at` FROM `{p}maps`";

	/// <summary>
	/// A map by its exact name, else the most recently played map whose name contains the text.
	/// </summary>
	internal static async Task<MapRepository.MapRow?> FindMapAsync(string name)
	{
		var exact = await SurfTimer.DB.QueryFirstOrDefaultAsync<MapRepository.MapRow>(SelectMap + " WHERE `name` = @Name", new { Name = name });
		if (exact != null)
			return exact;

		string like = "%" + name.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
		return await SurfTimer.DB.QueryFirstOrDefaultAsync<MapRepository.MapRow>(
			SelectMap + " WHERE `name` LIKE @Like ORDER BY `last_played_at` DESC LIMIT 1", new { Like = like });
	}

	/// <summary>
	/// Every course of a map with its completions and WR (time + holder) in a style.
	/// </summary>
	internal static Task<List<CourseSummaryRow>> GetCourseSummariesAsync(int mapId, int style) =>
		SurfTimer.DB.QueryAsync<CourseSummaryRow>(@"
			SELECT c.`id`, c.`kind_id`, c.`number`, c.`tier`, c.`name`, COALESCE(cs.`completions`, 0) AS Completions,
				w.`run_time_ticks` AS WrTime, p.`name` AS WrHolder
			FROM `{p}courses` c
			LEFT JOIN `{p}course_stats` cs ON cs.`course_id` = c.`id` AND cs.`style_id` = @Style
			LEFT JOIN `{p}times` w ON w.`id` = cs.`wr_time_id`
			LEFT JOIN `{p}players` p ON p.`id` = w.`player_id`
			WHERE c.`map_id` = @MapId
			ORDER BY c.`kind_id`, c.`number`",
			new { MapId = mapId, Style = style });

	/// <summary>
	/// The !map stats of a map's main course (plus unique players over all its courses) and the viewer's PB.
	/// </summary>
	internal static async Task<MapStats> GetMapStatsAsync(int mapId, int courseId, int style, int playerId)
	{
		var args = new { MapId = mapId, CourseId = courseId, Style = style };
		var stats = await SurfTimer.DB.QueryFirstOrDefaultAsync<MapStats>(@"
			SELECT
				(SELECT COUNT(*) FROM `{p}times` WHERE `course_id` = @CourseId AND `style_id` = @Style AND `hidden` = 0) AS Completions,
				(SELECT COUNT(DISTINCT t.`player_id`) FROM `{p}times` t JOIN `{p}courses` c ON c.`id` = t.`course_id`
					WHERE c.`map_id` = @MapId AND t.`style_id` = @Style AND t.`hidden` = 0) AS UniquePlayers,
				(SELECT CAST(COALESCE(SUM(`started`), 0) AS SIGNED) FROM `{p}player_course_attempts`
					WHERE `course_id` = @CourseId AND `style_id` = @Style) AS RunsStarted,
				(SELECT COUNT(*) FROM `{p}run_history` WHERE `course_id` = @CourseId AND `style_id` = @Style) AS RunsFinished,
				(SELECT COUNT(*) FROM `{p}run_history` WHERE `course_id` = @CourseId AND `style_id` = @Style
					AND `finished_at` >= UTC_TIMESTAMP(3) - INTERVAL 7 DAY) AS FinishesWeek,
				(SELECT AVG(`run_time_ticks`) FROM `{p}times` WHERE `course_id` = @CourseId AND `style_id` = @Style AND `hidden` = 0) AS AverageTime,
				(SELECT AVG(`sync`) FROM `{p}times` WHERE `course_id` = @CourseId AND `style_id` = @Style AND `hidden` = 0
					AND `sync` IS NOT NULL) AS AverageSync", args) ?? new MapStats();

		if (stats.Completions > 0)
		{
			stats.MedianTime = await SurfTimer.DB.QueryFirstOrDefaultAsync<int?>(@"
				SELECT `run_time_ticks` FROM `{p}times` WHERE `course_id` = @CourseId AND `style_id` = @Style AND `hidden` = 0
				ORDER BY `run_time_ticks` LIMIT 1 OFFSET @Offset",
				new { CourseId = courseId, Style = style, Offset = (int)(stats.Completions / 2) });
		}

		stats.Mine = await TimeRepository.GetPlayerTimeAsync(courseId, style, playerId);
		return stats;
	}
}
