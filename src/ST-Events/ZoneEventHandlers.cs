using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using SurfTimer.Shared.Entities;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>
	/// Runs a save 1s later (so frames AFTER touching the zone exist for the replay trim) and blocks
	/// resets from the moment it's scheduled until it finishes - otherwise a !r in that window clears
	/// the recorder's Frames before they're trimmed. The lock is always released, even on failure.
	/// </summary>
	private void ScheduleRunSave(Player player, string description, Func<Task> save)
	{
		player.ReplayRecorder.BeginSave();
		AddTimer(1.0f, async () =>
		{
			try
			{
				await save();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[{ClassName}] {Description} failed for '{Name}'",
					nameof(SurfTimer), description, player.Profile.Name);
			}
			finally
			{
				player.ReplayRecorder.EndSave();
			}
		});
	}

	/* StartTouch */
	private void StartTouchHandleMapEndZone(Player player, [CallerMemberName] string methodName = "")
	{
		// Only a running map/stage run finishes here. A second map_end trigger, re-entering one, or
		// touching it during a bonus must not overwrite the end speed or save anything again.
		if (!player.Timer.IsRunning || player.Timer.IsBonusMode)
			return;

		// Get velocities for DB queries
		// Get the velocity of the player - we will be using this values to compare and write to DB
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();
		int pStyle = player.Timer.Style;

		// The map end is also the last stage's finish - captured before the timer is stopped below
		bool finishedStageForRepeat = player.IsRepeatMode && CurrentMap.Stages > 0;

		player.Controller.PrintToCenter($"Map End");

		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.END_ZONE_ENTER;
		player.ReplayRecorder.MapSituations.Add(player.Timer.Ticks);

		player.Stats.ThisRun.RunTime = player.Timer.Ticks; // End time for the Map run
		player.Stats.ThisRun.EndVelX = velocity.X; // End speed for the Map run
		player.Stats.ThisRun.EndVelY = velocity.Y; // End speed for the Map run
		player.Stats.ThisRun.EndVelZ = velocity.Z; // End speed for the Map run


		// MAP END ZONE - Map RUN
		if (player.Timer.IsRunning && !player.Timer.IsStageMode)
		{
			player.Timer.Stop();
			bool saveMapTime = false;
			string PracticeString = "";
			if (player.Timer.IsPracticeMode)
				PracticeString = $"({ChatColors.Grey}Practice{ChatColors.Default}) ";

			if (player.Timer.Ticks < CurrentMap.WR[pStyle].RunTime) // Player beat the Map WR
			{
				saveMapTime = true;
				int timeImprove = CurrentMap.WR[pStyle].RunTime - player.Timer.Ticks;
				Server.PrintToChatAll($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["mapwr_improved",
					player.Controller.PlayerName, PlayerHud.FormatTime(player.Timer.Ticks), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(CurrentMap.WR[pStyle].RunTime)]}"
				);
			}
			else if (CurrentMap.WR[pStyle].ID == -1) // No record was set on the map
			{
				saveMapTime = true;
				Server.PrintToChatAll($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["mapwr_set",
					player.Controller.PlayerName, PlayerHud.FormatTime(player.Timer.Ticks)]}"
				);
			}
			else if (player.Stats.PB[pStyle].RunTime <= 0) // Player first ever PersonalBest for the map
			{
				saveMapTime = true;
				player.Controller.PrintToChat($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["mappb_set",
					PlayerHud.FormatTime(player.Timer.Ticks)]}"
				);
			}
			else if (player.Timer.Ticks < player.Stats.PB[pStyle].RunTime) // Player beating their existing PersonalBest for the map
			{
				saveMapTime = true;
				int timeImprove = player.Stats.PB[pStyle].RunTime - player.Timer.Ticks;
				Server.PrintToChatAll($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["mappb_improved",
					player.Controller.PlayerName, PlayerHud.FormatTime(player.Timer.Ticks), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(player.Stats.PB[pStyle].RunTime)]}"
				);
			}
			else // Player did not beat their existing PersonalBest for the map nor the map record
			{
				player.Controller.PrintToChat($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["mappb_missed",
					PlayerHud.FormatTime(player.Timer.Ticks)]}"
				);
			}

			if (saveMapTime && !player.Timer.IsPracticeMode)
			{
				ScheduleRunSave(player, "SaveMapTime", () => player.Stats.ThisRun.SaveMapTime(player));
			}

			// Add entry in DB for the run
			if (!player.Timer.IsPracticeMode)
			{
				// Should we also save a last stage run?
				if (CurrentMap.Stages > 0)
				{
					float lastStageEntryVelX = player.Timer.StageEntryVelX;
					float lastStageEntryVelY = player.Timer.StageEntryVelY;
					float lastStageEntryVelZ = player.Timer.StageEntryVelZ;
					ScheduleRunSave(player, "SaveStageTime (last)", async () =>
					{
						// This calculation is wrong unless we wait for a bit in order for the `END_ZONE_ENTER` to be available in the `Frames` object
						int stage_run_time = player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.END_ZONE_ENTER) - player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.STAGE_ZONE_EXIT);

						await CurrentRun.SaveStageTime(player, CurrentMap.Stages, stage_run_time, true,
							startVelX: lastStageEntryVelX, startVelY: lastStageEntryVelY, startVelZ: lastStageEntryVelZ,
							endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z);

						player.HUD.DisplayStageMessage(CurrentMap.Stages, stage_run_time, velocity);
					});
				}
				// Should we also save a last checkpoint segment run? (non-staged maps only)
				else if (CurrentMap.Stages == 0 && CurrentMap.TotalCheckpoints > 0)
				{
					float lastCheckpointEntryVelX = player.Timer.CheckpointEntryVelX;
					float lastCheckpointEntryVelY = player.Timer.CheckpointEntryVelY;
					float lastCheckpointEntryVelZ = player.Timer.CheckpointEntryVelZ;
					ScheduleRunSave(player, "SaveCheckpointTime (last)", async () =>
					{
						// This calculation is wrong unless we wait for a bit in order for the `END_ZONE_ENTER` to be available in the `Frames` object
						int checkpoint_run_time = player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.END_ZONE_ENTER) - player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.CHECKPOINT_ZONE_EXIT);

						await CurrentRun.SaveCheckpointTime(player, (short)CurrentMap.TotalCheckpoints, checkpoint_run_time, true,
							startVelX: lastCheckpointEntryVelX, startVelY: lastCheckpointEntryVelY, startVelZ: lastCheckpointEntryVelZ,
							endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z);

						player.HUD.DisplayCheckpointSegmentMessage((short)CurrentMap.TotalCheckpoints, checkpoint_run_time, velocity);
					});
				}

				// This section checks if the PB is better than WR
				if (player.Timer.Ticks < CurrentMap.WR[pStyle].RunTime || CurrentMap.WR[pStyle].ID == -1)
				{
					AddTimer(2f, () =>
					{
						Console.WriteLine("CS2 Surf DEBUG >> OnTriggerStartTouch (Map end zone) -> WR/PB");
						CurrentMap.ReplayManager.MapWR.Start(); // Start the replay again
						CurrentMap.ReplayManager.MapWR.FormatBotName();
					});
				}

			}
		}
		// MAP END ZONE - Stage RUN
		else if (player.Timer.IsStageMode)
		{
			player.Timer.Stop();

			if (!player.Timer.IsPracticeMode)
			{
				float lastStageEntryVelX = player.Timer.StageEntryVelX;
				float lastStageEntryVelY = player.Timer.StageEntryVelY;
				float lastStageEntryVelZ = player.Timer.StageEntryVelZ;
				ScheduleRunSave(player, "SaveStageTime (last, stage mode)", async () =>
				{
					// This calculation is wrong unless we wait for a bit in order for the `END_ZONE_ENTER` to be available in the `Frames` object
					int stage_run_time = player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.END_ZONE_ENTER) - player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.STAGE_ZONE_EXIT);

					await CurrentRun.SaveStageTime(player, CurrentMap.Stages, stage_run_time, true,
						startVelX: lastStageEntryVelX, startVelY: lastStageEntryVelY, startVelZ: lastStageEntryVelZ,
						endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z);

					player.HUD.DisplayStageMessage(CurrentMap.Stages, stage_run_time, velocity);
				});
			}
		}

		if (finishedStageForRepeat)
			ScheduleRepeatTeleport(player, (short)CurrentMap.Stages);

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.Lime}StartTouchFunc{ChatColors.Default} -> {ChatColors.Red}Map Stop Zone");
#endif
	}

	private static void StartTouchHandleMapStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
		// We shouldn't start timer and reset data until MapTime has been saved - mostly concerns the Replays and trimming the correct parts
		if (!player.ReplayRecorder.IsSaving)
		{
			player.ReplayRecorder.Reset(); // Start replay recording
			player.ReplayRecorder.Start(); // Start replay recording
			player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.START_ZONE_ENTER;
			player.ReplayRecorder.MapSituations.Add(player.ReplayRecorder.Frames.Count);
			player.Timer.Reset();
			player.Stats.ThisRun.Checkpoints.Clear();
			player.Controller.PrintToCenter($"Map Start ({zone.Name})");

#if DEBUG
			player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.Lime}StartTouchFunc{ChatColors.Default} -> {ChatColors.Green}Map Start Zone");
