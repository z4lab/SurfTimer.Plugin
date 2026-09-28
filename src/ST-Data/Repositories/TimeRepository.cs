namespace SurfTimer;

/// <summary>
/// times (PBs), time_splits, run_history, course_stats and replays. Ranks are "count of strictly
/// faster times + 1" - a range scan on the (course_id, style_id, run_time_ticks) index.
/// </summary>
internal static class TimeRepository
{
	/// <summary>
	/// A finished run to store as a PB (if it's faster than the stored one).
	/// </summary>
	internal sealed record PbSave(
		int PlayerId, int CourseId, short StyleId, int RunTimeTicks, float? Sync,
		float StartVelX, float StartVelY, float StartVelZ, float EndVelX, float EndVelY, float EndVelZ,
		IReadOnlyCollection<CheckpointEntity>? Splits, ReplayCodec.Encoded? Replay);

	internal sealed record SaveResult(int TimeId, bool Improved);

	private sealed class ExistingRow
	{
		public int Id { get; set; }
		public int RunTimeTicks { get; set; }
		public int? ReplayId { get; set; }
	}

	internal sealed class TimeRow
	{
		public int Id { get; set; }
		public int PlayerId { get; set; }
		public int MapId { get; set; }
		public int CourseId { get; set; }
		public byte KindId { get; set; }
		public short Number { get; set; }
		public short Style { get; set; }
		public int RunTime { get; set; }
		public decimal? Sync { get; set; }
		public float StartVelX { get; set; }
		public float StartVelY { get; set; }
		public float StartVelZ { get; set; }
		public float EndVelX { get; set; }
		public float EndVelY { get; set; }
		public float EndVelZ { get; set; }
		public int? ReplayId { get; set; }
		public DateTime UpdatedAt { get; set; }
		public string? PlayerName { get; set; }
		public long Rank { get; set; }
		public long TotalCount { get; set; }

		internal CourseKind Kind => (CourseKind)KindId;

		internal T Fill<T>(T entity) where T : MapTimeRunDataEntity
		{
			entity.ID = Id;
			entity.PlayerID = PlayerId;
			entity.MapID = MapId;
			entity.CourseId = CourseId;
			entity.Type = CourseKinds.ToRunType(Kind);
			entity.Stage = Kind == CourseKind.Map ? (short)0 : Number;
			entity.Style = Style;
			entity.RunTime = RunTime;
			entity.Sync = Sync.HasValue ? (float)Sync.Value : null;
			entity.StartVelX = StartVelX;
			entity.StartVelY = StartVelY;
			entity.StartVelZ = StartVelZ;
			entity.EndVelX = EndVelX;
			entity.EndVelY = EndVelY;
			entity.EndVelZ = EndVelZ;
			entity.ReplayId = ReplayId;
			entity.RunDate = PlayerRepository.ToUnix(UpdatedAt);
			entity.Name = PlayerName;
			entity.Rank = (int)Rank;
			entity.TotalCount = (int)TotalCount;
			return entity;
		}
	}

	// A time with its course, holder, rank and the course's completions
	private const string SelectTime = @"
		SELECT t.`id`, t.`player_id`, c.`map_id`, t.`course_id`, c.`kind_id`, c.`number`, t.`style_id` AS Style,
			t.`run_time_ticks` AS RunTime, t.`sync`,
			t.`start_velocity_x` AS StartVelX, t.`start_velocity_y` AS StartVelY, t.`start_velocity_z` AS StartVelZ,
			t.`end_velocity_x` AS EndVelX, t.`end_velocity_y` AS EndVelY, t.`end_velocity_z` AS EndVelZ,
			t.`replay_id`, t.`updated_at`, p.`name` AS PlayerName,
			(SELECT COUNT(*) FROM `{p}times` x
				WHERE x.`course_id` = t.`course_id` AND x.`style_id` = t.`style_id` AND x.`run_time_ticks` < t.`run_time_ticks`) + 1 AS `Rank`,
			COALESCE(cs.`completions`, 0) AS TotalCount
		FROM `{p}times` t
		JOIN `{p}courses` c ON c.`id` = t.`course_id`
		JOIN `{p}players` p ON p.`id` = t.`player_id`
		LEFT JOIN `{p}course_stats` cs ON cs.`course_id` = t.`course_id` AND cs.`style_id` = t.`style_id`";

