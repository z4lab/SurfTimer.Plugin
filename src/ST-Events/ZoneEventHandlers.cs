using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
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
		// Sync of the last stage / checkpoint segment, saved with it (the saves run a second later)
		float lastSegmentSync = player.SegmentSyncPercent;

		player.HUD.Notify("Map End");

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
			player.CountAttempt(0, 0, finished: true);
			bool saveMapTime = false;
			string PracticeString = "";
			if (player.Timer.IsPracticeMode)
				PracticeString = $"({ChatColors.Grey}Practice{ChatColors.Default}) ";

			if (player.Timer.Ticks < CurrentMap.WR[pStyle].RunTime) // Player beat the Map WR
			{
				saveMapTime = true;
				int timeImprove = CurrentMap.WR[pStyle].RunTime - player.Timer.Ticks;
				ChatAnnounce.Send(ChatAnnounce.Kind.Record, player, $"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["mapwr_improved",
					player.Controller.PlayerName, PlayerHud.FormatTime(player.Timer.Ticks), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(CurrentMap.WR[pStyle].RunTime)]}");
			}
			else if (CurrentMap.WR[pStyle].ID == -1) // No record was set on the map
			{
				saveMapTime = true;
				ChatAnnounce.Send(ChatAnnounce.Kind.Record, player, $"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["mapwr_set",
					player.Controller.PlayerName, PlayerHud.FormatTime(player.Timer.Ticks)]}");
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
				ChatAnnounce.Send(ChatAnnounce.Kind.Pb, player, $"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["mappb_improved",
					player.Controller.PlayerName, PlayerHud.FormatTime(player.Timer.Ticks), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(player.Stats.PB[pStyle].RunTime)]}");
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
			else
			{
				CurrentRun.LogRun(player, 0, 0, player.Timer.Ticks, player.SyncPercent); // Not a PB - history only
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

						// Before the save: still on the main thread (chat can't be printed after an await),
						// and compared against the previous PB rather than the one being saved
						player.HUD.DisplayStageMessage(CurrentMap.Stages, stage_run_time, velocity);

						await CurrentRun.SaveStageTime(player, CurrentMap.Stages, stage_run_time, true,
							startVelX: lastStageEntryVelX, startVelY: lastStageEntryVelY, startVelZ: lastStageEntryVelZ,
							endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z, sync: lastSegmentSync);
					});
				}
				// Should we also save the last checkpoint segment (last cp -> map end)? (non-staged maps only)
				// Only when this run actually passed the last cp - otherwise there's no segment start and the
				// whole run would be saved as the last segment
				else if (CurrentMap.CheckpointSegments > 0 && player.Stats.ThisRun.Checkpoints.ContainsKey(CurrentMap.TotalCheckpoints))
				{
					float lastCheckpointEntryVelX = player.Timer.CheckpointEntryVelX;
					float lastCheckpointEntryVelY = player.Timer.CheckpointEntryVelY;
					float lastCheckpointEntryVelZ = player.Timer.CheckpointEntryVelZ;
					short lastSegment = (short)CurrentMap.CheckpointSegments;
					ScheduleRunSave(player, "SaveCheckpointTime (last)", async () =>
					{
						// This calculation is wrong unless we wait for a bit in order for the `END_ZONE_ENTER` to be available in the `Frames` object
						int endEnter = player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.END_ZONE_ENTER);
						int lastCheckpointExit = player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.CHECKPOINT_ZONE_EXIT);
						if (endEnter < 0 || lastCheckpointExit < 0 || lastCheckpointExit >= endEnter)
						{
							_logger.LogWarning("[{ClassName}] Last checkpoint segment not saved for '{Name}' - no checkpoint exit before the end zone (exit {Exit}, end {End})",
								nameof(SurfTimer), player.Profile.Name, lastCheckpointExit, endEnter);
							return;
						}
						int checkpoint_run_time = endEnter - lastCheckpointExit;

						// Before the save: still on the main thread (chat can't be printed after an await),
						// and compared against the previous PB rather than the one being saved
						player.HUD.DisplayCheckpointSegmentMessage(lastSegment, checkpoint_run_time, velocity);

						await CurrentRun.SaveCheckpointTime(player, lastSegment, checkpoint_run_time, true,
							startVelX: lastCheckpointEntryVelX, startVelY: lastCheckpointEntryVelY, startVelZ: lastCheckpointEntryVelZ,
							endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z, sync: lastSegmentSync);
					});
				}
			}
		}
		// MAP END ZONE - Stage RUN
		else if (player.Timer.IsStageMode)
		{
			player.Timer.Stop();
			player.CountAttempt(2, (short)CurrentMap.Stages, finished: true);

			if (!player.Timer.IsPracticeMode)
			{
				float lastStageEntryVelX = player.Timer.StageEntryVelX;
				float lastStageEntryVelY = player.Timer.StageEntryVelY;
				float lastStageEntryVelZ = player.Timer.StageEntryVelZ;
				ScheduleRunSave(player, "SaveStageTime (last, stage mode)", async () =>
				{
					// This calculation is wrong unless we wait for a bit in order for the `END_ZONE_ENTER` to be available in the `Frames` object
					int stage_run_time = player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.END_ZONE_ENTER) - player.ReplayRecorder.Frames.FindLastIndex(f => f.Situation == ReplayFrameSituation.STAGE_ZONE_EXIT);

					// Before the save: still on the main thread (chat can't be printed after an await),
					// and compared against the previous PB rather than the one being saved
					player.HUD.DisplayStageMessage(CurrentMap.Stages, stage_run_time, velocity);

					await CurrentRun.SaveStageTime(player, CurrentMap.Stages, stage_run_time, true,
						startVelX: lastStageEntryVelX, startVelY: lastStageEntryVelY, startVelZ: lastStageEntryVelZ,
						endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z, sync: lastSegmentSync);
				});
			}
		}

		if (finishedStageForRepeat)
			ScheduleRepeatTeleport(player, (short)CurrentMap.Stages);

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.Lime}StartTouchFunc{ChatColors.Default} -> {ChatColors.Red}Map Stop Zone");
#endif
	}

	/// <summary>Stop zone: a running timer stops (the run is abandoned)</summary>
	private static void StartTouchHandleStopZone(Player player)
	{
		if (!player.Timer.IsRunning)
			return;

		player.Timer.Reset();
		player.Stats.ThisRun.Checkpoints.Clear();
		player.HUD.Notify("Timer stopped");
	}

	/// <summary>Teleport-back zone: back to the start of the stage / bonus the player is in, like !rs</summary>
	private void StartTouchHandleTeleportBackZone(Player player)
	{
		if (player.ReplayRecorder.IsSaving)
			return; // As !rs - the finished run is still being saved
		ResetToCurrentStart(player);
	}

	private static void StartTouchHandleMapStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
		// The timer stops right away. The recorder restarts only once a pending save has trimmed its replay
		// from the current frames (Player.TickPendingStartRecording) - e.g. the map teleported the player
		// here right after a checkpoint / stage time was saved.
		player.Timer.Reset();
		player.Stats.ThisRun.Checkpoints.Clear();
		player.HUD.Notify($"Map Start ({zone.Name})");
		player.BeginStartZoneRecording(ZoneType.MapStart);

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.Lime}StartTouchFunc{ChatColors.Default} -> {ChatColors.Green}Map Start Zone");
#endif
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
		// Sync of the stage just completed, saved with it
		float segmentSync = player.SegmentSyncPercent;

		// Reset/Stop the Stage timer
		// Save a Stage run when `IsStageMode` is active - (`stage - 1` to get the previous stage data)
		if (player.Timer.IsStageMode)
		{
			if (stage > 1 && !failed_stage && player.Timer.IsRunning)
				player.CountAttempt(2, (short)(stage - 1), finished: true);

			if (stage > 1 && !failed_stage && !player.Timer.IsPracticeMode)
			{
				int stage_run_time = player.Timer.Ticks;
				float entryVelX = player.Timer.StageEntryVelX;
				float entryVelY = player.Timer.StageEntryVelY;
				float entryVelZ = player.Timer.StageEntryVelZ;
				ScheduleRunSave(player, $"SaveStageTime (stage {stage - 1}, stage mode)", () =>
					CurrentRun.SaveStageTime(player, (short)(stage - 1), stage_run_time,
						startVelX: entryVelX, startVelY: entryVelY, startVelZ: entryVelZ,
						endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z, sync: segmentSync));

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
						endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z, sync: segmentSync));
			}

			player.Timer.Checkpoint = (short)(stage - 1); // Stage = Checkpoint when in a run on a Staged map