#endif
		}
		else
		{
			player.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["reset_delay"]}");
		}
	}

	private void StartTouchHandleStageStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
		// Get velocities for DB queries
		// Get the velocity of the player - we will be using this values to compare and write to DB
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();
		short stage = zone.Number;

		if (!player.ReplayRecorder.IsRecording)
			player.ReplayRecorder.Start();

		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.STAGE_ZONE_ENTER;
		player.ReplayRecorder.StageEnterSituations.Add(player.ReplayRecorder.Frames.Count);

		bool failed_stage = false;
		if (player.Timer.Stage == stage)
			failed_stage = true;

		// Captured before the stage-mode branch below resets the timer
		bool finishedStageForRepeat = player.IsRepeatMode && stage > 1 && !failed_stage
			&& player.Timer.IsRunning && !player.Timer.IsBonusMode;

		// Reset/Stop the Stage timer
		// Save a Stage run when `IsStageMode` is active - (`stage - 1` to get the previous stage data)
		if (player.Timer.IsStageMode)
		{
			if (stage > 1 && !failed_stage && !player.Timer.IsPracticeMode)
			{
				int stage_run_time = player.Timer.Ticks;
				float entryVelX = player.Timer.StageEntryVelX;
				float entryVelY = player.Timer.StageEntryVelY;
				float entryVelZ = player.Timer.StageEntryVelZ;
				ScheduleRunSave(player, $"SaveStageTime (stage {stage - 1}, stage mode)", () =>
					CurrentRun.SaveStageTime(player, (short)(stage - 1), stage_run_time,
						startVelX: entryVelX, startVelY: entryVelY, startVelZ: entryVelZ,
						endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z));

				player.HUD.DisplayStageMessage((short)(stage - 1), stage_run_time, velocity);
			}
			player.Timer.Reset();
			player.Timer.IsStageMode = true;
		}

		player.Timer.Stage = stage;

