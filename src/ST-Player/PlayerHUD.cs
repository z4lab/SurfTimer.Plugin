using CounterStrikeSharp.API.Modules.Utils;
using SurfTimer.Shared.Entities;

namespace SurfTimer;

public class PlayerHud
{
	private readonly Player _player;
	private readonly string TimerColor = "#4FC3F7";
	private readonly string TimerColorPractice = "#BA68C8";
	private readonly string TimerColorActive = "#43A047";
	private readonly string RankColorPb = "#7986CB";
	private readonly string RankColorWr = "#FFD700";
	private readonly string SpectatorColor = "#9E9E9E";

	internal PlayerHud(Player Player)
	{
		_player = Player;
	}

	private static string FormatHUDElementHTML(
		string title,
		string body,
		string color,
		string size = "m"
	)
	{
		if (title != "")
		{
			if (size == "m")
				return $"{title}: <font color='{color}'>{body}</font>";
			else
				return $"<font class='fontSize-{size.ToLower()}'>{title}: <font color='{color}'>{body}</font></font>";
		}
		else
		{
			if (size == "m")
				return $"<font color='{color}'>{body}</font>";
			else
				return $"<font class='fontSize-{size.ToLower()}' color='{color}'>{body}</font>";
		}
	}

	/// <summary>
	/// Formats the given time in ticks into a readable time string.
	/// Unless specified differently, the default formatting will be `Compact`.
	/// Check <see cref="PlayerTimer.TimeFormatStyle"/> for all formatting types.
	/// </summary>
	public static string FormatTime(
		int ticks,
		PlayerTimer.TimeFormatStyle style = PlayerTimer.TimeFormatStyle.Compact
	)
	{
		TimeSpan time = TimeSpan.FromSeconds(ticks / 64.0);
		int millis = (int)(ticks % 64 * (1000.0 / 64.0));

		switch (style)
		{
			case PlayerTimer.TimeFormatStyle.Compact:
				return time.TotalMinutes < 1
					? $"{time.Seconds:D2}.{millis:D3}"
					: $"{time.Minutes:D1}:{time.Seconds:D2}.{millis:D3}";
			case PlayerTimer.TimeFormatStyle.Full:
				return time.TotalHours < 1
					? $"{time.Minutes:D2}:{time.Seconds:D2}.{millis:D3}"
					: $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{millis:D3}";
			case PlayerTimer.TimeFormatStyle.Verbose:
				return $"{time.Hours}h {time.Minutes}m {time.Seconds}s {millis}ms";
			default:
				throw new ArgumentException("Invalid time format style");
		}
	}

	/// <summary>
	/// Build the timer module with appropriate prefix based on mode
	/// </summary>
	/// <returns>string timerModule</returns>
	internal string BuildTimerWithPrefix()
	{
		// Timer Module
		string timerColor = TimerColor;

		if (_player.Timer.IsRunning)
		{
			if (_player.Timer.IsPracticeMode)
				timerColor = TimerColorPractice;
			else
				timerColor = TimerColorActive;
		}

		string prefix = "";

		if (_player.Timer.IsPracticeMode)
			prefix += "[P] ";

		if (_player.Timer.IsBonusMode)
			prefix += $"[B{_player.Timer.Bonus}] ";
		else if (_player.Timer.IsStageMode)
			prefix += $"[S{_player.Timer.Stage}] ";

		string timerModule = FormatHUDElementHTML(
			"",
			prefix + FormatTime(_player.Timer.Ticks),
			timerColor
		);

		return timerModule;
	}

	/// <summary>
	/// Build the velocity module
	/// </summary>
	/// <returns>string velocityModule</returns>
	internal string BuildVelocityModule()
	{
		float velocity = Extensions.GetVelocityFromController(_player.Controller);
		string velocityModule =
			FormatHUDElementHTML(
				"Speed",
				velocity.ToString("0"),
				Extensions.GetSpeedColorGradient(velocity)
			) + " u/s";
		return velocityModule;
	}

