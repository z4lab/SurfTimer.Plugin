using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	[ConsoleCommand("css_r", "Reset back to the start of the map.")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayerReset(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
		{
			Server.NextFrame(() =>  // Weird CS2 bug that requires doing this twice to show the Joined X team in chat and not stay in limbo
				{
					player.ChangeTeam(CsTeam.CounterTerrorist);
					player.Respawn();

					player.ChangeTeam(CsTeam.Spectator);

					player.ChangeTeam(CsTeam.CounterTerrorist);
					player.Respawn();
				}
			);
		}

		Player oPlayer = playerList[player.UserId ?? 0];
		if (oPlayer.ReplayRecorder.IsSaving)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["reset_delay"]}");
			return;
		}

		oPlayer.Timer.Reset();
		oPlayer.Stats.ThisRun.Checkpoints.Clear();
		if (!CurrentMap.StartZone.IsZero())
			Server.NextFrame(() =>
			{
				Extensions.Teleport(player.PlayerPawn.Value!, CurrentMap.StartZone, null, new VectorT(0, 0, 0));
			}
		);
	}

	[ConsoleCommand("css_rs", "Reset back to the start of the stage or bonus you were in.")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayerResetStage(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
		{
			Server.NextFrame(() =>  // Weird CS2 bug that requires doing this twice to show the Joined X team in chat and not stay in limbo
				{
					player.ChangeTeam(CsTeam.CounterTerrorist);
					player.Respawn();

					player.ChangeTeam(CsTeam.Spectator);

					player.ChangeTeam(CsTeam.CounterTerrorist);
					player.Respawn();
				}
			);
		}

		Player oPlayer = playerList[player.UserId ?? 0];
		if (oPlayer.ReplayRecorder.IsSaving)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["reset_delay"]}");
			return;
		}


		if (oPlayer.Timer.IsBonusMode)
		{
			if (oPlayer.Timer.Bonus != 0 && !CurrentMap.BonusStartZone[oPlayer.Timer.Bonus].IsZero())
				Server.NextFrame(() => Extensions.Teleport(player.PlayerPawn.Value!, CurrentMap.BonusStartZone[oPlayer.Timer.Bonus], null, new VectorT(0, 0, 0)));
			else // Reset back to map start
				Server.NextFrame(() => Extensions.Teleport(player.PlayerPawn.Value!, CurrentMap.StartZone, null, new VectorT(0, 0, 0)));
		}
		else
		{
			if (oPlayer.Timer.Stage != 0 && !CurrentMap.StageStartZone[oPlayer.Timer.Stage].IsZero())
				Server.NextFrame(() => Extensions.Teleport(player.PlayerPawn.Value!, CurrentMap.StageStartZone[oPlayer.Timer.Stage], null, new VectorT(0, 0, 0)));
			else // Reset back to map start
				Server.NextFrame(() => Extensions.Teleport(player.PlayerPawn.Value!, CurrentMap.StartZone, null, new VectorT(0, 0, 0)));
		}
	}

	[ConsoleCommand("css_s", "Teleport to a stage")]
	[ConsoleCommand("css_stage", "Teleport to a stage")]
	[CommandHelper(minArgs: 1, usage: "<Stage Number> [1/2/3]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayerGoToStage(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		short stage;
		try
		{
			stage = short.Parse(command.ArgByIndex(1));
		}
		catch (System.Exception)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage",
				"!s <stage>"]}"
			);
			return;
		}

		if (CurrentMap.Stages <= 0)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["not_staged"]}");
			return;
		}
		else if (stage > CurrentMap.Stages)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_stage_value",
				CurrentMap.Stages]}"
			);
			return;
		}

		if (!TeleportToStage(player, stage))
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage",
				"!s <stage>"]}"
			);
	}

	/// <summary>
	/// Resets the timer and teleports the player to a stage start in stage mode (stage 1 is the map
	/// start, which runs as a normal map run). Timer.Stage is set before the teleport so entering the
	/// zone isn't treated as finishing the previous stage. Returns false if the zone doesn't exist.
	/// </summary>
	private bool TeleportToStage(CCSPlayerController player, short stage)
	{
		bool zoneExists = stage == 1 ? !CurrentMap.StartZone.IsZero() : !CurrentMap.StageStartZone[stage].IsZero();
		if (!zoneExists)
			return false;

		playerList[player.UserId ?? 0].Timer.Reset();

		if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
		{
			Server.NextFrame(() =>  // Weird CS2 bug that requires doing this twice to show the Joined X team in chat and not stay in limbo
				{
					player.ChangeTeam(CsTeam.CounterTerrorist);
					player.Respawn();

					player.ChangeTeam(CsTeam.Spectator);

					player.ChangeTeam(CsTeam.CounterTerrorist);
					player.Respawn();
				}
			);
		}

		if (stage == 1)
		{
			Server.NextFrame(() => Extensions.Teleport(player.PlayerPawn.Value!, CurrentMap.StartZone, null, new VectorT(0, 0, 0)));
		}
		else
		{
			playerList[player.UserId ?? 0].Timer.Stage = stage;
			Server.NextFrame(() => Extensions.Teleport(player.PlayerPawn.Value!, CurrentMap.StageStartZone[stage], null, new VectorT(0, 0, 0)));
			playerList[player.UserId ?? 0].Timer.IsStageMode = true;
		}

		// To-do: If you run this while you're in the start zone, endtouch for the start zone runs after you've teleported
		//        causing the timer to start. This needs to be fixed.
		return true;
	}

	[ConsoleCommand("css_repeat", "Toggle repeat mode - sends you back to the start of each stage you finish")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayerToggleRepeat(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var oPlayer))
			return;

		if (oPlayer.IsRepeatMode)
		{
			oPlayer.IsRepeatMode = false;
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["repeat_disabled"]}");
			return;
		}

		if (CurrentMap.Stages <= 0)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["not_staged"]}");
			return;
		}

		oPlayer.IsRepeatMode = true;
		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["repeat_enabled"]}");

		// Switch into stage mode on the stage they're currently on (stage 1 = map start)
		if (!oPlayer.Timer.IsStageMode)
			TeleportToStage(player, oPlayer.Timer.Stage > 0 ? oPlayer.Timer.Stage : (short)1);
	}

	/// <summary>
	/// Sends a repeat-mode player back to the start of the stage they just finished, once any pending
	/// save is done - the save trims the replay up to the LAST stage-enter marker, so teleporting back
	/// before it runs would add a new marker and cut the replay wrong.
	/// </summary>
	private void ScheduleRepeatTeleport(Player player, short stage, int attemptsLeft = 100)
	{
		AddTimer(0.1f, () =>
		{
			if (!player.IsRepeatMode || !player.Controller.IsValid || !player.Controller.PawnIsAlive)
				return;

			if (player.ReplayRecorder.IsSaving)
			{
				if (attemptsLeft > 0)
					ScheduleRepeatTeleport(player, stage, attemptsLeft - 1);
				return;
			}

			TeleportToStage(player.Controller, stage);
		});
	}

	[ConsoleCommand("css_b", "Teleport to a bonus")]
	[ConsoleCommand("css_bonus", "Teleport to a bonus")]
	[CommandHelper(minArgs: 1, usage: "<Bonus Number> [1/2/3]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayerGoToBonus(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		int bonus;

		try
		{
			bonus = Int32.Parse(command.ArgByIndex(1));
		}
		catch (System.Exception)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage",
				"!b <bonus>"]}"
			);
			return;
		}

		if (CurrentMap.Bonuses <= 0)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["not_bonused"]}");
			return;
		}
		else if (bonus > CurrentMap.Bonuses)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_bonus_value",
				CurrentMap.Bonuses]}"
			);
			return;
		}

		if (!CurrentMap.BonusStartZone[bonus].IsZero())
		{
			playerList[player.UserId ?? 0].Timer.Reset();
			playerList[player.UserId ?? 0].Timer.IsBonusMode = true;

			if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
			{
				Server.NextFrame(() =>  // Weird CS2 bug that requires doing this twice to show the Joined X team in chat and not stay in limbo
					{
						player.ChangeTeam(CsTeam.CounterTerrorist);
						player.Respawn();

						player.ChangeTeam(CsTeam.Spectator);

						player.ChangeTeam(CsTeam.CounterTerrorist);
						player.Respawn();
					}
				);
			}

			Server.NextFrame(() => Extensions.Teleport(player.PlayerPawn.Value!, CurrentMap.BonusStartZone[bonus], null, new VectorT(0, 0, 0)));
		}
		else
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["invalid_usage",
				"!b <bonus>"]}"
			);
	}

	[ConsoleCommand("css_spec", "Spectate a player or bot by (partial) name, or open a picker menu")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void MovePlayerToSpectator(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		if (command.ArgCount <= 1)
		{
			ChatMenu menu = new ChatMenu("Spectate (press M to rejoin a team, or type !r)");
			foreach (var candidate in GetSpectateCandidates(player))
			{
				menu.AddMenuOption(candidate.PlayerName, (p, o) => SpectateTarget(p, candidate));
			}
			menu.Open(player);
			return;
		}

		string search = command.ArgString.Trim();
		var match = GetSpectateCandidates(player)
			.FirstOrDefault(c => c.PlayerName.Contains(search, StringComparison.OrdinalIgnoreCase));

		if (match == null)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["spec_no_match", search]}");
			return;
		}

		SpectateTarget(player, match);
	}

	/// <summary>
	/// Alive humans and currently-playing replay bots, excluding the caller. `playerList` only holds
	/// connected humans - replay bots are never in it - so both sources are unioned. Dead/spectating
	/// players and idle bots are left out since the engine rejects them as observer targets.
	/// </summary>
	private IEnumerable<CCSPlayerController> GetSpectateCandidates(CCSPlayerController caller)
	{
		return playerList.Values.Select(p => p.Controller)
			.Concat(CurrentMap.ReplayManager.Pool.Where(s => s.IsPlaying && s.Controller != null).Select(s => s.Controller!))
			.Where(c => c.IsValid && !c.Equals(caller) && IsSpectatable(c));
	}

	private static bool IsSpectatable(CCSPlayerController target)
	{
		var pawn = target.PlayerPawn.Value;
		return target.IsValid
			&& target.PawnIsAlive
			&& pawn != null && pawn.IsValid
			&& (target.Team == CsTeam.Terrorist || target.Team == CsTeam.CounterTerrorist);
	}

	/// <summary>
	/// Moves the spectator to Spectator team (abandoning their run) and points their first-person
	/// view at target - the CS2 equivalent of SourceMod's ChangeClientTeam + m_hObserverTarget +
	/// m_iObserverMode. Waits until both the observer pawn exists and the target is alive, so
	/// callers can invoke it right after requesting/respawning a bot.
	/// </summary>
	private void SpectateTarget(CCSPlayerController spectator, CCSPlayerController target)
	{
		Server.NextFrame(() =>
		{
			if (!spectator.IsValid)
				return;

			if (playerList.TryGetValue(spectator.UserId ?? 0, out var oPlayer))
			{
				oPlayer.Timer.Reset();
				oPlayer.Stats.ThisRun.Checkpoints.Clear();
			}

			spectator.MoveToSpectator();

			AddTimer(0.1f, () => TrySetObserverTarget(spectator, target, attemptsLeft: SpectatePollAttempts));
		});
	}

	// 15s at 0.1s per attempt - long enough for the player to pick Spectator from the M menu
	// themselves if the automatic team switch doesn't go through.
	private const int SpectatePollAttempts = 150;
	private const int SpectateManualHintAttempt = SpectatePollAttempts - 15;

	private void TrySetObserverTarget(CCSPlayerController spectator, CCSPlayerController target, int attemptsLeft)
	{
		if (!spectator.IsValid || !target.IsValid)
			return;

		CCSObserverPawn? observerPawn = spectator.ObserverPawn.Value;
		bool observerReady = spectator.Team == CsTeam.Spectator
			&& observerPawn != null && observerPawn.IsValid && observerPawn.ObserverServices != null;

		if (!observerReady || !IsSpectatable(target))
		{
			if (attemptsLeft == SpectateManualHintAttempt && spectator.Team != CsTeam.Spectator)
				spectator.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["spec_manual_hint", target.PlayerName]}");

			if (attemptsLeft > 0)
				AddTimer(0.1f, () => TrySetObserverTarget(spectator, target, attemptsLeft - 1));
			else
				_logger.LogWarning("[{ClassName}] SpectateTarget -> gave up on {Spectator} -> {Target} (spectatorTeam={Team}, spectatorAlive={Alive}, observerPawnValid={ObsValid}, observerServices={ObsServices}, targetSpectatable={TargetOk})",
					nameof(SurfTimer), spectator.PlayerName, target.PlayerName, spectator.Team, spectator.PawnIsAlive,
					observerPawn != null && observerPawn.IsValid, observerPawn?.ObserverServices != null, IsSpectatable(target)
				);
			return;
		}

		uint targetPawnRaw = target.PlayerPawn.Raw;

		var obs = observerPawn!.ObserverServices!;
		obs.ObserverTarget.Raw = targetPawnRaw;
		obs.ObserverMode = (byte)ObserverMode_t.OBS_MODE_IN_EYE;
		// ObserverServices is a pointer component of the pawn - the pawn's pointer field is what
		// has to be marked dirty for the change to be networked to the client.
		Utilities.SetStateChanged(observerPawn, "CBasePlayerPawn", "m_pObserverServices");

		Server.NextFrame(() =>
		{
			if (!spectator.IsValid || !target.IsValid || !observerPawn.IsValid || observerPawn.ObserverServices == null)
				return;

			if (observerPawn.ObserverServices.ObserverTarget.Raw == targetPawnRaw)
			{
				_logger.LogInformation("[{ClassName}] SpectateTarget -> {Spectator} -> {Target}: observer target set (schema)",
					nameof(SurfTimer), spectator.PlayerName, target.PlayerName
				);
				return;
			}

			_logger.LogInformation("[{ClassName}] SpectateTarget -> {Spectator} -> {Target}: schema write was overridden, falling back to spec_player",
				nameof(SurfTimer), spectator.PlayerName, target.PlayerName
			);
			spectator.ExecuteClientCommandFromServer($"spec_player \"{target.PlayerName}\"");
		});
	}

	[ConsoleCommand("css_rank", "Show the current rank of the player for the style they are in")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayerRank(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		int pRank = playerList[player.UserId ?? 0].Stats.PB[playerList[player.UserId ?? 0].Timer.Style].Rank;
		int tRank = CurrentMap.MapCompletions[playerList[player.UserId ?? 0].Timer.Style];
		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["rank",
			CurrentMap.Name!, pRank, tRank]}"
		);
	}


	/*
    #########################
        Replay Commands
    #########################
    */

	/// <summary>
	/// Finds the pool slot the given player is currently spectating, if any.
	/// </summary>
	private ReplayPlayer? FindSpectatedPoolSlot(CCSPlayerController player)
	{
		Player oPlayer = playerList[player.UserId ?? 0];
		foreach (var slot in CurrentMap.ReplayManager.Pool)
		{
			if (slot.Controller != null && oPlayer.IsSpectating(slot.Controller))
				return slot;
		}
		return null;
	}

	[ConsoleCommand("css_replaybotpause", "Pause the replay bot you're spectating")]
	[ConsoleCommand("css_rbpause", "Pause the replay bot you're spectating")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PauseReplay(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || player.Team != CsTeam.Spectator)
			return;

		FindSpectatedPoolSlot(player)?.Pause();
	}

	[ConsoleCommand("css_rbplay", "Restart the replay bot you're spectating from the beginning")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayReplay(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || player.Team != CsTeam.Spectator)
			return;

		var slot = FindSpectatedPoolSlot(player);
		slot?.ResetReplay();
		slot?.Start();
	}

	[ConsoleCommand("css_replaybotflip", "Flips the replay bot you're spectating between Forward/Backward playback")]
	[ConsoleCommand("css_rbflip", "Flips the replay bot you're spectating between Forward/Backward playback")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void ReverseReplay(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || player.Team != CsTeam.Spectator)
			return;

		var slot = FindSpectatedPoolSlot(player);
		if (slot != null)
			slot.FrameTickIncrement *= -1;
	}

	/*
    ########################
        Saveloc Commands
    ########################
    */
	[ConsoleCommand("css_saveloc", "Save current player location to be practiced")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void SavePlayerLocation(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;
		if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
		{
			player.ChangeTeam(CsTeam.CounterTerrorist);
			player.Respawn();
		}

		Player p = playerList[player.UserId ?? 0];
		if (!p.Timer.IsRunning)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_not_in_run"]}");
			return;
		}

		var player_pos = p.Controller.Pawn.Value!.AbsOrigin!;
		var player_angle = p.Controller.PlayerPawn.Value!.EyeAngles;
		var player_velocity = p.Controller.PlayerPawn.Value!.AbsVelocity;

		p.SavedLocations.Add(new SavelocFrame
		{
			Pos = new VectorT(player_pos.X, player_pos.Y, player_pos.Z),
			Ang = new QAngleT(player_angle.X, player_angle.Y, player_angle.Z),
			Vel = new VectorT(player_velocity.X, player_velocity.Y, player_velocity.Z),
			Tick = p.Timer.Ticks
		});
		p.CurrentSavedLocation = p.SavedLocations.Count - 1;

		p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_saved",
			p.SavedLocations.Count - 1]}"
		);
	}

	[ConsoleCommand("css_tele", "Teleport player to current saved location")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void TeleportPlayerLocation(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;
		if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
		{
			player.ChangeTeam(CsTeam.CounterTerrorist);
			player.Respawn();
		}

		Player p = playerList[player.UserId ?? 0];

		if (p.SavedLocations.Count == 0)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_no_locations"]}");
			return;
		}

		if (!p.Timer.IsRunning)
			p.Timer.Start();

		if (!p.Timer.IsPracticeMode)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_practice"]}");
			p.Timer.IsPracticeMode = true;
		}

		if (command.ArgCount > 1)
		{
			try
			{
				int tele_n = int.Parse(command.ArgByIndex(1));
				if (tele_n < p.SavedLocations.Count)
					p.CurrentSavedLocation = tele_n;
			}
			catch
			{
				Exception exception = new("sum ting wong");
				throw exception;
			}
		}
		SavelocFrame location = p.SavedLocations[p.CurrentSavedLocation];
		Server.NextFrame(() =>
			{
				Extensions.Teleport(p.Controller.PlayerPawn.Value!, location.Pos, location.Ang, location.Vel);
				p.Timer.Ticks = location.Tick;
			}
		);

		p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_teleported",
			p.CurrentSavedLocation]}"
		);
	}

	[ConsoleCommand("css_teleprev", "Teleport player to previous saved location")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void TeleportPlayerLocationPrev(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;
		if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
		{
			player.ChangeTeam(CsTeam.CounterTerrorist);
			player.Respawn();
		}

		Player p = playerList[player.UserId ?? 0];

		if (p.SavedLocations.Count == 0)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_no_locations"]}");
			return;
		}

		if (p.CurrentSavedLocation == 0)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_first"]}");
		}
		else
		{
			p.CurrentSavedLocation--;
		}

		TeleportPlayerLocation(player, command);

		p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_teleported",
			p.CurrentSavedLocation]}"
		);
	}

	[ConsoleCommand("css_telenext", "Teleport player to next saved location")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void TeleportPlayerLocationNext(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;
		if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
		{
			player.ChangeTeam(CsTeam.CounterTerrorist);
			player.Respawn();
		}

		Player p = playerList[player.UserId ?? 0];

		if (p.SavedLocations.Count == 0)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_no_locations"]}");
			return;
		}

		if (p.CurrentSavedLocation == p.SavedLocations.Count - 1)
		{
			p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_last"]}");
		}
		else
		{
			p.CurrentSavedLocation++;
		}

		TeleportPlayerLocation(player, command);

		p.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["saveloc_teleported",
			p.CurrentSavedLocation]}"
		);
	}




	/*
    ########################
           TEST CMDS
    ########################
    */
	[ConsoleCommand("css_rx", "x")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	[RequiresPermissions("@css/root")]
	public void TestSituationCmd(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		Player oPlayer = playerList[player.UserId ?? 0];

		CurrentRun.PrintSituations(oPlayer);
	}

	[ConsoleCommand("css_testx", "x")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	[RequiresPermissions("@css/root")]
	public void TestCmd(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		Player oPlayer = playerList[player.UserId ?? 0];
		int style = oPlayer.Timer.Style;

		player.PrintToChat($"{Config.PluginPrefix}{ChatColors.Lime}====== PLAYER ======");
		player.PrintToChat($"{Config.PluginPrefix} Profile ID: {ChatColors.Green}{oPlayer.Profile.ID}");
		player.PrintToChat($"{Config.PluginPrefix} Steam ID: {ChatColors.Green}{oPlayer.Profile.SteamID}");
		player.PrintToChat($"{Config.PluginPrefix} MapTime ID: {ChatColors.Green}{oPlayer.Stats.PB[style].ID} - {PlayerHud.FormatTime(oPlayer.Stats.PB[style].RunTime)}");
		player.PrintToChat($"{Config.PluginPrefix} Stage: {ChatColors.Green}{oPlayer.Timer.Stage}");
		player.PrintToChat($"{Config.PluginPrefix} IsStageMode: {ChatColors.Green}{oPlayer.Timer.IsStageMode}");
		player.PrintToChat($"{Config.PluginPrefix} IsRunning: {ChatColors.Green}{oPlayer.Timer.IsRunning}");
		player.PrintToChat($"{Config.PluginPrefix} Checkpoint: {ChatColors.Green}{oPlayer.Timer.Checkpoint}");
		player.PrintToChat($"{Config.PluginPrefix} Bonus: {ChatColors.Green}{oPlayer.Timer.Bonus}");
		player.PrintToChat($"{Config.PluginPrefix} Ticks: {ChatColors.Green}{oPlayer.Timer.Ticks}");
		player.PrintToChat($"{Config.PluginPrefix} StagePB ID: {ChatColors.Green}{oPlayer.Stats.StagePB[1][style].ID} - {PlayerHud.FormatTime(oPlayer.Stats.StagePB[1][style].RunTime)}");


		player.PrintToChat($"{Config.PluginPrefix}{ChatColors.Orange}====== MAP ======");
		player.PrintToChat($"{Config.PluginPrefix} Map ID: {ChatColors.Green}{CurrentMap.ID}");
		player.PrintToChat($"{Config.PluginPrefix} Map Name: {ChatColors.Green}{CurrentMap.Name}");
		player.PrintToChat($"{Config.PluginPrefix} Map Stages: {ChatColors.Green}{CurrentMap.Stages}");
		player.PrintToChat($"{Config.PluginPrefix} Map Bonuses: {ChatColors.Green}{CurrentMap.Bonuses}");
		player.PrintToChat($"{Config.PluginPrefix} Map Completions (Style: {ChatColors.Green}{style}{ChatColors.Default}): {ChatColors.Green}{CurrentMap.MapCompletions[style]}");
		player.PrintToChat($"{Config.PluginPrefix} CurrentMap.WR[{style}].Ticks: {ChatColors.Green}{CurrentMap.WR[style].RunTime}");
		player.PrintToChat($"{Config.PluginPrefix} CurrentMap.WR[{style}].Checkpoints.Count: {ChatColors.Green}{CurrentMap.WR[style].Checkpoints!.Count}");


		player.PrintToChat($"{Config.PluginPrefix}{ChatColors.Purple}====== REPLAYS ======");
		player.PrintToChat($"{Config.PluginPrefix} .ReplayRecorder.Frames.Count: {ChatColors.Green}{oPlayer.ReplayRecorder.Frames.Count}");
		player.PrintToChat($"{Config.PluginPrefix} .ReplayRecorder.IsRecording: {ChatColors.Green}{oPlayer.ReplayRecorder.IsRecording}");
		player.PrintToChat($"{Config.PluginPrefix} .ReplayManager.MapWR.RecordRunTime: {ChatColors.Green}{CurrentMap.ReplayManager.MapWR.RecordRunTime}");
		player.PrintToChat($"{Config.PluginPrefix} .ReplayManager.MapWR.Frames.Count: {ChatColors.Green}{CurrentMap.ReplayManager.MapWR.Frames.Count}");
		player.PrintToChat($"{Config.PluginPrefix} .ReplayManager.Pool.Count: {ChatColors.Green}{CurrentMap.ReplayManager.Pool.Count}/{Config.ReplayPoolCap}");
		foreach (var slot in CurrentMap.ReplayManager.Pool)
		{
			player.PrintToChat($"{Config.PluginPrefix} Pool slot: Type {ChatColors.Green}{slot.Type}{ChatColors.Default} Stage {ChatColors.Green}{slot.Stage}{ChatColors.Default} " +
				$"Controller {ChatColors.Green}{slot.Controller?.PlayerName ?? "none"}{ChatColors.Default} IsPlaying {ChatColors.Green}{slot.IsPlaying}{ChatColors.Default} RepeatCount {ChatColors.Green}{slot.RepeatCount}");
		}
	}

	[ConsoleCommand("css_ctest", "x")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	[RequiresPermissions("@css/root")]
	public void ConsoleTestCmd(CCSPlayerController? player, CommandInfo command)
	{
		Console.WriteLine("====== MAP INFO ======");
		Console.WriteLine($"Map ID: {CurrentMap.ID}");
		Console.WriteLine($"Map Name: {CurrentMap.Name}");
		Console.WriteLine($"Map Author: {CurrentMap.Author}");
		Console.WriteLine($"Map Tier: {CurrentMap.Tier}");
		Console.WriteLine($"Map Stages: {CurrentMap.Stages}");
		Console.WriteLine($"Map Bonuses: {CurrentMap.Bonuses}");
		Console.WriteLine($"Map Completions: {CurrentMap.MapCompletions[0]}");

		Console.WriteLine("====== MAP WR INFO ======");
		Console.WriteLine($"Map WR ID: {CurrentMap.WR[0].ID}");
		Console.WriteLine($"Map WR Name: {CurrentMap.WR[0].Name}");
		Console.WriteLine($"Map WR Type: {CurrentMap.WR[0].Type}");
		Console.WriteLine($"Map WR Rank: {CurrentMap.WR[0].Rank}");
		Console.WriteLine($"Map WR Checkpoints.Count: {CurrentMap.WR[0].Checkpoints?.Count}");
		Console.WriteLine($"Map WR ReplayFramesBase64.Length: {CurrentMap.WR[0].ReplayFrames?.ToString().Length}");
		Console.WriteLine($"Map WR ReplayFrames.Length: {CurrentMap.WR[0].ReplayFrames?.ToString().Length}");

		Console.WriteLine("====== MAP StageWR INFO ======");
		Console.WriteLine($"Map Stage Completions ({CurrentMap.Stages} + 1): {CurrentMap.StageCompletions.Length}");
		Console.WriteLine($"Map StageWR ID: {CurrentMap.StageWR[1][0].ID}");
		Console.WriteLine($"Map StageWR Name: {CurrentMap.StageWR[1][0].Name}");
		Console.WriteLine($"Map StageWR Type: {CurrentMap.StageWR[1][0].Type}");
		Console.WriteLine($"Map StageWR Rank: {CurrentMap.StageWR[1][0].Rank}");
		Console.WriteLine($"Map StageWR ReplayFramesBase64.Length: {CurrentMap.StageWR[1][0].ReplayFrames?.ToString().Length}");
		Console.WriteLine($"Map StageWR ReplayFrames.Length: {CurrentMap.StageWR[1][0].ReplayFrames?.ToString().Length}");

		Console.WriteLine($"Map Bonus Completions ({CurrentMap.Bonuses} + 1): {CurrentMap.BonusCompletions.Length}");

		if (CurrentMap.Stages > 0)
		{
			for (int i = 1; i <= CurrentMap.Stages; i++)
			{
				Console.WriteLine($"========== Stage {i} ==========");
				Console.WriteLine($"ID: {CurrentMap.StageWR[i][0].ID}");
				Console.WriteLine($"Name: {CurrentMap.StageWR[i][0].Name}");
				Console.WriteLine($"RunTime: {CurrentMap.StageWR[i][0].RunTime}");
				Console.WriteLine($"Type: {CurrentMap.StageWR[i][0].Type}");
				Console.WriteLine($"Rank: {CurrentMap.StageWR[i][0].Rank}");
				Console.WriteLine($"Stage Completions: {CurrentMap.StageCompletions[i][0]}");
			}
		}
	}
}
