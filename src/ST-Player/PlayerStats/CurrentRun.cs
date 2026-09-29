using CounterStrikeSharp.API;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SurfTimer;

/// <summary>
/// This class stores data for the current run.
/// </summary>
public class CurrentRun : RunStatsEntity
{
	private readonly ILogger<CurrentRun> _logger;

	public Dictionary<int, CheckpointEntity> Checkpoints { get; set; }

	internal CurrentRun()
	{
		_logger = SurfTimer.ServiceProvider.GetRequiredService<ILogger<CurrentRun>>();

		Checkpoints = new Dictionary<int, CheckpointEntity>();
	}

	/// <summary>
	/// Stores a finished run that isn't a PB in the run history (in the background).
	/// </summary>
	/// <param name="type">0 map, 1 bonus, 2 stage, 3 checkpoint segment</param>
	internal static void LogRun(Player player, short type, short number, int runTicks, float? sync)
	{
		if (player.Timer.IsPracticeMode || SurfTimer.CurrentMap == null || player.Profile.IsBanned)
			return;

		int courseId = SurfTimer.CurrentMap.CourseId(type, number);
		int playerId = player.Profile.ID;
		short style = player.Timer.Style;
		if (courseId <= 0 || playerId <= 0)
			return;

		float? rounded = sync.HasValue ? MathF.Round(sync.Value, 2) : null;
		_ = Task.Run(async () =>
		{
			try
			{
				await TimeRepository.LogRunAsync(playerId, courseId, style, runTicks, rounded);
			}
			catch (Exception ex)
			{
				SurfTimer.ServiceProvider.GetRequiredService<ILogger<CurrentRun>>()
					.LogError(ex, "[CurrentRun] Logging a run of player {PlayerId} failed", playerId);
			}
		});
	}

	/// <summary>
	/// Saves the player's run to the database.
	/// Supports all types of runs Map/Bonus/Stage/Checkpoint segment.
	/// </summary>
	/// <param name="player">Player object</param>
	/// <param name="bonus">Bonus number</param>
	/// <param name="stage">Stage number</param>
	/// <param name="checkpoint">Checkpoint segment number (non-staged maps only)</param>
	/// <param name="run_ticks">Ticks for the run - used for Stage, Bonus and Checkpoint entries</param>
	/// <param name="segmentStartVelX">Override for the segment's own start velocity (Stage/Checkpoint saves) - falls back to the overall run's start velocity when null (Map saves)</param>
	/// <param name="segmentEndVelX">Override for the segment's own end velocity (Stage/Checkpoint saves) - falls back to the overall run's end velocity when null (Map saves)</param>
	/// <param name="segmentSync">Stage / checkpoint segment sync - map and bonus runs use the run's sync</param>
	internal async Task SaveMapTime(Player player, short bonus = 0, short stage = 0, short checkpoint = 0, int run_ticks = -1,
		float? segmentStartVelX = null, float? segmentStartVelY = null, float? segmentStartVelZ = null,
		float? segmentEndVelX = null, float? segmentEndVelY = null, float? segmentEndVelZ = null,
		float? segmentSync = null,
		[CallerMemberName] string methodName = "")
	{
		int style = player.Timer.Style;
		short recType;

		if (checkpoint != 0)
		{
			recType = 3; // Checkpoint segment run
		}
		else if (stage != 0)
		{
			recType = 2; // Stage run
		}
		else if (bonus != 0)
		{
			recType = 1; // Bonus run
		}
		else
		{
			recType = 0; // Map run
		}

		short number = recType switch { 1 => bonus, 2 => stage, 3 => checkpoint, _ => 0 };
		var map = SurfTimer.CurrentMap;

		// Timer ban: nothing is stored (callers may already be off the main thread - chat via NextFrame)
		if (player.Profile.IsBanned)
		{
			if (recType == 0 || recType == 1)
			{
				var controller = player.Controller;
				Server.NextFrame(() =>
				{
					if (controller.IsValid)
						controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["timer_banned_run"]}");
				});
			}
			return;
		}

		int courseId = map.CourseId(recType, number);
		if (courseId <= 0)
		{
			_logger.LogError("[{ClassName}] {MethodName} -> No course for run type {Type} {Number} on {Map} - run not saved",
				nameof(CurrentRun), methodName, recType, number, map.Name);
			return;
		}

		// Everything from the live run is taken here, on the main thread, before the first await.
		// No replay when replays are off globally or for this map.
		// No replay either when the idle checker dropped this run's recording.
		bool storeReplay = Config.ReplaysEnabled && map.RecordReplays
			&& !player.ReplayRecorder.DroppedForRun && player.ReplayRecorder.Frames.Count > 0;
		var frames = !storeReplay ? new List<ReplayFrame>() : player.ReplayRecorder.TrimReplay(
			player,
			type: recType,
			bonus: bonus,
			stage: stage,
			lastStage: stage == map.Stages,
			checkpoint: checkpoint,
			lastCheckpoint: checkpoint == map.CheckpointSegments // Last segment ends at the map end
		);