#if DEBUG
			Console.WriteLine($"============== Initial entity value: {zone.Number} | Assigned to `stage`: {stage} | player.Timer.Checkpoint: {stage - 1}");
			Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Stage start zones) -> player.Stats.PB[{player.Timer.Style}].Checkpoint.Count = {player.Stats.PB[player.Timer.Style].Checkpoints?.Count ?? 0}");
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
			Console.WriteLine($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc (Checkpoint zones) -> player.Stats.PB[{pStyle}].Checkpoint.Count = {player.Stats.PB[pStyle].Checkpoints?.Count ?? 0}");
#endif

			if (player.Timer.IsRunning && player.ReplayRecorder.IsRecording)
			{
				player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.CHECKPOINT_ZONE_ENTER;
				player.ReplayRecorder.CheckpointEnterSituations.Add(player.Timer.Ticks);
			}

			// Reaching cp N completes checkpoint segment N: from the previous cp (or the map start for
			// cp 1) to here. The last segment (last cp -> map end) is saved in the map end handler.
			// player.Stats.ThisRun.RunTime is the tick we left the previous checkpoint zone / the map start.
			int checkpoint_run_time = player.Timer.Ticks - player.Stats.ThisRun.RunTime;

			// Print Checkpoint completion message (non-staged maps compare against the standalone
			// Checkpoint PB/WR records; the rare staged-map-with-checkpoint-zones case falls back to
			// the generic per-run split message since no standalone Checkpoint records exist there).
			// Printed before the save so it compares against the previous PB.
			if (SurfTimer.CurrentMap.Stages == 0)
				player.HUD.DisplayCheckpointSegmentMessage(checkpoint, checkpoint_run_time, velocity);
			else
				player.HUD.DisplayCheckpointMessages();

			// Save Checkpoint segment MapTime during a Map run (non-staged maps only)
			if (SurfTimer.CurrentMap.Stages == 0 && !failed_checkpoint && !player.Timer.IsPracticeMode)
			{
				float entryVelX = player.Timer.CheckpointEntryVelX;
				float entryVelY = player.Timer.CheckpointEntryVelY;
				float entryVelZ = player.Timer.CheckpointEntryVelZ;

				float segmentSync = player.SegmentSyncPercent; // Sync of this segment, saved with it

				ScheduleRunSave(player, $"SaveCheckpointTime (checkpoint {checkpoint})", () =>
					CurrentRun.SaveCheckpointTime(player, checkpoint, checkpoint_run_time,
						startVelX: entryVelX, startVelY: entryVelY, startVelZ: entryVelZ,
						endVelX: velocity.X, endVelY: velocity.Y, endVelZ: velocity.Z, sync: segmentSync));
			}

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
		// Same as the map start: the timer stops now, the recorder restarts once a pending save is done
		short bonus = zone.Number;
		player.Timer.Bonus = bonus;

		player.Timer.Reset();
		player.Timer.IsBonusMode = true;
		player.BeginStartZoneRecording(ZoneType.BonusStart);

		player.HUD.Notify($"Bonus Start ({zone.Name})");

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
		player.CountAttempt(1, bonus_idx, finished: true);
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
			ChatAnnounce.Send(ChatAnnounce.Kind.Record, player, $"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["bonuswr_improved",
				player.Controller.PlayerName, bonus_idx, PlayerHud.FormatTime(player.Timer.Ticks), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(CurrentMap.BonusWR[bonus_idx][pStyle].RunTime)]}");
		}
		else if (CurrentMap.BonusWR[bonus_idx][pStyle].ID == -1) // No Bonus record was set on the map
		{
			saveBonusTime = true;
			ChatAnnounce.Send(ChatAnnounce.Kind.Record, player, $"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["bonuswr_set",
				player.Controller.PlayerName, bonus_idx, PlayerHud.FormatTime(player.Timer.Ticks)]}");
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
			ChatAnnounce.Send(ChatAnnounce.Kind.Pb, player, $"{Config.PluginPrefix} {PracticeString}{LocalizationService.LocalizerNonNull["bonuspb_improved",
				player.Controller.PlayerName, bonus_idx, PlayerHud.FormatTime(player.Timer.Ticks), PlayerHud.FormatTime(timeImprove), PlayerHud.FormatTime(player.Stats.BonusPB[bonus_idx][pStyle].RunTime)]}");
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
			else
			{
				CurrentRun.LogRun(player, 1, bonus_idx, player.Timer.Ticks, player.SyncPercent); // Not a PB - history only
			}
		}
	}


	/* EndTouch */
	private static void EndTouchHandleMapEndZone(Player player, [CallerMemberName] string methodName = "")
	{
		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.END_ZONE_EXIT;
	}

	/// <summary>
	/// The map's hard limit on leaving a run start (exit_speed_limit map setting) - before the exit
	/// velocity is used for the timer, prespeed and start velocities.
	/// </summary>
	private static void ApplyExitLimit(Player player, ref VectorT velocity)
	{
		if (CurrentMap?.ExitSpeedLimit is not float limit || !player.ClampExitSpeed(limit, ref velocity, out float before))
			return;

		player.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["prespeed_capped",
			before.ToString("0"), limit.ToString("0")]}");
	}

	private static void EndTouchHandleMapStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();

		// A map run starts here (or stage 1 in stage mode - its start is the map start) - the map's exit limit applies
		if (!player.Timer.IsBonusMode && (!player.Timer.IsStageMode || player.Timer.Stage <= 1))
			ApplyExitLimit(player, ref velocity);

		// MAP START ZONE
		if (!player.Timer.IsStageMode && !player.Timer.IsBonusMode)
		{
			player.Timer.Start();
			player.ResetSync();
			player.CountAttempt(0, 0, finished: false);
			player.Stats.ThisRun.RunTime = player.Timer.Ticks;
			player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.START_ZONE_EXIT;
			player.ReplayRecorder.MapSituations.Add(player.ReplayRecorder.Frames.Count);
#if DEBUG
			player.Controller.PrintToChat($"{ChatColors.Red}START_ZONE_EXIT: player.ReplayRecorder.MapSituations.Add({player.ReplayRecorder.Frames.Count})");
			Console.WriteLine($"START_ZONE_EXIT: player.ReplayRecorder.MapSituations.Add({player.ReplayRecorder.Frames.Count})");
#endif
		}

		// Prespeed display
		player.HUD.NotifyPrespeed(CurrentMap.Stages > 0 ? "Stage 1" : "", velocity);
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

		// Only where a stage run starts (stage mode) - passing a stage start during a map run is mid-run speed
		if (player.Timer.IsStageMode && player.Timer.Stage == stage)
			ApplyExitLimit(player, ref velocity);

		// Set replay situation
		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.STAGE_ZONE_EXIT;
		player.ReplayRecorder.StageExitSituations.Add(player.ReplayRecorder.Frames.Count);
		player.Stats.ThisRun.RunTime = player.Timer.Ticks;
		player.MarkSegmentStart(); // This stage's sync counts from here

		// Entry speed for the stage just entered - shown regardless of how the stage was entered
		// (normal run or !s practice)
		player.Timer.StageEntryVelX = velocity.X;
		player.Timer.StageEntryVelY = velocity.Y;
		player.Timer.StageEntryVelZ = velocity.Z;
		player.HUD.NotifyPrespeed($"Stage {stage}", velocity);

		// Start the Stage timer
		if (player.Timer.IsStageMode && player.Timer.Stage == stage)
		{
			player.Timer.Start();
			player.ResetSync();
			player.CountAttempt(2, stage, finished: false);
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

		// This will populate the End velocities for the given Checkpoint zone (Stage = Checkpoint when in a Map Run).
		// Looked up by key - checkpoint numbers don't have to be contiguous, so a Count comparison isn't safe.
		if (player.Timer.Checkpoint != 0 && player.Stats.ThisRun.Checkpoints.TryGetValue(player.Timer.Checkpoint, out var currentCheckpoint))
		{
#if DEBUG
			Console.WriteLine($"currentCheckpoint.EndVelX {currentCheckpoint.EndVelX} - velocity.X {velocity.X}");
			Console.WriteLine($"currentCheckpoint.EndVelY {currentCheckpoint.EndVelY} - velocity.Y {velocity.Y}");
			Console.WriteLine($"currentCheckpoint.EndVelZ {currentCheckpoint.EndVelZ} - velocity.Z {velocity.Z}");
#endif

			if (player.Timer.IsRunning && player.ReplayRecorder.IsRecording)
			{
				player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.CHECKPOINT_ZONE_EXIT;
				player.ReplayRecorder.CheckpointExitSituations.Add(player.Timer.Ticks);
			}

			// The next checkpoint segment starts here (mirrors the stage start exit) - without this the
			// segment times counted from the map start
			if (player.Timer.IsRunning && !player.Timer.IsStageMode && !player.Timer.IsBonusMode)
			{
				player.Stats.ThisRun.RunTime = player.Timer.Ticks;
				player.MarkSegmentStart(); // This segment's sync counts from here
			}

			// Update the Checkpoint object values
			currentCheckpoint.EndVelX = velocity.X;
			currentCheckpoint.EndVelY = velocity.Y;
			currentCheckpoint.EndVelZ = velocity.Z;
			currentCheckpoint.EndTouch = player.Timer.Ticks;

			// Show Prespeed for stages - will be enabled/disabled by the user?
			player.HUD.NotifyPrespeed($"Checkpoint {zone.Number}", velocity);
		}
	}

	private static void EndTouchHandleBonusStartZone(Player player, ZoneInfo zone, [CallerMemberName] string methodName = "")
	{
#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_{ChatColors.LightRed}EndTouchFunc{ChatColors.Default} -> {ChatColors.Yellow}Bonus {zone.Number} Start Zone");
#endif
		// Only leaving the start of the bonus the player is on starts it. Leaving another bonus start -
		// e.g. teleported out of b1 by !b 2 (its EndTouch can come after b2's StartTouch reset the timer) -
		// or passing one during a map / stage run must not start the timer, cut the replay or overwrite
		// the run's start speed.
		if (player.Timer.IsStageMode || !player.Timer.IsBonusMode || zone.Number != player.Timer.Bonus)
			return;

		VectorT velocity = player.Controller.PlayerPawn.Value!.AbsVelocity.ToVector_t();

		// A bonus run starts here - the map's exit limit applies
		ApplyExitLimit(player, ref velocity);

		// Replay
		if (player.ReplayRecorder.IsRecording)
		{
			// Saving 2 seconds before leaving the start zone
			player.ReplayRecorder.Frames.RemoveRange(0, Math.Max(0, player.ReplayRecorder.Frames.Count - (Config.ReplaysPre * 2)));
		}

		// BONUS START ZONE
		player.Timer.Start();
		player.ResetSync();
		player.CountAttempt(1, zone.Number, finished: false);
		// Set the CurrentRunData values
		player.Stats.ThisRun.RunTime = player.Timer.Ticks;

		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.START_ZONE_EXIT;
		player.ReplayRecorder.BonusSituations.Add(player.ReplayRecorder.Frames.Count);
#if DEBUG
		Console.WriteLine($"START_ZONE_EXIT: player.ReplayRecorder.BonusSituations.Add({player.ReplayRecorder.Frames.Count})");
#endif

		// Prespeed display
		player.HUD.NotifyPrespeed("", velocity);
		player.Stats.ThisRun.StartVelX = velocity.X; // Start pre speed for the Bonus run
		player.Stats.ThisRun.StartVelY = velocity.Y; // Start pre speed for the Bonus run
		player.Stats.ThisRun.StartVelZ = velocity.Z; // Start pre speed for the Bonus run
	}

	private static void EndTouchHandleBonusEndZone(Player player, [CallerMemberName] string methodName = "")
	{
		player.ReplayRecorder.CurrentSituation = ReplayFrameSituation.END_ZONE_EXIT;
	}
}