#if DEBUG
		Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Stage start zones) -> player.Timer.IsRunning: {player.Timer.IsRunning}");
		Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Stage start zones) -> !player.Timer.IsStageMode: {!player.Timer.IsStageMode}");
		Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Stage start zones) -> player.Stats.ThisRun.Checkpoint.Count <= stage: {player.Stats.ThisRun.Checkpoints.Count <= stage}");
#endif

		// Guard against re-entering the current (not-yet-advanced-past) stage zone: this must check
		// whether the checkpoint this zone-entry would complete (stage - 1) has already been
		// recorded, NOT compare Count against `stage` directly - those differ by exactly 1 by
		// design, which was letting a bounce-in/bounce-out re-trigger the message/save every time.
		if (player.Timer.IsRunning && !player.Timer.IsStageMode && !player.Stats.ThisRun.Checkpoints.ContainsKey((short)(stage - 1)))
		{
			int stage_run_time = player.Timer.Ticks - player.Stats.ThisRun.RunTime; // player.Stats.ThisRun.RunTime should be the Tick we left the previous Stage zone

			// Save Stage MapTime during a Map run
			if (stage > 1 && !failed_stage && !player.Timer.IsPracticeMode)
			{
				float entryVelX = player.Timer.StageEntryVelX;
				float entryVelY = player.Timer.StageEntryVelY;
				float entryVelZ = player.Timer.StageEntryVelZ;

				ScheduleRunSave(player, $"SaveStageTime (stage {stage - 1})", () =>
					CurrentRun.SaveStageTime(player, (short)(stage - 1), stage_run_time,
						startVelX: entryVelX, startVelY: entryVelY, startVelZ: entryVelZ,
						endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z));
			}

			player.Timer.Checkpoint = (short)(stage - 1); // Stage = Checkpoint when in a run on a Staged map

#if DEBUG
			Console.WriteLine($"============== Initial entity value: {zone.Number} | Assigned to `stage`: {stage} | player.Timer.Checkpoint: {stage - 1}");
			Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Stage start zones) -> player.Stats.PB[{player.Timer.Style}].Checkpoint.Count = {player.Stats.PB[player.Timer.Style].Checkpoints.Count}");
