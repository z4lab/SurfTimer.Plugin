namespace SurfTimer;

/// <summary>
/// players, player_names, player_sessions, player_settings.
/// </summary>
internal static class PlayerRepository
{
	private sealed class PlayerRow
	{
		public int Id { get; set; }
		public ulong SteamId { get; set; }
		public string Name { get; set; } = "";
		public string? CountryCode { get; set; }
		public DateTime FirstSeenAt { get; set; }
		public DateTime LastSeenAt { get; set; }
		public long Sessions { get; set; }
	}

	private const string SelectPlayer = @"
		SELECT p.`id`, p.`steam_id`, p.`name`, p.`country_code`, p.`first_seen_at`, p.`last_seen_at`,
			(SELECT COUNT(*) FROM `{p}player_sessions` s WHERE s.`player_id` = p.`id`) AS Sessions
		FROM `{p}players` p";

	internal static int ToUnix(DateTime utc) => (int)new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

	private static PlayerProfileEntity ToEntity(PlayerRow row) => new()
	{
		ID = row.Id,
		SteamID = row.SteamId,
		Name = row.Name,
		Country = row.CountryCode,
		JoinDate = ToUnix(row.FirstSeenAt),
		LastSeen = ToUnix(row.LastSeenAt),
		Connections = (int)row.Sessions,
	};

	/// <summary>
	/// Creates the player or updates name / country / last seen, and records the name in the history.
	/// </summary>
	/// <param name="country">ISO code, or null when unknown (keeps the stored one)</param>
	internal static Task<PlayerProfileEntity> UpsertAsync(ulong steamId, string name, string? country) =>
		SurfTimer.DB.InTransactionAsync(async tx =>
		{
			var args = new { SteamId = steamId, Name = name, Country = country };
			await tx.ExecuteAsync(@"
				INSERT INTO `{p}players` (`steam_id`, `name`, `country_code`, `first_seen_at`, `last_seen_at`)
				VALUES (@SteamId, @Name, @Country, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))
				ON DUPLICATE KEY UPDATE `name` = VALUES(`name`),
					`country_code` = COALESCE(VALUES(`country_code`), `country_code`),
					`last_seen_at` = VALUES(`last_seen_at`)", args);

			var row = (await tx.QueryFirstOrDefaultAsync<PlayerRow>(SelectPlayer + " WHERE p.`steam_id` = @SteamId", args))!;

			await tx.ExecuteAsync(@"
				INSERT INTO `{p}player_names` (`player_id`, `name`, `first_used_at`, `last_used_at`)
				VALUES (@PlayerId, @Name, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))
				ON DUPLICATE KEY UPDATE `last_used_at` = VALUES(`last_used_at`)",
				new { PlayerId = row.Id, Name = name });

			return ToEntity(row);
		});

	/// <summary>
	/// Best match for a (partial) name - current names and former ones, most recently seen first.
	/// </summary>
	internal static async Task<PlayerProfileEntity?> FindByNameAsync(string name)
	{
		// LIKE wildcards in the name are matched literally
		string pattern = "%" + name.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
		var row = await SurfTimer.DB.QueryFirstOrDefaultAsync<PlayerRow>(SelectPlayer + @"
			WHERE p.`name` LIKE @Pattern
				OR p.`id` IN (SELECT n.`player_id` FROM `{p}player_names` n WHERE n.`name` LIKE @Pattern)
			ORDER BY p.`last_seen_at` DESC LIMIT 1", new { Pattern = pattern });
		return row == null ? null : ToEntity(row);
	}

	// ---- Sessions (playtime / visits) ----

	internal static Task<long> OpenSessionAsync(int playerId, int? mapId) =>
		SurfTimer.DB.InTransactionAsync(async tx =>
		{
			await tx.ExecuteAsync(@"
				INSERT INTO `{p}player_sessions` (`player_id`, `map_id`, `joined_at`, `last_heartbeat_at`)
				VALUES (@PlayerId, @MapId, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))",
				new { PlayerId = playerId, MapId = mapId });
			return await tx.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()");
		});

	internal static Task HeartbeatAsync(IReadOnlyCollection<long> sessionIds) => sessionIds.Count == 0
		? Task.CompletedTask
		: SurfTimer.DB.ExecuteAsync("UPDATE `{p}player_sessions` SET `last_heartbeat_at` = UTC_TIMESTAMP(3) WHERE `id` IN @Ids AND `left_at` IS NULL",
			new { Ids = sessionIds });

	internal static Task CloseSessionAsync(long sessionId) =>
		SurfTimer.DB.ExecuteAsync(@"
			UPDATE `{p}player_sessions`
			SET `left_at` = UTC_TIMESTAMP(3), `last_heartbeat_at` = UTC_TIMESTAMP(3),
				`duration_seconds` = TIMESTAMPDIFF(SECOND, `joined_at`, UTC_TIMESTAMP(3))
			WHERE `id` = @Id AND `left_at` IS NULL", new { Id = sessionId });

	/// <summary>
	/// Closes sessions left open by a crash / hard restart, at their last heartbeat. Only ones without a
	/// heartbeat for 5 minutes, so other servers on the same database aren't affected.
	/// </summary>
	internal static Task<int> CloseStaleSessionsAsync() =>
		SurfTimer.DB.ExecuteAsync(@"
			UPDATE `{p}player_sessions`
			SET `left_at` = `last_heartbeat_at`, `duration_seconds` = TIMESTAMPDIFF(SECOND, `joined_at`, `last_heartbeat_at`)
			WHERE `left_at` IS NULL AND `last_heartbeat_at` < UTC_TIMESTAMP(3) - INTERVAL 5 MINUTE");

	// ---- Settings ----

	private sealed class SettingRow
	{
		public string SettingKey { get; set; } = "";
		public string Value { get; set; } = "";
	}

	internal static async Task<Dictionary<string, string>> GetSettingsAsync(int playerId) =>
		(await SurfTimer.DB.QueryAsync<SettingRow>("SELECT `setting_key`, `value` FROM `{p}player_settings` WHERE `player_id` = @PlayerId",
			new { PlayerId = playerId }))
		.ToDictionary(s => s.SettingKey, s => s.Value, StringComparer.OrdinalIgnoreCase);

	internal static Task SetSettingAsync(int playerId, string key, string value) =>
		SurfTimer.DB.ExecuteAsync(@"
			INSERT INTO `{p}player_settings` (`player_id`, `setting_key`, `value`, `updated_at`)
			VALUES (@PlayerId, @Key, @Value, UTC_TIMESTAMP(3))
			ON DUPLICATE KEY UPDATE `value` = VALUES(`value`), `updated_at` = VALUES(`updated_at`)",
			new { PlayerId = playerId, Key = key, Value = value });
}