		var splits = recType == 0
			? this.Checkpoints.Values.Select(c => new CheckpointEntity(c.CP, c.RunTime, c.StartVelX, c.StartVelY, c.StartVelZ,
				c.EndVelX, c.EndVelY, c.EndVelZ, c.EndTouch, c.Attempts)).ToList()
			: null;

		var save = new TimeRepository.PbSave(
			PlayerId: player.Profile.ID,
			CourseId: courseId,
			StyleId: (short)style,
			RunTimeTicks: run_ticks == -1 ? this.RunTime : run_ticks,
			// The timer is stopped by the time a map/bonus run is saved, so its sync is final
			Sync: MathF.Round(segmentSync ?? player.SyncPercent, 2),
			StartVelX: segmentStartVelX ?? this.StartVelX,
			StartVelY: segmentStartVelY ?? this.StartVelY,
			StartVelZ: segmentStartVelZ ?? this.StartVelZ,
			EndVelX: segmentEndVelX ?? this.EndVelX,
			EndVelY: segmentEndVelY ?? this.EndVelY,
			EndVelZ: segmentEndVelZ ?? this.EndVelZ,
			Splits: splits,
			Replay: null);

		var stopwatch = Stopwatch.StartNew();

		// Encoding the replay is the expensive part - off the main thread
		var replay = frames.Count > 0 ? await Task.Run(() => ReplayCodec.Encode(frames)) : null;
		var result = await TimeRepository.SavePbAsync(save with { Replay = replay });

		_logger.LogTrace("[{ClassName}] {MethodName} -> Saved time {TimeId} (course {CourseId}, improved {Improved}, replay {Frames} frames / {Bytes} bytes)",
			nameof(CurrentRun), methodName, result.TimeId, courseId, result.Improved, replay?.FrameCount ?? 0, replay?.Data.Length ?? 0);

		// Records (and WR replays that changed), the player's PB with its new rank, everyone's points
		await map.LoadMapRecordRuns();

		var pb = player.Stats.PbFor(recType, number, style);
		if (pb != null)
		{
			pb.ID = result.TimeId;
			await pb.ReloadAsync();
			if (recType == 0)
				await pb.LoadCheckpoints();
		}

		await PointsService.RecalculateMapAsync(map.ID, style);