	/// <summary>
	/// Stores a finished run: always in run_history, and as the PB (with splits and replay, replacing the
	/// old ones) when it's the first or a faster time. Keeps course_stats (completions, WR) in sync.
	/// One transaction.
	/// </summary>
	internal static Task<SaveResult> SavePbAsync(PbSave run) =>
		SurfTimer.DB.InTransactionAsync(async tx =>
		{
			var key = new { run.PlayerId, run.CourseId, run.StyleId };
			var existing = await tx.QueryFirstOrDefaultAsync<ExistingRow>(@"
				SELECT `id`, `run_time_ticks`, `replay_id` FROM `{p}times`
				WHERE `player_id` = @PlayerId AND `course_id` = @CourseId AND `style_id` = @StyleId FOR UPDATE", key);

			bool improved = existing == null || run.RunTimeTicks < existing.RunTimeTicks;

			await tx.ExecuteAsync(@"
				INSERT INTO `{p}run_history` (`player_id`, `course_id`, `style_id`, `run_time_ticks`, `sync`, `is_pb`, `finished_at`)
				VALUES (@PlayerId, @CourseId, @StyleId, @RunTimeTicks, @Sync, @IsPb, UTC_TIMESTAMP(3))",
				new { run.PlayerId, run.CourseId, run.StyleId, run.RunTimeTicks, run.Sync, IsPb = improved });

			if (!improved)
				return new SaveResult(existing!.Id, false);

			int? replayId = null;
			if (run.Replay != null)
			{
				await tx.ExecuteAsync(@"
					INSERT INTO `{p}replays` (`format_version`, `tick_rate`, `frame_count`, `raw_size`, `data`, `created_at`)
					VALUES (@FormatVersion, @TickRate, @FrameCount, @RawSize, @Data, UTC_TIMESTAMP(3))",
					new
					{
						FormatVersion = ReplayCodec.FormatVersion,
						TickRate = ReplayCodec.TickRate,
						run.Replay.FrameCount,
						run.Replay.RawSize,
						run.Replay.Data,
					});
				replayId = (int)await tx.ExecuteScalarAsync<ulong>("SELECT LAST_INSERT_ID()");
			}

			var values = new
			{
				run.PlayerId, run.CourseId, run.StyleId, run.RunTimeTicks, run.Sync,
				run.StartVelX, run.StartVelY, run.StartVelZ, run.EndVelX, run.EndVelY, run.EndVelZ,
				ReplayId = replayId, Id = existing?.Id ?? 0,
			};

			int timeId;
			if (existing == null)
			{
				await tx.ExecuteAsync(@"
					INSERT INTO `{p}times` (`player_id`, `course_id`, `style_id`, `run_time_ticks`, `sync`,
						`start_velocity_x`, `start_velocity_y`, `start_velocity_z`, `end_velocity_x`, `end_velocity_y`, `end_velocity_z`,
						`replay_id`, `created_at`, `updated_at`)
					VALUES (@PlayerId, @CourseId, @StyleId, @RunTimeTicks, @Sync,
						@StartVelX, @StartVelY, @StartVelZ, @EndVelX, @EndVelY, @EndVelZ,
						@ReplayId, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))", values);
				timeId = (int)await tx.ExecuteScalarAsync<ulong>("SELECT LAST_INSERT_ID()");
			}
			else
			{
				await tx.ExecuteAsync(@"
					UPDATE `{p}times` SET `run_time_ticks` = @RunTimeTicks, `sync` = @Sync,
						`start_velocity_x` = @StartVelX, `start_velocity_y` = @StartVelY, `start_velocity_z` = @StartVelZ,
						`end_velocity_x` = @EndVelX, `end_velocity_y` = @EndVelY, `end_velocity_z` = @EndVelZ,
						`replay_id` = @ReplayId, `updated_at` = UTC_TIMESTAMP(3)
					WHERE `id` = @Id", values);
				timeId = existing.Id;

				// Only the PB's replay is kept
				if (existing.ReplayId != null)
					await tx.ExecuteAsync("DELETE FROM `{p}replays` WHERE `id` = @Id", new { Id = existing.ReplayId });
			}

			// Splits of the new PB
			await tx.ExecuteAsync("DELETE FROM `{p}time_splits` WHERE `time_id` = @TimeId", new { TimeId = timeId });
			if (run.Splits is { Count: > 0 })
			{
				await tx.ExecuteAsync(@"
					INSERT INTO `{p}time_splits` (`time_id`, `split_number`, `enter_ticks`, `exit_ticks`,
						`enter_velocity_x`, `enter_velocity_y`, `enter_velocity_z`, `exit_velocity_x`, `exit_velocity_y`, `exit_velocity_z`, `attempts`)
					VALUES (@TimeId, @SplitNumber, @EnterTicks, @ExitTicks, @EnterX, @EnterY, @EnterZ, @ExitX, @ExitY, @ExitZ, @Attempts)",
					run.Splits.Select(s => new
					{
						TimeId = timeId,
						SplitNumber = s.CP,
						EnterTicks = Math.Max(0, s.RunTime),
						ExitTicks = Math.Max(0, s.EndTouch),
						EnterX = s.StartVelX,
						EnterY = s.StartVelY,
						EnterZ = s.StartVelZ,
						ExitX = s.EndVelX,
						ExitY = s.EndVelY,
						ExitZ = s.EndVelZ,
						Attempts = Math.Clamp(s.Attempts, 0, ushort.MaxValue),
					}).ToList());
			}

			// Leaderboard cache: one more completion for a first time, and the course's fastest time
			await tx.ExecuteAsync(@"
				INSERT INTO `{p}course_stats` (`course_id`, `style_id`, `completions`, `wr_time_id`, `updated_at`)
				VALUES (@CourseId, @StyleId, @NewCompletion, @TimeId, UTC_TIMESTAMP(3))
				ON DUPLICATE KEY UPDATE `completions` = `completions` + VALUES(`completions`), `updated_at` = VALUES(`updated_at`)",
				new { run.CourseId, run.StyleId, NewCompletion = existing == null ? 1 : 0, TimeId = timeId });
			await tx.ExecuteAsync(@"
				UPDATE `{p}course_stats` SET `wr_time_id` = (
					SELECT t.`id` FROM `{p}times` t WHERE t.`course_id` = @CourseId AND t.`style_id` = @StyleId
					ORDER BY t.`run_time_ticks`, t.`updated_at` LIMIT 1)
				WHERE `course_id` = @CourseId AND `style_id` = @StyleId", new { run.CourseId, run.StyleId });

			return new SaveResult(timeId, true);
		});

	/// <summary>
	/// A finished run that isn't stored as a PB (not faster than the PB) - history only.
	/// </summary>
	internal static Task LogRunAsync(int playerId, int courseId, short styleId, int runTimeTicks, float? sync) =>
		SurfTimer.DB.ExecuteAsync(@"
			INSERT INTO `{p}run_history` (`player_id`, `course_id`, `style_id`, `run_time_ticks`, `sync`, `is_pb`, `finished_at`)
			VALUES (@PlayerId, @CourseId, @StyleId, @RunTimeTicks, @Sync, FALSE, UTC_TIMESTAMP(3))",
			new { PlayerId = playerId, CourseId = courseId, StyleId = styleId, RunTimeTicks = runTimeTicks, Sync = sync });

	internal sealed class RecordRow
	{
		public int CourseId { get; set; }
		public byte KindId { get; set; }
		public short Number { get; set; }
		public byte StyleId { get; set; }
		public long Completions { get; set; }
		public int? WrTimeId { get; set; }

		internal CourseKind Kind => (CourseKind)KindId;
	}

	/// <summary>
	/// Completions and the WR time id of every course of the map (course_stats) - no blobs.
	/// </summary>
	internal static Task<List<RecordRow>> GetMapRecordsAsync(int mapId) =>
		SurfTimer.DB.QueryAsync<RecordRow>(@"
			SELECT cs.`course_id`, c.`kind_id`, c.`number`, cs.`style_id`, cs.`completions`, cs.`wr_time_id`
			FROM `{p}course_stats` cs JOIN `{p}courses` c ON c.`id` = cs.`course_id`
			WHERE c.`map_id` = @MapId", new { MapId = mapId });

	internal static Task<List<TimeRow>> GetTimesAsync(IReadOnlyCollection<int> timeIds) => timeIds.Count == 0
		? Task.FromResult(new List<TimeRow>())
		: SurfTimer.DB.QueryAsync<TimeRow>(SelectTime + " WHERE t.`id` IN @Ids", new { Ids = timeIds });

	internal static Task<TimeRow?> GetTimeAsync(int timeId) =>
		SurfTimer.DB.QueryFirstOrDefaultAsync<TimeRow>(SelectTime + " WHERE t.`id` = @Id", new { Id = timeId });

	/// <summary>
	/// A player's PBs on one map, with ranks.
	/// </summary>
	internal static Task<List<TimeRow>> GetPlayerMapTimesAsync(int playerId, int mapId) =>
		SurfTimer.DB.QueryAsync<TimeRow>(SelectTime + " WHERE t.`player_id` = @PlayerId AND c.`map_id` = @MapId",
			new { PlayerId = playerId, MapId = mapId });

	private sealed class SplitRow
	{
		public int TimeId { get; set; }
		public short SplitNumber { get; set; }
		public int EnterTicks { get; set; }
		public int ExitTicks { get; set; }
		public float EnterVelocityX { get; set; }
		public float EnterVelocityY { get; set; }
		public float EnterVelocityZ { get; set; }
		public float ExitVelocityX { get; set; }
		public float ExitVelocityY { get; set; }
		public float ExitVelocityZ { get; set; }
		public int Attempts { get; set; }
	}

	internal static async Task<Dictionary<int, CheckpointEntity>> GetSplitsAsync(int timeId) =>
		(await SurfTimer.DB.QueryAsync<SplitRow>("SELECT * FROM `{p}time_splits` WHERE `time_id` = @TimeId", new { TimeId = timeId }))
		.ToDictionary(s => (int)s.SplitNumber, s => new CheckpointEntity(s.SplitNumber, s.EnterTicks,
			s.EnterVelocityX, s.EnterVelocityY, s.EnterVelocityZ, s.ExitVelocityX, s.ExitVelocityY, s.ExitVelocityZ,
			s.ExitTicks, s.Attempts)
		{ MapTimeID = s.TimeId });

	/// <summary>
	/// A stored replay (ReplayCodec data), or null.
	/// </summary>
	internal static Task<byte[]?> GetReplayDataAsync(int replayId) =>
		SurfTimer.DB.QueryFirstOrDefaultAsync<byte[]>("SELECT `data` FROM `{p}replays` WHERE `id` = @Id", new { Id = replayId });
}
