using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>Where a player was when they went to spectator (RunResume.cs puts them back there)</summary>
internal sealed record SpecReturn(short CourseBonus, short Stage, bool StageMode);

/// <summary>A saved run read for resuming - State null when the stored one can't be used (other version, broken)</summary>
internal sealed record ResumeCandidate(PlayerRunState? State, List<ReplayFrame>? Frames, long Revision, long AgeSeconds);

/// <summary>A player's saved run on the current map, as far as this server knows (Player.RunState)</summary>
internal sealed class RunStateTracker
{
	/// <summary>The saved run was restored, discarded or there was none - saving starts only after that</summary>
	internal bool Settled { get; set; }
	/// <summary>0 not loaded, 1 loading, 2 loaded (Loaded holds the result, null = nothing saved)</summary>
	internal int LoadStage { get; set; }
	internal bool LoadFailed { get; set; }
	internal ResumeCandidate? Loaded { get; set; }

	/// <summary>A row of this player and map exists (written by this server, or the resumed one)</summary>
	internal bool RowExists { get; set; }
	/// <summary>The last periodic capture - a final save falls back to it when the pawn is gone (spectator, map end)</summary>
	internal PlayerRunState? LastSnapshot { get; set; }
}

/// <summary>
/// Resume runs: a running run is saved per player and map (player_run_states) every few seconds without its replay,
/// and fully - replay frames included - when the player leaves (disconnect, map end, server switch). It's deleted as
/// soon as the run ends. Restoring is done by RunResume.cs on the player's first spawn on that map. Writes of one
/// player and map run one after another, so a delete can't overtake an earlier save.
/// </summary>
internal static class PlayerStateService
{
	/// <summary>Lightweight saves while running: every 5 s</summary>
	internal const int SaveIntervalTicks = 64 * 5;
	/// <summary>Larger replays aren't stored with the state (max_allowed_packet is 16 MB by default) - the run resumes without one</summary>
	private const int MaxReplayBytes = 15 * 1024 * 1024;
	/// <summary>A lightweight save of another server this recent may still get its final save - loading waits for it</summary>
	private const int OtherServerGraceSeconds = 15;

	/// <summary>This server process - tells its own rows from other servers' (server switch)</summary>
	internal static readonly string Instance = Guid.NewGuid().ToString("N");

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PlayerStateService");

	// Last write per (player, map) - the next one is chained after it
	private static readonly ConcurrentDictionary<(int PlayerId, int MapId), Task> _tails = new();
	private static readonly object _tailLock = new();
	private static long _lastRevision;

	/// <summary>Increasing across writes and servers: milliseconds, at least one more than the last one here</summary>
	private static long NextRevision()
	{
		long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		while (true)
		{
			long last = Interlocked.Read(ref _lastRevision);
			long next = Math.Max(last + 1, now);
			if (Interlocked.CompareExchange(ref _lastRevision, next, last) == last)
				return next;
		}
	}

	/// <summary>
	/// A revision written by another server: this server's next writes go above it, even if its clock is behind - else
	/// they would count as older and be ignored.
	/// </summary>
	private static void SeenRevision(long revision)
	{
		while (true)
		{
			long last = Interlocked.Read(ref _lastRevision);
			if (revision <= last || Interlocked.CompareExchange(ref _lastRevision, revision, last) == last)
				return;
		}
	}

	private static Task Enqueue((int PlayerId, int MapId) key, string what, Func<Task> work)
	{
		lock (_tailLock)
		{
			var previous = _tails.TryGetValue(key, out var tail) ? tail : Task.CompletedTask;
			Task next = null!;
			next = previous.ContinueWith(async _ =>
			{
				try
				{
					await work();
				}
				catch (Exception ex)
				{
					Logger.LogError(ex, "[PlayerStateService] {What} for player {PlayerId} on map {MapId} failed", what, key.PlayerId, key.MapId);
				}
				finally
				{
					// Forgotten once nothing is queued after it
					_tails.TryRemove(new KeyValuePair<(int, int), Task>(key, next));
				}
			}, TaskScheduler.Default).Unwrap();
			_tails[key] = next;
			return next;
		}
	}

