using System.Diagnostics;

namespace SurfTimer;

/// <summary>
/// admin_actions (audit log), database statistics and maintenance for the !admin panel.
/// </summary>
internal static class AdminRepository
{
	// ---- Audit ----

	internal static Task LogAsync(int? adminPlayerId, string action, string targetType, long? targetId, int? mapId, string details) =>
		SurfTimer.DB.ExecuteAsync(@"
			INSERT INTO `{p}admin_actions` (`admin_player_id`, `action`, `target_type`, `target_id`, `map_id`, `details`, `created_at`)
			VALUES (@AdminId, @Action, @TargetType, @TargetId, @MapId, @Details, UTC_TIMESTAMP(3))",
			new
			{
				AdminId = adminPlayerId,
				Action = Truncate(action, 64),
				TargetType = Truncate(targetType, 32),
				TargetId = targetId,
				MapId = mapId,
				Details = Truncate(details, 512),
			});

	internal sealed class AuditRow
	{
		public long Id { get; set; }
		public string? AdminName { get; set; }
		public string Action { get; set; } = "";
		public string Details { get; set; } = "";
		public string? MapName { get; set; }
		public DateTime CreatedAt { get; set; }
	}

	/// <summary>
	/// Latest admin actions, newest first - of one map, or all when mapId is null.
	/// </summary>
	internal static Task<List<AuditRow>> GetAuditAsync(int? mapId, int limit) =>
		SurfTimer.DB.QueryAsync<AuditRow>(@"
			SELECT a.`id`, p.`name` AS AdminName, a.`action`, a.`details`, m.`name` AS MapName, a.`created_at`
			FROM `{p}admin_actions` a
			LEFT JOIN `{p}players` p ON p.`id` = a.`admin_player_id`
			LEFT JOIN `{p}maps` m ON m.`id` = a.`map_id`
			WHERE (@MapId IS NULL OR a.`map_id` = @MapId)
			ORDER BY a.`created_at` DESC LIMIT @Limit", new { MapId = mapId, Limit = limit });

	// ---- Database statistics ----

	internal sealed class HealthRow
	{
		public string Version { get; set; } = "";
		public double PingMs { get; set; }
		public int SchemaVersion { get; set; }
		public string SchemaName { get; set; } = "";
		public int Migrations { get; set; }
	}

	private sealed class MigrationRow
	{
		public int Version { get; set; }
		public string Name { get; set; } = "";
		public int Count { get; set; }
	}

	internal static async Task<HealthRow> GetHealthAsync()
	{
		var watch = Stopwatch.StartNew();
		await SurfTimer.DB.ExecuteScalarAsync<int>("SELECT 1");
		double ping = watch.Elapsed.TotalMilliseconds;

		string version = await SurfTimer.DB.ExecuteScalarAsync<string>("SELECT VERSION()") ?? "";
		var migration = await SurfTimer.DB.QueryFirstOrDefaultAsync<MigrationRow>(@"
			SELECT `version`, `name`, (SELECT COUNT(*) FROM `{p}schema_migrations`) AS `Count`
			FROM `{p}schema_migrations` ORDER BY `version` DESC LIMIT 1");

		return new HealthRow
		{
			Version = version,
			PingMs = ping,
			SchemaVersion = migration?.Version ?? 0,
			SchemaName = migration?.Name ?? "",
			Migrations = migration?.Count ?? 0,
		};
	}

	internal sealed class TableRow
	{
		public string Name { get; set; } = "";
		public long Rows { get; set; }
		public long Bytes { get; set; }
		public bool Exact { get; set; }
	}

	/// <summary>
	/// The plugin's tables (by prefix) with InnoDB's row estimate and data + index size, biggest first.
	/// </summary>
	internal static async Task<List<TableRow>> GetTablesAsync(bool exactCounts)
	{
		string prefix = SurfTimer.DB.TablePrefix;
		var tables = await SurfTimer.DB.QueryAsync<TableRow>(@"
			SELECT `TABLE_NAME` AS Name, CAST(COALESCE(`TABLE_ROWS`, 0) AS SIGNED) AS `Rows`,
				CAST(COALESCE(`DATA_LENGTH`, 0) + COALESCE(`INDEX_LENGTH`, 0) AS SIGNED) AS Bytes
			FROM information_schema.`TABLES`
			WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_TYPE` = 'BASE TABLE'", null);

		// Only our tables: with a prefix all starting with it, without one the known names
		tables = tables.Where(t => prefix.Length > 0
			? t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			: KnownTables.Contains(t.Name)).ToList();

		if (exactCounts)
		{
			foreach (var table in tables)
			{
				// Names come from information_schema and are checked against our prefix / list above
				table.Rows = await SurfTimer.DB.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `{table.Name.Replace("`", "``")}`");
				table.Exact = true;
			}
		}

		foreach (var table in tables)
			table.Name = prefix.Length > 0 ? table.Name[prefix.Length..] : table.Name;
		return tables.OrderByDescending(t => t.Bytes).ThenBy(t => t.Name).ToList();
	}

	private static readonly HashSet<string> KnownTables = new(StringComparer.OrdinalIgnoreCase)
	{
		"schema_migrations", "styles", "course_kinds", "players", "player_names", "player_settings", "player_stats",
		"maps", "map_authors", "map_settings", "courses", "player_sessions", "replays", "times", "time_splits",
		"run_history", "course_stats", "player_course_points", "player_course_attempts", "player_bans", "admin_actions",
	};

	internal sealed class TotalsRow
	{
		public long Players { get; set; }
		public long Maps { get; set; }
		public long RankedMaps { get; set; }
		public long Courses { get; set; }
		public long Times { get; set; }
		public long HiddenTimes { get; set; }
		public long Replays { get; set; }
		public long ReplayBytes { get; set; }
		public long History { get; set; }
		public long OpenSessions { get; set; }
		public long PlayTime { get; set; }
		public long ActiveBans { get; set; }
	}

	internal static async Task<TotalsRow> GetTotalsAsync() =>
		await SurfTimer.DB.QueryFirstOrDefaultAsync<TotalsRow>(@"
			SELECT
				(SELECT COUNT(*) FROM `{p}players`) AS Players,
				(SELECT COUNT(*) FROM `{p}maps`) AS Maps,
				(SELECT COUNT(*) FROM `{p}maps` WHERE `ranked` = 1) AS RankedMaps,
				(SELECT COUNT(*) FROM `{p}courses`) AS Courses,
				(SELECT COUNT(*) FROM `{p}times`) AS Times,
				(SELECT COUNT(*) FROM `{p}times` WHERE `hidden` = 1) AS HiddenTimes,
				(SELECT COUNT(*) FROM `{p}replays`) AS Replays,
				(SELECT CAST(COALESCE(SUM(`data_size`), 0) AS SIGNED) FROM `{p}replays`) AS ReplayBytes,
				(SELECT COUNT(*) FROM `{p}run_history`) AS History,
				(SELECT COUNT(*) FROM `{p}player_sessions` WHERE `left_at` IS NULL) AS OpenSessions,
				(SELECT CAST(COALESCE(SUM(COALESCE(`duration_seconds`, TIMESTAMPDIFF(SECOND, `joined_at`, `last_heartbeat_at`))), 0) AS SIGNED)
					FROM `{p}player_sessions`) AS PlayTime,
				(SELECT COUNT(DISTINCT b.`player_id`) FROM `{p}player_bans` b
					WHERE b.`lifted_at` IS NULL AND (b.`expires_at` IS NULL OR b.`expires_at` > UTC_TIMESTAMP(3))) AS ActiveBans") ?? new TotalsRow();

	// ---- Maintenance ----

	private const string OrphanedReplays = "FROM `{p}replays` r WHERE NOT EXISTS (SELECT 1 FROM `{p}times` t WHERE t.`replay_id` = r.`id`)";

	internal static Task<long> CountOrphanedReplaysAsync() =>
		SurfTimer.DB.ExecuteScalarAsync<long>("SELECT COUNT(*) " + OrphanedReplays);

	internal static Task<int> DeleteOrphanedReplaysAsync() =>
		SurfTimer.DB.ExecuteAsync("DELETE r " + OrphanedReplays);

	internal static Task<long> CountHistoryOlderThanAsync(int days) =>
		SurfTimer.DB.ExecuteScalarAsync<long>(
			"SELECT COUNT(*) FROM `{p}run_history` WHERE `finished_at` < UTC_TIMESTAMP(3) - INTERVAL @Days DAY", new { Days = days });

	/// <summary>
	/// Deletes old finished runs - PBs stay in times, only the history (and "runs finished") shrinks.
	/// </summary>
	internal static Task<int> PurgeHistoryOlderThanAsync(int days) =>
		SurfTimer.DB.ExecuteAsync(
			"DELETE FROM `{p}run_history` WHERE `finished_at` < UTC_TIMESTAMP(3) - INTERVAL @Days DAY", new { Days = days });

	/// <summary>
	/// OPTIMIZE TABLE for every plugin table (rebuilds InnoDB tables - briefly locks them).
	/// </summary>
	internal static async Task<int> OptimizeTablesAsync()
	{
		var tables = await GetTablesAsync(exactCounts: false);
		string prefix = SurfTimer.DB.TablePrefix;
		foreach (var table in tables)
		{
			// OPTIMIZE returns a result set per table - read and ignored
			await SurfTimer.DB.QueryAsync<dynamic>($"OPTIMIZE TABLE `{(prefix + table.Name).Replace("`", "``")}`");
		}
		return tables.Count;
	}

	private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