	/// <summary>
	/// Build the rank module with appropriate values based on mode
	/// </summary>
	/// <returns>string rankModule</returns>
	internal string BuildRankModule()
	{
		int style = _player.Timer.Style;

		// Rank Module
		string rankModule = FormatHUDElementHTML("Rank", $"N/A", RankColorPb);
		if (_player.Timer.IsBonusMode)
		{
			if (
				_player.Stats.BonusPB[_player.Timer.Bonus][style].ID != -1
				&& SurfTimer.CurrentMap.BonusWR[_player.Timer.Bonus][style].ID != -1
			)
				rankModule = FormatHUDElementHTML(
					"Rank",
					$"{_player.Stats.BonusPB[_player.Timer.Bonus][style].Rank}/{SurfTimer.CurrentMap.BonusCompletions[_player.Timer.Bonus][style]}",
					RankColorPb
				);
			else if (SurfTimer.CurrentMap.BonusWR[_player.Timer.Bonus][style].ID != -1)
				rankModule = FormatHUDElementHTML(
					"Rank",
					$"-/{SurfTimer.CurrentMap.BonusCompletions[_player.Timer.Bonus][style]}",
					RankColorPb
				);
		}
		else if (_player.Timer.IsStageMode)
		{
			if (
				_player.Stats.StagePB[_player.Timer.Stage][style].ID != -1
				&& SurfTimer.CurrentMap.StageWR[_player.Timer.Stage][style].ID != -1
			)
				rankModule = FormatHUDElementHTML(
					"Rank",
					$"{_player.Stats.StagePB[_player.Timer.Stage][style].Rank}/{SurfTimer.CurrentMap.StageCompletions[_player.Timer.Stage][style]}",
					RankColorPb
				);
			else if (SurfTimer.CurrentMap.StageWR[_player.Timer.Stage][style].ID != -1)
				rankModule = FormatHUDElementHTML(
					"Rank",
					$"-/{SurfTimer.CurrentMap.StageCompletions[_player.Timer.Stage][style]}",
					RankColorPb
				);
		}
		else
		{
			if (_player.Stats.PB[style].ID != -1 && SurfTimer.CurrentMap.WR[style].ID != -1)
				rankModule = FormatHUDElementHTML(
					"Rank",
					$"{_player.Stats.PB[style].Rank}/{SurfTimer.CurrentMap.MapCompletions[style]}",
					RankColorPb
				);
			else if (SurfTimer.CurrentMap.WR[style].ID != -1)
				rankModule = FormatHUDElementHTML(
					"Rank",
					$"-/{SurfTimer.CurrentMap.MapCompletions[style]}",
					RankColorPb
				);
		}

		return rankModule;
	}

	/// <summary>
	/// Build the PB module with appropriate values based on mode
	/// </summary>
	/// <returns>string pbModule</returns>
	internal string BuildPbModule()
	{
		int style = _player.Timer.Style;

		// PB & WR Modules
		string pbModule = FormatHUDElementHTML(
			"PB",
			_player.Stats.PB[style].RunTime > 0
				? FormatTime(_player.Stats.PB[style].RunTime)
				: "N/A",
			RankColorPb
		);

		if (_player.Timer.Bonus > 0 && _player.Timer.IsBonusMode) // Show corresponding bonus values
		{
			pbModule = FormatHUDElementHTML(
				"PB",
				_player.Stats.BonusPB[_player.Timer.Bonus][style].RunTime > 0
					? FormatTime(_player.Stats.BonusPB[_player.Timer.Bonus][style].RunTime)
					: "N/A",
				RankColorPb
			);
		}
		else if (_player.Timer.IsStageMode) // Show corresponding stage values
		{
			pbModule = FormatHUDElementHTML(
				"PB",
				_player.Stats.StagePB[_player.Timer.Stage][style].RunTime > 0
					? FormatTime(_player.Stats.StagePB[_player.Timer.Stage][style].RunTime)
					: "N/A",
				RankColorPb
			);
		}

		return pbModule;
	}