	private static bool TryKey(Player player, out (int PlayerId, int MapId) key)
	{
		var map = SurfTimer.CurrentMap;
		key = (player.Profile.ID, map?.ID ?? 0);
		return key.PlayerId > 0 && key.MapId > 0;
	}

	/// <summary>
	/// Every tick (Tick.cs): a lightweight save every SaveIntervalTicks while the run is going, and the saved run
	/// deleted right when it ends (finish, reset, stop zone) - so a finished run can't come back.
	/// </summary>
	internal static void Tick(Player player)
	{
		var tracker = player.RunState;
		if (!tracker.Settled || !TryKey(player, out var key))
			return;

		if (!player.Timer.IsRunning)
		{
			tracker.LastSnapshot = null;
			if (tracker.RowExists)
				Delete(player, key, "run ended");
			return;
		}

		var controller = player.Controller;
		if (!controller.PawnIsAlive || player.IsPlacementPending
			|| (Server.TickCount + controller.Slot * 7) % SaveIntervalTicks != 0) // Players spread over the interval
			return;

		var pawn = controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;

		var state = PlayerRunState.Capture(player, pawn, SurfTimer.CurrentMap);
		if (state == null)
			return;

		tracker.LastSnapshot = state;
		Write(player, key, state, frames: null, full: false);
	}

	/// <summary>
	/// The player leaves this map (disconnect, map end, plugin unload): the running run is saved with its replay so far,
	/// a run that isn't running any more is deleted. Never blocks - the work runs in the background.
	/// </summary>
	internal static void SaveFinal(Player player, string reason)
	{
		var tracker = player.RunState;
		if (!tracker.Settled || !TryKey(player, out var key))
			return; // Not restored yet: whatever is stored stays for the next time

		if (!player.Timer.IsRunning)
		{
			if (tracker.RowExists)
				Delete(player, key, reason);
			return;
		}

		// The pawn as it is now - or, gone (spectator, map end), the last periodic capture (time and frames match it)
		PlayerRunState? state = null;
		var controller = player.Controller;
		var pawn = controller.IsValid && controller.PawnIsAlive && !player.IsPlacementPending ? controller.PlayerPawn.Value : null;
		if (pawn != null && pawn.IsValid)
			state = PlayerRunState.Capture(player, pawn, SurfTimer.CurrentMap);
		state ??= tracker.LastSnapshot;
		if (state == null)
			return; // Spectating before the first save - nothing to put the player back to

		var recorder = player.ReplayRecorder;
		List<ReplayFrame>? frames = null;
		if (!state.ReplayDropped && !recorder.DroppedForRun && state.FrameCount <= recorder.Frames.Count)
			frames = recorder.Frames.GetRange(0, state.FrameCount); // The frames up to the captured moment
		else
			state.ReplayDropped = true;

		Write(player, key, state, frames, full: true);
		Logger.LogDebug("[PlayerStateService] Saved the run of {Name} ({Reason}): {Run}, {Frames} replay frames",
			player.Profile.Name, reason, state.Describe(), frames?.Count ?? 0);
	}

	private static void Write(Player player, (int PlayerId, int MapId) key, PlayerRunState state, List<ReplayFrame>? frames, bool full)
	{
		long revision = NextRevision();
		player.RunState.RowExists = true;
		string instance = Instance;

		Enqueue(key, full ? "Final save" : "Save", async () =>
		{
			byte[]? replay = null;
			int replayFrames = 0;
			if (frames != null && frames.Count > 0)
			{
				var encoded = ReplayCodec.Encode(frames);
				if (encoded.Data.Length <= MaxReplayBytes)
				{
					replay = encoded.Data;
					replayFrames = encoded.FrameCount;
				}
				else
				{
					state.ReplayDropped = true;
					Logger.LogWarning("[PlayerStateService] Replay of player {PlayerId} too large to keep ({Bytes} bytes) - the run resumes without one",
						key.PlayerId, encoded.Data.Length);
				}
			}

			await PlayerStateRepository.UpsertAsync(key.PlayerId, key.MapId, PlayerRunState.CurrentVersion, state.Serialize(), replay, replayFrames,
				full, state.Practice, state.Ticks, instance, revision);
		});
	}

