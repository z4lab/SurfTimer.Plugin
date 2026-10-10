namespace SurfTimer;

/// <summary>
/// player_run_states - a player's in-progress run per map (PlayerStateService). Every write carries a revision: a
/// write older than the stored one changes nothing, so a periodic save arriving late can't undo the final one.
/// </summary>
internal static class PlayerStateRepository
{
	internal sealed class Row
	{
		public int FormatVersion { get; set; }
		public string State { get; set; } = "";
		public byte[]? Replay { get; set; }
		public int ReplayFrames { get; set; }
		public bool IsFull { get; set; }
		public string ServerInstance { get; set; } = "";
		public long Revision { get; set; }
		/// <summary>Seconds since it was written (database clock)</summary>
		public long AgeSeconds { get; set; }
	}

	// `revision` is assigned last: the assignments before it compare against the stored (old) revision
	private const string Upsert = @"
		INSERT INTO `{p}player_run_states` (`player_id`, `map_id`, `format_version`, `state`, `replay`, `replay_frames`, `is_full`,
			`practice`, `run_ticks`, `server_instance`, `revision`, `created_at`, `updated_at`)
		VALUES (@PlayerId, @MapId, @FormatVersion, @State, @Replay, @ReplayFrames, @IsFull,
			@Practice, @RunTicks, @Instance, @Revision, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))
		ON DUPLICATE KEY UPDATE
			`format_version`  = IF(VALUES(`revision`) >= `revision`, VALUES(`format_version`), `format_version`),
			`state`           = IF(VALUES(`revision`) >= `revision`, VALUES(`state`), `state`),
			`replay`          = IF(VALUES(`revision`) >= `revision`, VALUES(`replay`), `replay`),
			`replay_frames`   = IF(VALUES(`revision`) >= `revision`, VALUES(`replay_frames`), `replay_frames`),
			`is_full`         = IF(VALUES(`revision`) >= `revision`, VALUES(`is_full`), `is_full`),
			`practice`        = IF(VALUES(`revision`) >= `revision`, VALUES(`practice`), `practice`),
			`run_ticks`       = IF(VALUES(`revision`) >= `revision`, VALUES(`run_ticks`), `run_ticks`),
			`server_instance` = IF(VALUES(`revision`) >= `revision`, VALUES(`server_instance`), `server_instance`),
			`updated_at`      = IF(VALUES(`revision`) >= `revision`, VALUES(`updated_at`), `updated_at`),
			`revision`        = GREATEST(`revision`, VALUES(`revision`))";

	internal static Task UpsertAsync(int playerId, int mapId, int formatVersion, string state, byte[]? replay, int replayFrames,
		bool isFull, bool practice, int runTicks, string instance, long revision) =>
		SurfTimer.DB.ExecuteAsync(Upsert, new
		{
			PlayerId = playerId,
			MapId = mapId,
			FormatVersion = formatVersion,
			State = state,
			Replay = replay,
			ReplayFrames = replayFrames,
			IsFull = isFull,
			Practice = practice,
			RunTicks = Math.Max(0, runTicks),
			Instance = instance,
			Revision = revision,
		});

	internal static Task<Row?> GetAsync(int playerId, int mapId) =>
		SurfTimer.DB.QueryFirstOrDefaultAsync<Row>(@"
			SELECT `format_version`, `state`, `replay`, `replay_frames`, `is_full`, `server_instance`, `revision`,
				TIMESTAMPDIFF(SECOND, `updated_at`, UTC_TIMESTAMP(3)) AS AgeSeconds
			FROM `{p}player_run_states`
			WHERE `player_id` = @PlayerId AND `map_id` = @MapId",
			new { PlayerId = playerId, MapId = mapId });

	/// <summary>Removes the saved run - only if nothing newer than revision was written since (e.g. by another server)</summary>
	internal static Task<int> DeleteAsync(int playerId, int mapId, long upToRevision) =>
		SurfTimer.DB.ExecuteAsync(@"
			DELETE FROM `{p}player_run_states`
			WHERE `player_id` = @PlayerId AND `map_id` = @MapId AND `revision` <= @Revision",
			new { PlayerId = playerId, MapId = mapId, Revision = upToRevision });

	/// <summary>Saved runs not written for this many days (expired for everyone)</summary>
	internal static Task<int> DeleteOlderThanAsync(int days) =>
		SurfTimer.DB.ExecuteAsync(@"
			DELETE FROM `{p}player_run_states`
			WHERE `updated_at` < UTC_TIMESTAMP(3) - INTERVAL @Days DAY",
			new { Days = days });
}