	/// <summary>
	/// Build the WR module with appropriate values based on mode
	/// </summary>
	/// <returns>string wrModule</returns>
	internal string BuildWrModule()
	{
		int style = _player.Timer.Style;

		// WR Module
		string wrModule = FormatHUDElementHTML(
			"WR",
			SurfTimer.CurrentMap.WR[style].RunTime > 0
				? FormatTime(SurfTimer.CurrentMap.WR[style].RunTime)
				: "N/A",
			RankColorWr
		);

		if (_player.Timer.Bonus > 0 && _player.Timer.IsBonusMode) // Show corresponding bonus values
		{
			wrModule = FormatHUDElementHTML(
				"WR",
				SurfTimer.CurrentMap.BonusWR[_player.Timer.Bonus][style].RunTime > 0
					? FormatTime(SurfTimer.CurrentMap.BonusWR[_player.Timer.Bonus][style].RunTime)
					: "N/A",
				RankColorWr
			);
		}
		else if (_player.Timer.IsStageMode) // Show corresponding stage values
		{
			wrModule = FormatHUDElementHTML(
				"WR",
				SurfTimer.CurrentMap.StageWR[_player.Timer.Stage][style].RunTime > 0
					? FormatTime(SurfTimer.CurrentMap.StageWR[_player.Timer.Stage][style].RunTime)
					: "N/A",
				RankColorWr
			);
		}

		return wrModule;
	}

	/// <summary>
	/// Displays the Center HUD for the client
	/// </summary>
	internal void Display()
	{
		if (!_player.Controller.IsValid)
			return;

		if (_player.Controller.PawnIsAlive)
		{
			string timerModule = BuildTimerWithPrefix();

			// Velocity Module
			string velocityModule = BuildVelocityModule();

			// Rank Module
			string rankModule = BuildRankModule();

			// PB & WR Modules
			string pbModule = BuildPbModule();
			string wrModule = BuildWrModule();

			// Build HUD
			string hud =
				$"{timerModule}<br>{velocityModule}<br>{pbModule} | {rankModule}<br>{wrModule}";

			// Display HUD
			_player.Controller.PrintToCenterHtml(hud);
		}
		else if (_player.Controller.Team == CsTeam.Spectator)
		{
			DisplaySpectatorHud();
		}
	}

	/// <summary>
	/// Displays the Spectator HUD for the client if they are spectating a replay bot from the pool
	/// </summary>
	internal void DisplaySpectatorHud()
	{
		ReplayPlayer? specReplay = SurfTimer.CurrentMap.ReplayManager.Pool.Find(x =>
			x.Controller != null && _player.IsSpectating(x.Controller)
		);

		if (specReplay == null)
			return;

		string hud = BuildReplayModule(specReplay);
		if (!string.IsNullOrEmpty(hud))
		{
			_player.Controller.PrintToCenterHtml(hud);
		}
	}

	/// <summary>
	/// Build the spectator HUD module for whichever replay a pool slot is currently playing -
	/// covers Map/Stage/Bonus/Checkpoint content, both WR and a specific player's PB.
	/// </summary>
	/// <param name="specReplay">Pool slot to use</param>
	internal string BuildReplayModule(ReplayPlayer specReplay)
	{
		string kind = specReplay.RequestedByPlayerId == -1 ? "WR" : "PB";
		string replayType = specReplay.Type switch
		{
			0 => $"Map {kind} Replay",
			1 => $"Bonus {specReplay.Stage} {kind} Replay",
			2 => $"Stage {specReplay.Stage} {kind} Replay",
			3 => $"Checkpoint {specReplay.Stage} {kind} Replay",
			_ => "",
		};
		if (replayType == "")
			return ""; // Invalid type

		float velocity = Extensions.GetVelocityFromController(specReplay.Controller!);
		string timerColor = specReplay.ReplayCurrentRunTime > 0 ? TimerColorActive : RankColorWr;

		string replayModule = FormatHUDElementHTML("", replayType, SpectatorColor, "m");
		string nameModule = FormatHUDElementHTML("", $"{specReplay.RecordPlayerName}", RankColorWr);
		string timeModule = FormatHUDElementHTML(
			"",
			$"{FormatTime(specReplay.ReplayCurrentRunTime)} / {FormatTime(specReplay.RecordRunTime)}",
			timerColor
		);
		string velocityModule =
			FormatHUDElementHTML(
				"Speed",
				velocity.ToString("0"),
				Extensions.GetSpeedColorGradient(velocity)
			) + " u/s";
		string cycleModule = FormatHUDElementHTML(
			"Cycle",
			$"{specReplay.RepeatCount}",
			SpectatorColor,
			"s"
		);

		return $"{replayModule}<br>{nameModule}<br>{timeModule}<br>{velocityModule}<br>{cycleModule}";
	}