#endif

			// Print Stage completion message (staged maps show Stage records, never the generic
			// Checkpoint comparison - that's for non-staged maps only)
			player.HUD.DisplayStageMessage((short)(stage - 1), stage_run_time, velocity);

			// store the checkpoint in the player's current run checkpoints used for Checkpoint functionality
			if (!player.Stats.ThisRun.Checkpoints.ContainsKey(player.Timer.Checkpoint))
			{
				var cp2 = new CheckpointEntity(player.Timer.Checkpoint,
												player.Timer.Ticks,
												velocity.X,
												velocity.Y,
												velocity.Z,
												-1.0f,
												-1.0f,
												-1.0f,
												0,
												1);
				player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint] = cp2;
			}
			else
			{
				player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint].Attempts++;
			}
		}

		if (finishedStageForRepeat)
			ScheduleRepeatTeleport(player, (short)(stage - 1));

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.Lime}StartTouchFunc{ChatColors.Default} -> {ChatColors.Yellow}Stage {zone.Number} Start Zone");
#endif
	}

	private void StartTouchHandleCheckpointZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
		// Get velocities for DB queries
		// Get the velocity of the player - we will be using this values to compare and write to DB
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();
		short checkpoint = zone.Number;

		bool failed_checkpoint = player.Timer.Checkpoint == checkpoint;

		player.Timer.Checkpoint = checkpoint;

		// Guard against re-entering the current (not-yet-advanced-past) checkpoint zone using an
		// explicit "already recorded" check rather than a Count comparison (see the Stage-zone
		// handler above for the bug this pattern avoids).
		if (player.Timer.IsRunning && !player.Timer.IsStageMode && !player.Stats.ThisRun.Checkpoints.ContainsKey(checkpoint))
		{
#if DEBUG
			int pStyle = player.Timer.Style;
			Console.WriteLine($"============== Initial entity value: {zone.Number} | Assigned to `checkpoint`: {zone.Number}");
			Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Checkpoint zones) -> player.Stats.PB[{pStyle}].Checkpoint.Count = {player.Stats.PB[pStyle].Checkpoints.Count}");