	private static void Delete(Player player, (int PlayerId, int MapId) key, string reason)
	{
		long revision = NextRevision();
		player.RunState.RowExists = false;
		player.RunState.LastSnapshot = null;
		Enqueue(key, $"Delete ({reason})", () => PlayerStateRepository.DeleteAsync(key.PlayerId, key.MapId, revision));
	}

	/// <summary>The saved run won't be resumed (start over, can't be used) - deleted, saving starts again</summary>
	internal static void Discard(Player player, string reason)
	{
		var tracker = player.RunState;
		tracker.Settled = true;
		tracker.Loaded = null;
		if (TryKey(player, out var key))
			Delete(player, key, reason);
	}

	/// <summary>
	/// Reads the player's saved run of the current map in the background - after this server's own pending writes of it,
	/// and briefly waiting when another server saved it seconds ago without a final save (it may still be writing it).
	/// The result lands in Player.RunState on the main thread.
	/// </summary>
	internal static void BeginLoad(Player player)
	{
		var tracker = player.RunState;
		if (tracker.LoadStage != 0 || !TryKey(player, out var key))
			return;
		tracker.LoadStage = 1;

		var pending = _tails.TryGetValue(key, out var tail) ? tail : Task.CompletedTask;
		Task.Run(async () =>
		{
			ResumeCandidate? result = null;
			bool failed = false;
			try
			{
				await pending; // Never throws (Enqueue catches)

				PlayerStateRepository.Row? row;
				for (int attempt = 0; ; attempt++)
				{
					row = await PlayerStateRepository.GetAsync(key.PlayerId, key.MapId);
					if (row == null || row.IsFull || row.ServerInstance == Instance || row.AgeSeconds >= OtherServerGraceSeconds || attempt >= 5)
						break;
					await Task.Delay(1000);
				}

				if (row != null)
				{
					SeenRevision(row.Revision);
					var state = row.FormatVersion == PlayerRunState.CurrentVersion ? PlayerRunState.Deserialize(row.State) : null;
					List<ReplayFrame>? frames = null;
					if (state != null && row.Replay != null && !state.ReplayDropped)
					{
						try
						{
							var decoded = ReplayCodec.Decode(row.Replay);
							if (decoded.Count >= state.FrameCount)
								frames = decoded.GetRange(0, state.FrameCount);
						}
						catch (Exception ex)
						{
							Logger.LogWarning(ex, "[PlayerStateService] Saved replay of player {PlayerId} unreadable - the run resumes without one", key.PlayerId);
						}
					}
					result = new ResumeCandidate(state, frames, row.Revision, row.AgeSeconds);
				}
			}
			catch (Exception ex)
			{
				failed = true;
				Logger.LogError(ex, "[PlayerStateService] Loading the saved run of player {PlayerId} failed", key.PlayerId);
			}

			Server.NextFrame(() =>
			{
				tracker.Loaded = result;
				tracker.LoadFailed = failed;
				tracker.LoadStage = 2;
			});
		});
	}

	/// <summary>Saved runs nobody can resume any more (older than both expiries) - on map start</summary>
	internal static void CleanupExpired()
	{
		int days = Config.ResumeExpiryDays, vipDays = Config.ResumeExpiryVipDays;
		if (days <= 0 || vipDays <= 0)
			return; // Never expires for someone - can't tell VIPs apart while they're offline

		int maxDays = Math.Max(days, vipDays);
		Task.Run(async () =>
		{
			try
			{
				int removed = await PlayerStateRepository.DeleteOlderThanAsync(maxDays);
				if (removed > 0)
					Logger.LogInformation("[PlayerStateService] Removed {Count} expired saved run(s)", removed);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "[PlayerStateService] Removing expired saved runs failed");
			}
		});
	}

	/// <summary>Plugin unload: waits a little for queued writes (final saves) to reach the database</summary>
	internal static void Drain(TimeSpan timeout)
	{
		Task[] pending;
		lock (_tailLock)
			pending = _tails.Values.ToArray();
		if (pending.Length > 0)
			Task.WaitAll(pending, timeout);
	}
}