	/// <summary>
	/// Displays checkpoints comparison messages in player chat.
	/// Only calculates if the player has a PB, otherwise it will display N/A
	/// </summary>
	internal void DisplayCheckpointMessages()
	{
		int pbTime;
		int wrTime = -1;
		float pbSpeed;
		float wrSpeed = -1.0f;
		int style = _player.Timer.Style;
		int playerCurrentCheckpoint = _player.Timer.Checkpoint;
		int currentTime = _player.Timer.Ticks;
		float currentSpeed = Extensions.GetVelocityFromController(_player.Controller!);

		// Default values for the PB and WR differences in case no calculations can be made
		string strPbDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";
		string strWrDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";

		// Get PB checkpoint data if available
		CheckpointEntity? pbCheckpoint = null;
		_player.Stats.PB[style].Checkpoints?.TryGetValue(playerCurrentCheckpoint, out pbCheckpoint);
		if (pbCheckpoint != null)
		{
			pbTime = pbCheckpoint.RunTime;
			pbSpeed = (float)
				Math.Sqrt(
					pbCheckpoint.StartVelX * pbCheckpoint.StartVelX
						+ pbCheckpoint.StartVelY * pbCheckpoint.StartVelY
						+ pbCheckpoint.StartVelZ * pbCheckpoint.StartVelZ
				);
		}
		else
		{
			// We assign default values to pbTime and pbSpeed
			pbTime = -1; // This determines if we will calculate differences or not!!!
			pbSpeed = 0.0f;
		}

		// Calculate differences in PB (PB - Current)
		if (pbTime != -1)
		{
#if DEBUG
			Console.WriteLine(
				$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> Starting PB difference calculation... (pbTime != -1)"
			);
#endif
			// Reset the string
			strPbDifference = string.Empty;

			// Calculate the time difference
			if (pbTime - currentTime < 0.0)
			{
				strPbDifference += ChatColors.Red + "+" + FormatTime((pbTime - currentTime) * -1); // We multiply by -1 to get the positive value
			}
			else if (pbTime - currentTime >= 0.0)
			{
				strPbDifference += ChatColors.Green + "-" + FormatTime(pbTime - currentTime);
			}
			strPbDifference += ChatColors.Default + " ";

			// Calculate the speed difference
			if (pbSpeed - currentSpeed <= 0.0)
			{
				strPbDifference +=
					"(" + ChatColors.Green + "+" + ((pbSpeed - currentSpeed) * -1).ToString("0"); // We multiply by -1 to get the positive value
			}
			else if (pbSpeed - currentSpeed > 0.0)
			{
				strPbDifference +=
					"(" + ChatColors.Red + "-" + (pbSpeed - currentSpeed).ToString("0");
			}
			strPbDifference += ChatColors.Default + ")";
		}

		if (SurfTimer.CurrentMap.WR[style].RunTime > 0)
		{
			// Calculate differences in WR (WR - Current)
#if DEBUG
			Console.WriteLine(
				$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> Starting WR difference calculation... (SurfTimer.CurrentMap.WR[{style}].Ticks > 0)"
			);
#endif

			CheckpointEntity? wrCheckpoint = null;
			SurfTimer.CurrentMap.WR[style].Checkpoints?.TryGetValue(playerCurrentCheckpoint, out wrCheckpoint);
			if (wrCheckpoint != null)
			{
				wrTime = wrCheckpoint.RunTime;
				wrSpeed = (float)
					Math.Sqrt(
						wrCheckpoint.StartVelX * wrCheckpoint.StartVelX
							+ wrCheckpoint.StartVelY * wrCheckpoint.StartVelY
							+ wrCheckpoint.StartVelZ * wrCheckpoint.StartVelZ
					);
				// Reset the string
				strWrDifference = string.Empty;

				// Calculate the WR time difference
				if (wrTime - currentTime < 0.0)
				{
					strWrDifference += ChatColors.Red + "+" + FormatTime((wrTime - currentTime) * -1); // We multiply by -1 to get the positive value
				}
				else if (wrTime - currentTime >= 0.0)
				{
					strWrDifference += ChatColors.Green + "-" + FormatTime(wrTime - currentTime);
				}
				strWrDifference += ChatColors.Default + " ";

				// Calculate the WR speed difference
				if (wrSpeed - currentSpeed <= 0.0)
				{
					strWrDifference +=
						"(" + ChatColors.Green + "+" + ((wrSpeed - currentSpeed) * -1).ToString("0"); // We multiply by -1 to get the positive value
				}
				else if (wrSpeed - currentSpeed > 0.0)
				{
					strWrDifference +=
						"(" + ChatColors.Red + "-" + (wrSpeed - currentSpeed).ToString("0");
				}
				strWrDifference += ChatColors.Default + ")";
			}
		}

		// Print checkpoint message
		_player.Controller.PrintToChat(
			$"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpoint_message",
			playerCurrentCheckpoint, FormatTime(_player.Timer.Ticks), currentSpeed.ToString("0"), strPbDifference, strWrDifference]}"
		);