#endif

			if (player.Timer.IsRunning && player.ReplayRecorder.IsRecording)
			{
				player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.CHECKPOINT_ZONE_ENTER;
				player.ReplayRecorder.CheckpointEnterSituations.Add(player.Timer.Ticks);
			}

			int checkpoint_run_time = player.Timer.Ticks - player.Stats.ThisRun.RunTime; // player.Stats.ThisRun.RunTime should be the Tick we left the previous Checkpoint zone
			short completedCheckpoint = (short)(checkpoint - 1);

			// Save Checkpoint segment MapTime during a Map run (non-staged maps only)
			if (SurfTimer.CurrentMap.Stages == 0 && checkpoint > 1 && !failed_checkpoint && !player.Timer.IsPracticeMode)
			{
				float entryVelX = player.Timer.CheckpointEntryVelX;
				float entryVelY = player.Timer.CheckpointEntryVelY;
				float entryVelZ = player.Timer.CheckpointEntryVelZ;

				ScheduleRunSave(player, $"SaveCheckpointTime (checkpoint {completedCheckpoint})", () =>
					CurrentRun.SaveCheckpointTime(player, completedCheckpoint, checkpoint_run_time,
						startVelX: entryVelX, startVelY: entryVelY, startVelZ: entryVelZ,
						endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z));
			}

			// Print Checkpoint completion message (non-staged maps compare against the standalone
			// Checkpoint PB/WR records; the rare staged-map-with-checkpoint-zones case falls back to
			// the generic per-run split message since no standalone Checkpoint records exist there)
			if (SurfTimer.CurrentMap.Stages == 0)
				player.HUD.DisplayCheckpointSegmentMessage(completedCheckpoint, checkpoint_run_time, velocity);
			else
				player.HUD.DisplayCheckpointMessages();

			if (!player.Stats.ThisRun.Checkpoints.ContainsKey(checkpoint))
			{
				// store the checkpoint in the player's current run checkpoints used for Checkpoint functionality
				var cp2 = new CheckpointEntity(checkpoint,
												player.Timer.Ticks,
												velocity.X,
												velocity.Y,
												velocity.Z,
												-1.0f,
												-1.0f,
												-1.0f,
												0,
												1);
				player.Stats.ThisRun.Checkpoints[checkpoint] = cp2;
			}
			else
			{
				player.Stats.ThisRun.Checkpoints[checkpoint].Attempts++;
			}
		}

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.Lime}StartTouchFunc{ChatColors.Default} -> {ChatColors.LightBlue}Checkpoint {zone.Number} Zone");
#endif
	}

	private static void StartTouchHandleBonusStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
		// Same as the map start: don't reset the recorder/timer until a pending save has trimmed its replay
		if (player.ReplayRecorder.IsSaving)
		{
			player.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["reset_delay"]}");
			return;
		}

		short bonus = zone.Number;
		player.Timer.Bonus = bonus;

		player.Timer.Reset();
		player.Timer.IsBonusMode = true;


		player.ReplayRecorder.Reset();
		player.ReplayRecorder.Start(); // Start replay recording
		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.START_ZONE_ENTER;
		player.ReplayRecorder.BonusSituations.Add(player.ReplayRecorder.Frames.Count);
		Console.WriteLine($"START_ZONE_ENTER: player.ReplayRecorder.BonusSituations.Add({player.ReplayRecorder.Frames.Count})");

		player.Controller.PrintToCenter($"Bonus Start ({zone.Name})");

#if DEBUG
		Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Bonus start zones) -> player.Timer.IsRunning: {player.Timer.IsRunning}");
		Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Bonus start zones) -> !player.Timer.IsBonusMode: {!player.Timer.IsBonusMode}");
