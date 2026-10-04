namespace SurfTimer;

/// <summary>
/// maps, courses, map_authors, map_settings.
/// </summary>
internal static class MapRepository
{
	internal sealed class MapRow
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public bool Ranked { get; set; }
		public ulong? WorkshopId { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime LastPlayedAt { get; set; }
	}

	internal sealed class CourseRow
	{
		public int Id { get; set; }
		public byte KindId { get; set; }
		public short Number { get; set; }
		public byte? Tier { get; set; }
		public string? Name { get; set; }
		public bool PointsEnabled { get; set; } = true;

		internal CourseKind Kind => (CourseKind)KindId;
	}

	private sealed class SettingRow
	{
		public string SettingKey { get; set; } = "";
		public string Value { get; set; } = "";
	}

	/// <summary>
	/// The map's row - created on its first load; marks it as played now.
	/// </summary>
	internal static Task<MapRow> GetOrCreateAsync(string name) =>
		SurfTimer.DB.InTransactionAsync(async tx =>
		{
			var args = new { Name = name };
			await tx.ExecuteAsync(@"
				INSERT INTO `{p}maps` (`name`, `ranked`, `created_at`, `last_played_at`)
				VALUES (@Name, FALSE, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))
				ON DUPLICATE KEY UPDATE `last_played_at` = VALUES(`last_played_at`)", args);
			return (await tx.QueryFirstOrDefaultAsync<MapRow>(
				"SELECT `id`, `name`, `ranked`, `workshop_id`, `created_at`, `last_played_at` FROM `{p}maps` WHERE `name` = @Name", args))!;
		});

	/// <summary>
	/// Creates the courses the map's zones define (existing ones are kept, so records of courses that
	/// were removed from the map stay) and returns all of the map's courses.
	/// </summary>
	internal static async Task<List<CourseRow>> EnsureCoursesAsync(int mapId, IEnumerable<(CourseKind Kind, short Number)> courses)
	{
		var rows = courses.Select(c => new { MapId = mapId, KindId = (byte)c.Kind, c.Number }).ToList();
		if (rows.Count > 0)
		{
			await SurfTimer.DB.ExecuteAsync(@"
				INSERT IGNORE INTO `{p}courses` (`map_id`, `kind_id`, `number`, `created_at`)
				VALUES (@MapId, @KindId, @Number, UTC_TIMESTAMP(3))", rows);
		}

		return await SurfTimer.DB.QueryAsync<CourseRow>(
			"SELECT `id`, `kind_id`, `number`, `tier`, `name`, `points_enabled` FROM `{p}courses` WHERE `map_id` = @MapId", new { MapId = mapId });
	}

	internal static Task SetCourseTierAsync(int courseId, byte? tier) =>
		SurfTimer.DB.ExecuteAsync("UPDATE `{p}courses` SET `tier` = @Tier WHERE `id` = @Id", new { Id = courseId, Tier = tier });

	internal static Task SetRankedAsync(int mapId, bool ranked) =>
		SurfTimer.DB.ExecuteAsync("UPDATE `{p}maps` SET `ranked` = @Ranked WHERE `id` = @Id", new { Id = mapId, Ranked = ranked });

	internal static Task SetCourseNameAsync(int courseId, string? name) =>
		SurfTimer.DB.ExecuteAsync("UPDATE `{p}courses` SET `name` = @Name WHERE `id` = @Id", new { Id = courseId, Name = name });

	internal static Task SetCoursePointsEnabledAsync(int courseId, bool enabled) =>
		SurfTimer.DB.ExecuteAsync("UPDATE `{p}courses` SET `points_enabled` = @Enabled WHERE `id` = @Id", new { Id = courseId, Enabled = enabled });

	internal static Task SetWorkshopIdAsync(int mapId, ulong? workshopId) =>
		SurfTimer.DB.ExecuteAsync("UPDATE `{p}maps` SET `workshop_id` = @WorkshopId WHERE `id` = @Id", new { Id = mapId, WorkshopId = workshopId });

	/// <summary>
	/// Maps by when they were last played, newest first (the admin panel's change-map list).
	/// </summary>
	internal static Task<List<MapRow>> GetRecentMapsAsync(int limit) =>
		SurfTimer.DB.QueryAsync<MapRow>(@"
			SELECT `id`, `name`, `ranked`, `workshop_id`, `created_at`, `last_played_at` FROM `{p}maps`
			ORDER BY `last_played_at` DESC LIMIT @Limit", new { Limit = limit });

	/// <summary>A map's row by its exact name - null when it was never played</summary>
	internal static Task<MapRow?> GetByNameAsync(string name) =>
		SurfTimer.DB.QueryFirstOrDefaultAsync<MapRow>(
			"SELECT `id`, `name`, `ranked`, `workshop_id`, `created_at`, `last_played_at` FROM `{p}maps` WHERE `name` = @Name", new { Name = name });

	internal static Task<List<int>> GetAllIdsAsync() =>
		SurfTimer.DB.QueryAsync<int>("SELECT `id` FROM `{p}maps`");

	// ---- Authors ----

	internal static Task<List<string>> GetAuthorsAsync(int mapId) =>
		SurfTimer.DB.QueryAsync<string>("SELECT `name` FROM `{p}map_authors` WHERE `map_id` = @MapId ORDER BY `position`",
			new { MapId = mapId });

	internal static Task SetAuthorsAsync(int mapId, IReadOnlyList<string> authors) =>
		SurfTimer.DB.InTransactionAsync(async tx =>
		{
			await tx.ExecuteAsync("DELETE FROM `{p}map_authors` WHERE `map_id` = @MapId", new { MapId = mapId });
			if (authors.Count > 0)
			{
				await tx.ExecuteAsync("INSERT INTO `{p}map_authors` (`map_id`, `position`, `name`) VALUES (@MapId, @Position, @Name)",
					authors.Select((name, i) => new { MapId = mapId, Position = (byte)i, Name = name }).ToList());
			}
		});

	// ---- Settings ----

	internal static async Task<Dictionary<string, string>> GetSettingsAsync(int mapId) =>
		(await SurfTimer.DB.QueryAsync<SettingRow>("SELECT `setting_key`, `value` FROM `{p}map_settings` WHERE `map_id` = @MapId",
			new { MapId = mapId }))
		.ToDictionary(s => s.SettingKey, s => s.Value, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// One setting of a map by its name (before the map's row is loaded) - null when unset or unknown map.
	/// </summary>
	internal static Task<string?> GetSettingByNameAsync(string mapName, string key) =>
		SurfTimer.DB.QueryFirstOrDefaultAsync<string>(@"
			SELECT ms.`value` FROM `{p}map_settings` ms JOIN `{p}maps` m ON m.`id` = ms.`map_id`
			WHERE m.`name` = @Name AND ms.`setting_key` = @Key", new { Name = mapName, Key = key });

	internal static Task SetSettingAsync(int mapId, string key, string value) =>
		SurfTimer.DB.ExecuteAsync(@"
			INSERT INTO `{p}map_settings` (`map_id`, `setting_key`, `value`, `updated_at`)
			VALUES (@MapId, @Key, @Value, UTC_TIMESTAMP(3))
			ON DUPLICATE KEY UPDATE `value` = VALUES(`value`), `updated_at` = VALUES(`updated_at`)",
			new { MapId = mapId, Key = key, Value = value });

	internal static Task DeleteSettingAsync(int mapId, string key) =>
		SurfTimer.DB.ExecuteAsync("DELETE FROM `{p}map_settings` WHERE `map_id` = @MapId AND `setting_key` = @Key",
			new { MapId = mapId, Key = key });
}