		stopwatch.Stop();
		_logger.LogInformation("[{Class}] {Method} -> Finished SaveMapTime for '{Name}' (time {ID}) in {Elapsed}ms",
			nameof(CurrentRun), methodName, player.Profile.Name, result.TimeId, stopwatch.ElapsedMilliseconds
		);
	}

	/// <summary>
	/// Deals with saving a Stage MapTime (Type 2) in the Database.
	/// Should deal with `IsStageMode` runs, Stages during Map Runs and also Last Stage.
	/// </summary>
	/// <param name="player">Player object</param>
	/// <param name="stage">Stage to save</param>
	/// <param name="saveLastStage">Is it the last stage?</param>
	/// <param name="stage_run_time">Run Time (Ticks) for the stage run</param>
	/// <param name="startVelX">This specific stage's own entry velocity (not the overall map run's)</param>
	/// <param name="endVelX">This specific stage's own exit velocity (not the overall map run's)</param>
	internal static async Task SaveStageTime(Player player, short stage = -1, int stage_run_time = -1, bool saveLastStage = false,
		float startVelX = 0, float startVelY = 0, float startVelZ = 0,
		float endVelX = 0, float endVelY = 0, float endVelZ = 0, float? sync = null)
	{
#if DEBUG
		var _logger = SurfTimer.ServiceProvider.GetRequiredService<ILogger<CurrentRun>>();
		_logger.LogTrace("[{Class}] -> SaveStageTime received: Name = {Name} | Stage = {Stage} | RunTime = {RunTime} | IsLastStage = {IsLastStage}",
			nameof(CurrentRun), player.Profile.Name, stage, stage_run_time, saveLastStage
		);
#endif
		int pStyle = player.Timer.Style;
		if (
			stage_run_time < SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime ||
			SurfTimer.CurrentMap.StageWR[stage][pStyle].ID == -1 ||
			player.Stats.StagePB[stage][pStyle] != null && player.Stats.StagePB[stage][pStyle].RunTime > stage_run_time ||
			player.Stats.StagePB[stage][pStyle] != null && player.Stats.StagePB[stage][pStyle].ID == -1
		)
		{
			if (stage_run_time < SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime) // Player beat the Stage WR
			{
				int timeImprove = SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime - stage_run_time;
				ChatAnnounce.Send(ChatAnnounce.Kind.Record, player, $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagewr_improved",
					player.Controller.PlayerName, stage, PlayerHud.FormatTime(stage_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime)]}");
			}
			else if (SurfTimer.CurrentMap.StageWR[stage][pStyle].ID == -1) // No Stage record was set on the map
			{
				ChatAnnounce.Send(ChatAnnounce.Kind.Record, player, $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagewr_set",
					player.Controller.PlayerName, stage, PlayerHud.FormatTime(stage_run_time)]}");
			}
			else if (player.Stats.StagePB[stage][pStyle] != null && player.Stats.StagePB[stage][pStyle].ID == -1) // Player first Stage personal best
			{
				player.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagepb_set",
					stage, PlayerHud.FormatTime(stage_run_time)]}"
				);
			}
			else if (player.Stats.StagePB[stage][pStyle] != null && player.Stats.StagePB[stage][pStyle].RunTime > stage_run_time) // Player beating their existing Stage personal best
			{
				int timeImprove = player.Stats.StagePB[stage][pStyle].RunTime - stage_run_time;
				ChatAnnounce.Send(ChatAnnounce.Kind.Pb, player, $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagepb_improved",
					player.Controller.PlayerName, stage, PlayerHud.FormatTime(stage_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(player.Stats.StagePB[stage][pStyle].RunTime)]}");
			}

			// Save stage run
			await player.Stats.ThisRun.SaveMapTime(player, stage: stage, run_ticks: stage_run_time,
				segmentStartVelX: startVelX, segmentStartVelY: startVelY, segmentStartVelZ: startVelZ,
				segmentEndVelX: endVelX, segmentEndVelY: endVelY, segmentEndVelZ: endVelZ, segmentSync: sync
			); // Save the Stage MapTime PB data
		}
		else
		{
			LogRun(player, 2, stage, stage_run_time, sync); // Not a PB - history only

			if (stage_run_time > SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime && player.Timer.IsStageMode) // Player is behind the Stage WR for the map
			{
				int timeImprove = stage_run_time - SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime;
				player.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagewr_missed",
					stage, PlayerHud.FormatTime(stage_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime)]}"
				);
			}
		}
	}

	/// <summary>
	/// Deals with saving a Checkpoint segment MapTime (Type 3) in the Database.
	/// Only used on maps with no Stages, where Checkpoints act as the map's segments.
	/// Should deal with checkpoint segments crossed during a Map run and also the Last Checkpoint.
	/// </summary>
	/// <param name="player">Player object</param>
	/// <param name="checkpoint">Checkpoint segment to save</param>
	/// <param name="checkpoint_run_time">Run Time (Ticks) for the checkpoint segment run</param>
	/// <param name="saveLastCheckpoint">Is it the last checkpoint segment?</param>
	/// <param name="startVelX">This specific checkpoint segment's own entry velocity (not the overall map run's)</param>
	/// <param name="endVelX">This specific checkpoint segment's own exit velocity (not the overall map run's)</param>
	internal static async Task SaveCheckpointTime(Player player, short checkpoint = -1, int checkpoint_run_time = -1, bool saveLastCheckpoint = false,
		float startVelX = 0, float startVelY = 0, float startVelZ = 0,
		float endVelX = 0, float endVelY = 0, float endVelZ = 0, float? sync = null)
	{
#if DEBUG
		var _logger = SurfTimer.ServiceProvider.GetRequiredService<ILogger<CurrentRun>>();
		_logger.LogTrace("[{Class}] -> SaveCheckpointTime received: Name = {Name} | Checkpoint = {Checkpoint} | RunTime = {RunTime} | IsLastCheckpoint = {IsLastCheckpoint}",
			nameof(CurrentRun), player.Profile.Name, checkpoint, checkpoint_run_time, saveLastCheckpoint
		);
#endif
		int pStyle = player.Timer.Style;
		if (
			checkpoint_run_time < SurfTimer.CurrentMap.CheckpointWR[checkpoint][pStyle].RunTime ||
			SurfTimer.CurrentMap.CheckpointWR[checkpoint][pStyle].ID == -1 ||
			player.Stats.CheckpointPB[checkpoint][pStyle] != null && player.Stats.CheckpointPB[checkpoint][pStyle].RunTime > checkpoint_run_time ||
			player.Stats.CheckpointPB[checkpoint][pStyle] != null && player.Stats.CheckpointPB[checkpoint][pStyle].ID == -1
		)
		{
			if (checkpoint_run_time < SurfTimer.CurrentMap.CheckpointWR[checkpoint][pStyle].RunTime) // Player beat the Checkpoint WR
			{
				int timeImprove = SurfTimer.CurrentMap.CheckpointWR[checkpoint][pStyle].RunTime - checkpoint_run_time;
				ChatAnnounce.Send(ChatAnnounce.Kind.Record, player, $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpointwr_improved",
					player.Controller.PlayerName, checkpoint, PlayerHud.FormatTime(checkpoint_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(SurfTimer.CurrentMap.CheckpointWR[checkpoint][pStyle].RunTime)]}");
			}
			else if (SurfTimer.CurrentMap.CheckpointWR[checkpoint][pStyle].ID == -1) // No Checkpoint record was set on the map
			{
				ChatAnnounce.Send(ChatAnnounce.Kind.Record, player, $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpointwr_set",
					player.Controller.PlayerName, checkpoint, PlayerHud.FormatTime(checkpoint_run_time)]}");
			}
			else if (player.Stats.CheckpointPB[checkpoint][pStyle] != null && player.Stats.CheckpointPB[checkpoint][pStyle].ID == -1) // Player first Checkpoint personal best
			{
				player.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpointpb_set",
					checkpoint, PlayerHud.FormatTime(checkpoint_run_time)]}"
				);
			}
			else if (player.Stats.CheckpointPB[checkpoint][pStyle] != null && player.Stats.CheckpointPB[checkpoint][pStyle].RunTime > checkpoint_run_time) // Player beating their existing Checkpoint personal best
			{
				int timeImprove = player.Stats.CheckpointPB[checkpoint][pStyle].RunTime - checkpoint_run_time;
				ChatAnnounce.Send(ChatAnnounce.Kind.Pb, player, $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpointpb_improved",
					player.Controller.PlayerName, checkpoint, PlayerHud.FormatTime(checkpoint_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(player.Stats.CheckpointPB[checkpoint][pStyle].RunTime)]}");
			}

			// Save checkpoint segment run
			await player.Stats.ThisRun.SaveMapTime(player, checkpoint: checkpoint, run_ticks: checkpoint_run_time,
				segmentStartVelX: startVelX, segmentStartVelY: startVelY, segmentStartVelZ: startVelZ,
				segmentEndVelX: endVelX, segmentEndVelY: endVelY, segmentEndVelZ: endVelZ, segmentSync: sync
			); // Save the Checkpoint MapTime PB data
		}
		else
		{
			LogRun(player, 3, checkpoint, checkpoint_run_time, sync); // Not a PB - history only
		}
	}


	public static void PrintSituations(Player player)
	{
		Console.WriteLine($"========================== FOUND SITUATIONS ==========================");
		for (int i = 0; i < player.ReplayRecorder.Frames.Count; i++)
		{
			ReplayFrame x = player.ReplayRecorder.Frames[i];
			switch (x.Situation)
			{
				case ReplayFrameSituation.START_ZONE_ENTER:
					Console.WriteLine($"START_ZONE_ENTER: {i} | Situation {x.Situation}");
					break;
				case ReplayFrameSituation.START_ZONE_EXIT:
					Console.WriteLine($"START_ZONE_EXIT: {i} | Situation {x.Situation}");
					break;
				case ReplayFrameSituation.STAGE_ZONE_ENTER:
					Console.WriteLine($"STAGE_ZONE_ENTER: {i} | Situation {x.Situation}");
					break;
				case ReplayFrameSituation.STAGE_ZONE_EXIT:
					Console.WriteLine($"STAGE_ZONE_EXIT: {i} | Situation {x.Situation}");
					break;
				case ReplayFrameSituation.CHECKPOINT_ZONE_ENTER:
					Console.WriteLine($"CHECKPOINT_ZONE_ENTER: {i} | Situation {x.Situation}");
					break;
				case ReplayFrameSituation.CHECKPOINT_ZONE_EXIT:
					Console.WriteLine($"CHECKPOINT_ZONE_EXIT: {i} | Situation {x.Situation}");
					break;
				case ReplayFrameSituation.END_ZONE_ENTER:
					Console.WriteLine($"END_ZONE_ENTER: {i} | Situation {x.Situation}");
					break;
				case ReplayFrameSituation.END_ZONE_EXIT:
					Console.WriteLine($"END_ZONE_EXIT: {i} | Situation {x.Situation}");
					break;
			}
		}
		Console.WriteLine("========================== MapSituations: {0} | START_ZONE_ENTER = {1} | START_ZONE_EXIT = {2} | END_ZONE_ENTER = {3} ==========================",
			player.ReplayRecorder.MapSituations.Count,
			player.ReplayRecorder.MapSituations[0], player.ReplayRecorder.MapSituations[1], player.ReplayRecorder.MapSituations[2]);
		Console.WriteLine("========================== Total Frames: {0} ==========================", player.ReplayRecorder.Frames.Count);
	}

}