#endif
	}

	private void StartTouchHandleBonusEndZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
		short bonus_idx = zone.Number;

		// Only the end of the bonus actually being run finishes it - not a map run passing through,
		// another bonus, or a second end trigger of the same bonus after finishing
		if (!player.Timer.IsRunning || !player.Timer.IsBonusMode || player.Timer.Bonus != bonus_idx
			|| bonus_idx >= CurrentMap.BonusWR.Length)
			return;

		// Get velocities for DB queries
		// Get the velocity of the player - we will be using this values to compare and write to DB
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();
		int pStyle = player.Timer.Style;

		player.Timer.Stop();
		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.END_ZONE_ENTER;
		player.ReplayRecorder.BonusSituations.Add(player.Timer.Ticks);

		player.Stats.ThisRun.RunTime = player.Timer.Ticks; // End time for the run
		player.Stats.ThisRun.EndVelX = velocity.X; // End pre speed for the run
		player.Stats.ThisRun.EndVelY = velocity.Y; // End pre speed for the run
		player.Stats.ThisRun.EndVelZ = velocity.Z; // End pre speed for the run

		bool saveBonusTime = false;
		string PracticeString = "";
		if (player.Timer.IsPracticeMode)
			PracticeString = $"({ChatColors.Grey}Practice{ChatColors.Default}) ";

		if (player.Timer.Ticks < CurrentMap.BonusWR[bonus_idx][pStyle].RunTime) // Player beat the Bonus WR
		{
			saveBonusTime = true;
			int timeImprove = CurrentMap.BonusWR[bonus_idx][pStyle].RunTime - player.Timer.Ticks;
			Server.PrintToChatAll($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["bonuswr_improved",
				player.Controller.PlayerName, bonus_idx, PlayerHud.FormatTime(player.Timer.Ticks), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(CurrentMap.BonusWR[bonus_idx][pStyle].RunTime)]}"
			);
		}
		else if (CurrentMap.BonusWR[bonus_idx][pStyle].ID == -1) // No Bonus record was set on the map
		{
			saveBonusTime = true;
			Server.PrintToChatAll($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["bonuswr_set",
				player.Controller.PlayerName, bonus_idx, PlayerHud.FormatTime(player.Timer.Ticks)]}"
			);
		}
		else if (player.Stats.BonusPB[bonus_idx][pStyle].RunTime <= 0) // Player first ever PersonalBest for the bonus
		{
			saveBonusTime = true;
			player.Controller.PrintToChat($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["bonuspb_set",
				bonus_idx, PlayerHud.FormatTime(player.Timer.Ticks)]}"
			);
		}
		else if (player.Timer.Ticks < player.Stats.BonusPB[bonus_idx][pStyle].RunTime) // Player beating their existing PersonalBest for the bonus
		{
			saveBonusTime = true;
			int timeImprove = player.Stats.BonusPB[bonus_idx][pStyle].RunTime - player.Timer.Ticks;
			Server.PrintToChatAll($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["bonuspb_improved",
				player.Controller.PlayerName, bonus_idx, PlayerHud.FormatTime(player.Timer.Ticks), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(player.Stats.BonusPB[bonus_idx][pStyle].RunTime)]}"
			);
		}
		else // Player did not beat their existing personal best for the bonus
		{
			player.Controller.PrintToChat($"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["bonuspb_missed",
				bonus_idx, PlayerHud.FormatTime(player.Timer.Ticks)]}"
			);
		}

		if (!player.Timer.IsPracticeMode)
		{
			if (saveBonusTime)
			{
				ScheduleRunSave(player, $"SaveMapTime (bonus {bonus_idx})", () => player.Stats.ThisRun.SaveMapTime(player, bonus: bonus_idx));
			}
		}
	}


	/* EndTouch */
	private static void EndTouchHandleMapEndZone(Player player, [CallerMemberName] string methodName = "")
	{
		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.END_ZONE_EXIT;
	}

	private static void EndTouchHandleMapStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();

		// MAP START ZONE
		if (!player.Timer.IsStageMode && !player.Timer.IsBonusMode)
		{
			player.Timer.Start();
			player.Stats.ThisRun.RunTime = player.Timer.Ticks;
			player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.START_ZONE_EXIT;
			player.ReplayRecorder.MapSituations.Add(player.ReplayRecorder.Frames.Count);
#if DEBUG
			player.Controller.PrintToChat($"{ChatColors.Red}START_ZONE_EXIT: player.ReplayRecorder.MapSituations.Add({player.ReplayRecorder.Frames.Count})");
			Console.WriteLine($"START_ZONE_EXIT: player.ReplayRecorder.MapSituations.Add({player.ReplayRecorder.Frames.Count})");
#endif
		}

		// Prespeed display
		string prespeedPrefix = CurrentMap.Stages > 0 ? "Stage 1 - " : "";
		player.Controller.PrintToCenter($"{prespeedPrefix}Prespeed: {velocity.velMag():0} u/s");
		player.Stats.ThisRun.StartVelX = velocity.X; // Start pre speed for the Map run
		player.Stats.ThisRun.StartVelY = velocity.Y; // Start pre speed for the Map run
		player.Stats.ThisRun.StartVelZ = velocity.Z; // Start pre speed for the Map run
		player.Timer.StageEntryVelX = velocity.X; // Entry speed for Stage 1 (starts at map start)
		player.Timer.StageEntryVelY = velocity.Y;
		player.Timer.StageEntryVelZ = velocity.Z;
		player.Timer.CheckpointEntryVelX = velocity.X; // Entry speed for Checkpoint 1 (starts at map start)
		player.Timer.CheckpointEntryVelY = velocity.Y;
		player.Timer.CheckpointEntryVelZ = velocity.Z;

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.LightRed}EndTouchFunc{ChatColors.Default} -> {ChatColors.Green}Map Start Zone");
#endif
	}

	private static void EndTouchHandleStageStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.LightRed}EndTouchFunc{ChatColors.Default} -> {ChatColors.Yellow}Stage {zone.Number} Start Zone");
		Console.WriteLine($"===================== player.Timer.Checkpoint {player.Timer.Checkpoint} - player.Stats.ThisRun.Checkpoint.Count {player.Stats.ThisRun.Checkpoints.Count}");
