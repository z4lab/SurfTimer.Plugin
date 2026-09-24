using CounterStrikeSharp.API;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurfTimer.Data;
using SurfTimer.Shared.DTO;
using SurfTimer.Shared.Entities;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SurfTimer;

/// <summary>
/// This class stores data for the current run.
/// </summary>
public class CurrentRun : RunStatsEntity
{
	private readonly ILogger<CurrentRun> _logger;
	private readonly IDataAccessService _dataService;

	public Dictionary<int, CheckpointEntity> Checkpoints { get; set; }

	internal CurrentRun()
	{
		_logger = SurfTimer.ServiceProvider.GetRequiredService<ILogger<CurrentRun>>();
		_dataService = SurfTimer.ServiceProvider.GetRequiredService<IDataAccessService>();

		Checkpoints = new Dictionary<int, CheckpointEntity>();
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
	internal async Task SaveMapTime(Player player, short bonus = 0, short stage = 0, short checkpoint = 0, int run_ticks = -1,
		float? segmentStartVelX = null, float? segmentStartVelY = null, float? segmentStartVelZ = null,
		float? segmentEndVelX = null, float? segmentEndVelY = null, float? segmentEndVelZ = null,
		[CallerMemberName] string methodName = "")
	{
		string replay_frames = "";
		int style = player.Timer.Style;
		int mapTimeId = 0;
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

		/// Test Time Saving: if (methodName != "TestSetPb")
		replay_frames = player.ReplayRecorder.TrimReplay(
			player,
			type: recType,
			bonus: bonus,
			stage: stage,
			lastStage: stage == SurfTimer.CurrentMap.Stages,
			checkpoint: checkpoint,
			lastCheckpoint: checkpoint == SurfTimer.CurrentMap.TotalCheckpoints
		);

		_logger.LogTrace("[{ClassName}] {MethodName} -> Sending total of {Frames} serialized and compressed replay frames.",
			nameof(CurrentRun), methodName, replay_frames.Length
		);

		var stopwatch = Stopwatch.StartNew();
		var mapTime = new MapTimeRunDataDto
		{
			PlayerID = player.Profile.ID,
			MapID = SurfTimer.CurrentMap.ID,
			Style = player.Timer.Style,
			Type = recType,
			Stage = stage != 0 ? stage : (bonus != 0 ? bonus : checkpoint),
			RunTime = run_ticks == -1 ? this.RunTime : run_ticks,
			StartVelX = segmentStartVelX ?? this.StartVelX,
			StartVelY = segmentStartVelY ?? this.StartVelY,
			StartVelZ = segmentStartVelZ ?? this.StartVelZ,
			EndVelX = segmentEndVelX ?? this.EndVelX,
			EndVelY = segmentEndVelY ?? this.EndVelY,
			EndVelZ = segmentEndVelZ ?? this.EndVelZ,
			ReplayFrames = replay_frames,
			Checkpoints = this.Checkpoints
		};

		switch (recType)
		{
			case 0:
				mapTimeId = player.Stats.PB[style].ID;
				break;
			case 1:
				mapTimeId = player.Stats.BonusPB[bonus][style].ID;
				break;
			case 2:
				mapTimeId = player.Stats.StagePB[stage][style].ID;
				break;
			case 3:
				mapTimeId = player.Stats.CheckpointPB[checkpoint][style].ID;
				break;
		}

		if (mapTimeId <= 0)
			mapTimeId = await _dataService.InsertMapTimeAsync(mapTime);
		else
			_ = await _dataService.UpdateMapTimeAsync(mapTime, mapTimeId);


		// Reload the times for the map
		await SurfTimer.CurrentMap.LoadMapRecordRuns();

		_logger.LogTrace("[{ClassName}] {MethodName} -> Loading data for run {ID} with type {Type}.",
			nameof(CurrentRun), methodName, mapTimeId, recType
		);

		// Reload the player PB time (could possibly be skipped as we have mapTimeId after inserting)
		switch (recType)
		{
			case 0:
				player.Stats.PB[player.Timer.Style].ID = mapTimeId;
				await player.Stats.PB[player.Timer.Style].LoadPlayerSpecificMapTimeData(player);
				break;
			case 1:
				player.Stats.BonusPB[bonus][player.Timer.Style].ID = mapTimeId;
				await player.Stats.BonusPB[bonus][player.Timer.Style].LoadPlayerSpecificMapTimeData(player);
				break;
			case 2:
				player.Stats.StagePB[stage][player.Timer.Style].ID = mapTimeId;
				await player.Stats.StagePB[stage][player.Timer.Style].LoadPlayerSpecificMapTimeData(player);
				break;
			case 3:
				player.Stats.CheckpointPB[checkpoint][player.Timer.Style].ID = mapTimeId;
				await player.Stats.CheckpointPB[checkpoint][player.Timer.Style].LoadPlayerSpecificMapTimeData(player);
				break;
		}

		stopwatch.Stop();
		_logger.LogInformation("[{Class}] {Method} -> Finished SaveMapTime for '{Name}' (ID {ID}) in {Elapsed}ms | API = {API}",
			nameof(CurrentRun), methodName, player.Profile.Name, mapTimeId, stopwatch.ElapsedMilliseconds, Config.Api.GetApiOnly()
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
		float endVelX = 0, float endVelY = 0, float endVelZ = 0)
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
				Server.PrintToChatAll($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagewr_improved",
					player.Controller.PlayerName, stage, PlayerHud.FormatTime(stage_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime)]}"
				);
			}
			else if (SurfTimer.CurrentMap.StageWR[stage][pStyle].ID == -1) // No Stage record was set on the map
			{
				Server.PrintToChatAll($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagewr_set",
					player.Controller.PlayerName, stage, PlayerHud.FormatTime(stage_run_time)]}"
				);
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
				Server.PrintToChatAll($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagepb_improved",
					player.Controller.PlayerName, stage, PlayerHud.FormatTime(stage_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(player.Stats.StagePB[stage][pStyle].RunTime)]}"
				);
			}

			player.ReplayRecorder.IsSaving = true;

			// Save stage run
			await player.Stats.ThisRun.SaveMapTime(player, stage: stage, run_ticks: stage_run_time,
				segmentStartVelX: startVelX, segmentStartVelY: startVelY, segmentStartVelZ: startVelZ,
				segmentEndVelX: endVelX, segmentEndVelY: endVelY, segmentEndVelZ: endVelZ
			); // Save the Stage MapTime PB data
		}
		else if (stage_run_time > SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime && player.Timer.IsStageMode) // Player is behind the Stage WR for the map
		{
			int timeImprove = stage_run_time - SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime;
			player.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stagewr_missed",
				stage, PlayerHud.FormatTime(stage_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(SurfTimer.CurrentMap.StageWR[stage][pStyle].RunTime)]}"
			);
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
	internal static async Task SaveCheckpointTime(Player player, short checkpoint = -1, int checkpoint_run_time = -1, bool saveLastCheckpoint = false)
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
				Server.PrintToChatAll($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpointwr_improved",
					player.Controller.PlayerName, checkpoint, PlayerHud.FormatTime(checkpoint_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(SurfTimer.CurrentMap.CheckpointWR[checkpoint][pStyle].RunTime)]}"
				);
			}
			else if (SurfTimer.CurrentMap.CheckpointWR[checkpoint][pStyle].ID == -1) // No Checkpoint record was set on the map
			{
				Server.PrintToChatAll($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpointwr_set",
					player.Controller.PlayerName, checkpoint, PlayerHud.FormatTime(checkpoint_run_time)]}"
				);
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
				Server.PrintToChatAll($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpointpb_improved",
					player.Controller.PlayerName, checkpoint, PlayerHud.FormatTime(checkpoint_run_time), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(player.Stats.CheckpointPB[checkpoint][pStyle].RunTime)]}"
				);
			}

			player.ReplayRecorder.IsSaving = true;

			// Save checkpoint segment run
			await player.Stats.ThisRun.SaveMapTime(player, checkpoint: checkpoint, run_ticks: checkpoint_run_time); // Save the Checkpoint MapTime PB data
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