#if DEBUG
		Console.WriteLine(
			$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> [TIME]  PB: {pbTime} - CURR: {currentTime} = pbTime: {pbTime - currentTime}"
		);
		Console.WriteLine(
			$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> [SPEED] PB: {pbSpeed} - CURR: {currentSpeed} = difference: {pbSpeed - currentSpeed}"
		);
		Console.WriteLine(
			$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> [TIME]  WR: {wrTime} - CURR: {currentTime} = difference: {wrTime - currentTime}"
		);
		Console.WriteLine(
			$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> [SPEED] WR: {wrSpeed} - CURR: {currentSpeed} = difference: {wrSpeed - currentSpeed}"
		);
#endif
	}

	/// <summary>
	/// Displays a Stage completion comparison message in player chat (staged maps only).
	/// Compares against the standalone Stage PB/WR records, not the generic per-run Checkpoint splits.
	/// Only calculates if a record exists yet, otherwise it will display N/A.
	/// </summary>
	/// <param name="stage">Stage that was just completed</param>
	/// <param name="stageRunTime">Ticks it took to complete the stage</param>
	/// <param name="exitVelocity">Player's velocity at the moment the stage was completed</param>
	internal void DisplayStageMessage(short stage, int stageRunTime, VectorT exitVelocity)
	{
		int style = _player.Timer.Style;
		float exitSpeed = exitVelocity.velMag();

		string strPbDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";
		string strWrDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";

		PersonalBest stagePb = _player.Stats.StagePB[stage][style];
		if (stagePb.ID != -1)
		{
			int pbTime = stagePb.RunTime;
			float pbSpeed = (float)
				Math.Sqrt(stagePb.EndVelX * stagePb.EndVelX + stagePb.EndVelY * stagePb.EndVelY + stagePb.EndVelZ * stagePb.EndVelZ);

			strPbDifference = string.Empty;
			if (pbTime - stageRunTime < 0.0)
				strPbDifference += ChatColors.Red + "+" + FormatTime((pbTime - stageRunTime) * -1);
			else
				strPbDifference += ChatColors.Green + "-" + FormatTime(pbTime - stageRunTime);
			strPbDifference += ChatColors.Default + " ";

			if (pbSpeed - exitSpeed <= 0.0)
				strPbDifference += "(" + ChatColors.Green + "+" + ((pbSpeed - exitSpeed) * -1).ToString("0");
			else
				strPbDifference += "(" + ChatColors.Red + "-" + (pbSpeed - exitSpeed).ToString("0");
			strPbDifference += ChatColors.Default + ")";
		}

		PersonalBest stageWr = SurfTimer.CurrentMap.StageWR[stage][style];
		if (stageWr.ID != -1)
		{
			int wrTime = stageWr.RunTime;
			float wrSpeed = (float)
				Math.Sqrt(stageWr.EndVelX * stageWr.EndVelX + stageWr.EndVelY * stageWr.EndVelY + stageWr.EndVelZ * stageWr.EndVelZ);

			strWrDifference = string.Empty;
			if (wrTime - stageRunTime < 0.0)
				strWrDifference += ChatColors.Red + "+" + FormatTime((wrTime - stageRunTime) * -1);
			else
				strWrDifference += ChatColors.Green + "-" + FormatTime(wrTime - stageRunTime);
			strWrDifference += ChatColors.Default + " ";

			if (wrSpeed - exitSpeed <= 0.0)
				strWrDifference += "(" + ChatColors.Green + "+" + ((wrSpeed - exitSpeed) * -1).ToString("0");
			else
				strWrDifference += "(" + ChatColors.Red + "-" + (wrSpeed - exitSpeed).ToString("0");
			strWrDifference += ChatColors.Default + ")";
		}

		_player.Controller.PrintToChat(
			$"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stage_message",
			stage, FormatTime(stageRunTime), exitSpeed.ToString("0"), strPbDifference, strWrDifference]}"
		);
	}

	/// <summary>
	/// Displays a Checkpoint segment completion comparison message in player chat (non-staged maps
	/// only). Compares against the standalone Checkpoint PB/WR records, not the generic per-run
	/// embedded splits. Only calculates if a record exists yet, otherwise it will display N/A.
	/// </summary>
	/// <param name="checkpoint">Checkpoint segment that was just completed</param>
	/// <param name="checkpointRunTime">Ticks it took to complete the checkpoint segment</param>
	/// <param name="exitVelocity">Player's velocity at the moment the checkpoint segment was completed</param>
	internal void DisplayCheckpointSegmentMessage(short checkpoint, int checkpointRunTime, VectorT exitVelocity)
	{
		int style = _player.Timer.Style;
		float exitSpeed = exitVelocity.velMag();

		string strPbDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";
		string strWrDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";

		PersonalBest checkpointPb = _player.Stats.CheckpointPB[checkpoint][style];
		if (checkpointPb.ID != -1)
		{
			int pbTime = checkpointPb.RunTime;
			float pbSpeed = (float)
				Math.Sqrt(checkpointPb.EndVelX * checkpointPb.EndVelX + checkpointPb.EndVelY * checkpointPb.EndVelY + checkpointPb.EndVelZ * checkpointPb.EndVelZ);

			strPbDifference = string.Empty;
			if (pbTime - checkpointRunTime < 0.0)
				strPbDifference += ChatColors.Red + "+" + FormatTime((pbTime - checkpointRunTime) * -1);
			else
				strPbDifference += ChatColors.Green + "-" + FormatTime(pbTime - checkpointRunTime);
			strPbDifference += ChatColors.Default + " ";

			if (pbSpeed - exitSpeed <= 0.0)
				strPbDifference += "(" + ChatColors.Green + "+" + ((pbSpeed - exitSpeed) * -1).ToString("0");
			else
				strPbDifference += "(" + ChatColors.Red + "-" + (pbSpeed - exitSpeed).ToString("0");
			strPbDifference += ChatColors.Default + ")";
		}

		PersonalBest checkpointWr = SurfTimer.CurrentMap.CheckpointWR[checkpoint][style];
		if (checkpointWr.ID != -1)
		{
			int wrTime = checkpointWr.RunTime;
			float wrSpeed = (float)
				Math.Sqrt(checkpointWr.EndVelX * checkpointWr.EndVelX + checkpointWr.EndVelY * checkpointWr.EndVelY + checkpointWr.EndVelZ * checkpointWr.EndVelZ);

			strWrDifference = string.Empty;
			if (wrTime - checkpointRunTime < 0.0)
				strWrDifference += ChatColors.Red + "+" + FormatTime((wrTime - checkpointRunTime) * -1);
			else
				strWrDifference += ChatColors.Green + "-" + FormatTime(wrTime - checkpointRunTime);
			strWrDifference += ChatColors.Default + " ";

			if (wrSpeed - exitSpeed <= 0.0)
				strWrDifference += "(" + ChatColors.Green + "+" + ((wrSpeed - exitSpeed) * -1).ToString("0");
			else
				strWrDifference += "(" + ChatColors.Red + "-" + (wrSpeed - exitSpeed).ToString("0");
			strWrDifference += ChatColors.Default + ")";
		}

		_player.Controller.PrintToChat(
			$"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpoint_message",
			checkpoint, FormatTime(checkpointRunTime), exitSpeed.ToString("0"), strPbDifference, strWrDifference]}"
		);
	}
}