#endif
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();
		short stage = zone.Number;

		// Set replay situation
		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.STAGE_ZONE_EXIT;
		player.ReplayRecorder.StageExitSituations.Add(player.ReplayRecorder.Frames.Count);
		player.Stats.ThisRun.RunTime = player.Timer.Ticks;

		// Entry speed for the stage just entered - shown regardless of how the stage was entered
		// (normal run or !s practice)
		player.Timer.StageEntryVelX = velocity.X;
		player.Timer.StageEntryVelY = velocity.Y;
		player.Timer.StageEntryVelZ = velocity.Z;
		player.Controller.PrintToCenter($"Stage {stage} - Prespeed: {velocity.velMag().ToString("0")} u/s");

		// Start the Stage timer
		if (player.Timer.IsStageMode && player.Timer.Stage == stage)
		{
			player.Timer.Start();
		}
		else if (player.Timer.IsRunning && player.Stats.ThisRun.Checkpoints.TryGetValue(player.Timer.Checkpoint, out CheckpointEntity? currentCheckpoint))
		{
#if DEBUG
			Console.WriteLine($"currentCheckpoint.EndVelX {currentCheckpoint.EndVelX} - velocity.X {velocity.X}");
			Console.WriteLine($"currentCheckpoint.EndVelY {currentCheckpoint.EndVelY} - velocity.Y {velocity.Y}");
			Console.WriteLine($"currentCheckpoint.EndVelZ {currentCheckpoint.EndVelZ} - velocity.Z {velocity.Z}");
			Console.WriteLine($"currentCheckpoint.Attempts {currentCheckpoint.Attempts}");
#endif

			// Update the Checkpoint object values
			currentCheckpoint.EndVelX = velocity.X;
			currentCheckpoint.EndVelY = velocity.Y;
			currentCheckpoint.EndVelZ = velocity.Z;
			currentCheckpoint.EndTouch = player.Timer.Ticks;
		}
	}

	private static void EndTouchHandleCheckpointZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.LightRed}EndTouchFunc{ChatColors.Default} -> {ChatColors.Yellow}Checkpoint {zone.Number} Start Zone");
		Console.WriteLine($"===================== player.Timer.Checkpoint {player.Timer.Checkpoint} - player.Stats.ThisRun.Checkpoint.Count {player.Stats.ThisRun.Checkpoints.Count}");
