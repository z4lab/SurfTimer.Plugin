namespace SurfTimer;

/// <summary>
/// players, player_names, player_sessions, player_settings, player_bans.
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

	internal static Task<int> ResetSettingsAsync(int playerId) =>
		SurfTimer.DB.ExecuteAsync("DELETE FROM `{p}player_settings` WHERE `player_id` = @PlayerId", new { PlayerId = playerId });

	// ---- Admin: search, history ----

	/// <summary>
	/// Players matching a (partial) current or former name, most recently seen first.
	/// </summary>
	internal static async Task<List<PlayerProfileEntity>> SearchAsync(string name, int limit)
	{
		string pattern = "%" + name.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
		var rows = await SurfTimer.DB.QueryAsync<PlayerRow>(SelectPlayer + @"
			WHERE p.`name` LIKE @Pattern
				OR p.`id` IN (SELECT n.`player_id` FROM `{p}player_names` n WHERE n.`name` LIKE @Pattern)
			ORDER BY p.`last_seen_at` DESC LIMIT @Limit", new { Pattern = pattern, Limit = limit });
		return rows.Select(ToEntity).ToList();
	}

	internal static async Task<PlayerProfileEntity?> GetAsync(int playerId)
	{
		var row = await SurfTimer.DB.QueryFirstOrDefaultAsync<PlayerRow>(SelectPlayer + " WHERE p.`id` = @Id", new { Id = playerId });
		return row == null ? null : ToEntity(row);
	}

	internal sealed class NameRow
	{
		public string Name { get; set; } = "";
		public DateTime FirstUsedAt { get; set; }
		public DateTime LastUsedAt { get; set; }
	}

	internal static Task<List<NameRow>> GetNamesAsync(int playerId) =>
		SurfTimer.DB.QueryAsync<NameRow>(@"
			SELECT `name`, `first_used_at`, `last_used_at` FROM `{p}player_names`
			WHERE `player_id` = @PlayerId ORDER BY `last_used_at` DESC LIMIT 40", new { PlayerId = playerId });

	internal sealed class SessionRow
	{
		public DateTime JoinedAt { get; set; }
		public DateTime LastHeartbeatAt { get; set; }
		public DateTime? LeftAt { get; set; }
		public string? MapName { get; set; }
	}

	internal static Task<List<SessionRow>> GetRecentSessionsAsync(int playerId, int limit) =>
		SurfTimer.DB.QueryAsync<SessionRow>(@"
			SELECT s.`joined_at`, s.`last_heartbeat_at`, s.`left_at`, m.`name` AS MapName
			FROM `{p}player_sessions` s LEFT JOIN `{p}maps` m ON m.`id` = s.`map_id`
			WHERE s.`player_id` = @PlayerId ORDER BY s.`joined_at` DESC LIMIT @Limit",
			new { PlayerId = playerId, Limit = limit });

	internal sealed class SummaryRow
	{
		public long Points { get; set; }
		public long Rank { get; set; }
		public long PlayTime { get; set; }
		public long Times { get; set; }
		public long HiddenTimes { get; set; }
	}

	/// <summary>
	/// Points / rank (style), playtime and time counts for the admin player page.
	/// </summary>
	internal static async Task<SummaryRow> GetSummaryAsync(int playerId, int style) =>
		await SurfTimer.DB.QueryFirstOrDefaultAsync<SummaryRow>(@"
			SELECT CAST(COALESCE(ps.`points`, 0) AS SIGNED) AS Points,
				(SELECT COUNT(*) FROM `{p}player_stats` WHERE `style_id` = @Style AND `points` > COALESCE(ps.`points`, 0)) + 1 AS `Rank`,
				(SELECT CAST(COALESCE(SUM(COALESCE(s.`duration_seconds`, TIMESTAMPDIFF(SECOND, s.`joined_at`, s.`last_heartbeat_at`))), 0) AS SIGNED)
					FROM `{p}player_sessions` s WHERE s.`player_id` = @PlayerId) AS PlayTime,
				(SELECT COUNT(*) FROM `{p}times` t WHERE t.`player_id` = @PlayerId) AS Times,
				(SELECT COUNT(*) FROM `{p}times` t WHERE t.`player_id` = @PlayerId AND t.`hidden` = 1) AS HiddenTimes
			FROM (SELECT 1) AS one
			LEFT JOIN `{p}player_stats` ps ON ps.`player_id` = @PlayerId AND ps.`style_id` = @Style",
			new { PlayerId = playerId, Style = style }) ?? new SummaryRow();

	// ---- Server rank (chat) ----

	private sealed class RankRow
	{
		public int PlayerId { get; set; }
		public long Rank { get; set; }
	}

	/// <summary>
	/// Server rank by points (normal style) of the given players - players without points aren't in the
	/// result. Same rank rule as !profile: strictly more points + 1.
	/// </summary>
	internal static async Task<Dictionary<int, int>> GetServerRanksAsync(IReadOnlyCollection<int> playerIds)
	{
		if (playerIds.Count == 0)
			return new Dictionary<int, int>();

		var rows = await SurfTimer.DB.QueryAsync<RankRow>(@"
			SELECT ps.`player_id`,
				(SELECT COUNT(*) FROM `{p}player_stats` o WHERE o.`style_id` = 0 AND o.`points` > ps.`points`) + 1 AS `Rank`
			FROM `{p}player_stats` ps
			WHERE ps.`style_id` = 0 AND ps.`points` > 0 AND ps.`player_id` IN @Ids", new { Ids = playerIds });
		return rows.ToDictionary(r => r.PlayerId, r => (int)r.Rank);
	}

	// ---- Timer bans ----

	internal sealed class BanRow
	{
		public int Id { get; set; }
		public int PlayerId { get; set; }
		public string? AdminName { get; set; }
		public string Reason { get; set; } = "";
		public DateTime CreatedAt { get; set; }
		public DateTime? ExpiresAt { get; set; }

		internal bool IsPermanent => ExpiresAt == null;
	}

	private const string ActiveBan = "b.`lifted_at` IS NULL AND (b.`expires_at` IS NULL OR b.`expires_at` > UTC_TIMESTAMP(3))";

	/// <summary>
	/// The player's active timer ban (the one ending last), or null.
	/// </summary>
	internal static Task<BanRow?> GetActiveBanAsync(int playerId) =>
		SurfTimer.DB.QueryFirstOrDefaultAsync<BanRow>(@"
			SELECT b.`id`, b.`player_id`, a.`name` AS AdminName, b.`reason`, b.`created_at`, b.`expires_at`
			FROM `{p}player_bans` b LEFT JOIN `{p}players` a ON a.`id` = b.`admin_player_id`
			WHERE b.`player_id` = @PlayerId AND " + ActiveBan + @"
			ORDER BY b.`expires_at` IS NULL DESC, b.`expires_at` DESC LIMIT 1", new { PlayerId = playerId });

	/// <summary>
	/// Adds a timer ban (expiresAt null = permanent). Hiding the times is up to the caller
	/// (TimeRepository.SetHiddenForPlayerAsync) so it can recalculate points after.
	/// </summary>
	internal static Task BanAsync(int playerId, int? adminPlayerId, string reason, DateTime? expiresAtUtc) =>
		SurfTimer.DB.ExecuteAsync(@"
			INSERT INTO `{p}player_bans` (`player_id`, `admin_player_id`, `reason`, `created_at`, `expires_at`)
			VALUES (@PlayerId, @AdminId, @Reason, UTC_TIMESTAMP(3), @ExpiresAt)",
			new { PlayerId = playerId, AdminId = adminPlayerId, Reason = reason, ExpiresAt = expiresAtUtc });

	/// <summary>
	/// Lifts all active bans of the player.
	/// </summary>
	internal static Task<int> UnbanAsync(int playerId, int? adminPlayerId) =>
		SurfTimer.DB.ExecuteAsync(@"
			UPDATE `{p}player_bans` b SET b.`lifted_at` = UTC_TIMESTAMP(3), b.`lifted_by` = @AdminId
			WHERE b.`player_id` = @PlayerId AND " + ActiveBan, new { PlayerId = playerId, AdminId = adminPlayerId });

	/// <summary>
	/// Marks bans that ran out as lifted and returns the players who are no longer banned at all -
	/// their times get shown again by the caller.
	/// </summary>
	internal static Task<List<int>> LiftExpiredBansAsync() =>
		SurfTimer.DB.InTransactionAsync(async tx =>
		{
			var expired = (await tx.QueryAsync<int>(@"
				SELECT DISTINCT b.`player_id` FROM `{p}player_bans` b
				WHERE b.`lifted_at` IS NULL AND b.`expires_at` IS NOT NULL AND b.`expires_at` <= UTC_TIMESTAMP(3)")).ToList();
			if (expired.Count == 0)
				return expired;

			await tx.ExecuteAsync(@"
				UPDATE `{p}player_bans` SET `lifted_at` = `expires_at`
				WHERE `lifted_at` IS NULL AND `expires_at` IS NOT NULL AND `expires_at` <= UTC_TIMESTAMP(3)");

			var stillBanned = (await tx.QueryAsync<int>(@"
				SELECT DISTINCT b.`player_id` FROM `{p}player_bans` b WHERE b.`player_id` IN @Ids AND " + ActiveBan,
				new { Ids = expired })).ToHashSet();
			return expired.Where(id => !stillBanned.Contains(id)).ToList();
		});
}