#endif
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();

		// Entry speed for the checkpoint segment just entered - shown regardless of how it was
		// entered (non-staged maps only, mirrors EndTouchHandleStageStartZone's capture)
		player.Timer.CheckpointEntryVelX = velocity.X;
		player.Timer.CheckpointEntryVelY = velocity.Y;
		player.Timer.CheckpointEntryVelZ = velocity.Z;

		// This will populate the End velocities for the given Checkpoint zone (Stage = Checkpoint when in a Map Run)
		if (player.Timer.Checkpoint != 0 && player.Timer.Checkpoint <= player.Stats.ThisRun.Checkpoints.Count)
		{
#if DEBUG
			Console.WriteLine($"currentCheckpoint.EndVelX {player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint].EndVelX} - velocity.X {velocity.X}");
			Console.WriteLine($"currentCheckpoint.EndVelY {player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint].EndVelY} - velocity.Y {velocity.Y}");
			Console.WriteLine($"currentCheckpoint.EndVelZ {player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint].EndVelZ} - velocity.Z {velocity.Z}");
#endif

			if (player.Timer.IsRunning && player.ReplayRecorder.IsRecording)
			{
				player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.CHECKPOINT_ZONE_EXIT;
				player.ReplayRecorder.CheckpointExitSituations.Add(player.Timer.Ticks);
			}

			// Update the Checkpoint object values
			player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint].EndVelX = velocity.X;
			player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint].EndVelY = velocity.Y;
			player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint].EndVelZ = velocity.Z;
			player.Stats.ThisRun.Checkpoints[player.Timer.Checkpoint].EndTouch = player.Timer.Ticks;

			// Show Prespeed for stages - will be enabled/disabled by the user?
			player.Controller.PrintToCenter($"Checkpoint {zone.Number} - Prespeed: {velocity.velMag():0} u/s");
		}
	}

	private static void EndTouchHandleBonusStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.LightRed}EndTouchFunc{ChatColors.Default} -> {ChatColors.Yellow}Bonus {zone.Number} Start Zone");
#endif
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();

		// Replay
		if (player.ReplayRecorder.IsRecording)
		{
			// Saving 2 seconds before leaving the start zone
			player.ReplayRecorder.Frames.RemoveRange(0, Math.Max(0, player.ReplayRecorder.Frames.Count - (Config.ReplaysPre * 2)));
		}

		// BONUS START ZONE
		if (!player.Timer.IsStageMode && player.Timer.IsBonusMode)
		{
			player.Timer.Start();
			// Set the CurrentRunData values
			player.Stats.ThisRun.RunTime = player.Timer.Ticks;

			player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.START_ZONE_EXIT;
			player.ReplayRecorder.BonusSituations.Add(player.ReplayRecorder.Frames.Count);
#if DEBUG
			Console.WriteLine($"START_ZONE_EXIT: player.ReplayRecorder.BonusSituations.Add({player.ReplayRecorder.Frames.Count})");
#endif
		}

		// Prespeed display
		player.Controller.PrintToCenter($"Prespeed: {velocity.velMag():0} u/s");
		player.Stats.ThisRun.StartVelX = velocity.X; // Start pre speed for the Bonus run
		player.Stats.ThisRun.StartVelY = velocity.Y; // Start pre speed for the Bonus run
		player.Stats.ThisRun.StartVelZ = velocity.Z; // Start pre speed for the Bonus run
	}

	private static void EndTouchHandleBonusEndZone(Player player, [CallerMemberName] string methodName = "")
	{
		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.END_ZONE_EXIT;
	}
}
