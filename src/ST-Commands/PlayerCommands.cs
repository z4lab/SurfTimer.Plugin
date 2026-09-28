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
		TeleportToZone(player, ZoneType.MapStart, 1);
	}

	/// <summary>
	/// Teleports the player (velocity zeroed) to the nearest trigger of a zone - maps can have several
	/// per zone (e.g. one per side of a course). Returns false if the map has no such zone.
	/// </summary>
	private bool TeleportToZone(CCSPlayerController player, ZoneType type, short number)
	{
		var pawn = player.PlayerPawn.Value;
		VectorT? from = player.PawnIsAlive && pawn != null && pawn.IsValid && pawn.AbsOrigin != null
			? pawn.AbsOrigin.ToVector_t()
			: null;

		var zone = CurrentMap.FindNearestZone(type, number, from);
		if (zone == null)
			return false;

		Server.NextFrame(() =>
		{
			var target = player.PlayerPawn.Value;
			if (target != null && target.IsValid)
				Extensions.Teleport(target, zone.Teleport, null, new VectorT(0, 0, 0));
		});
		return true;
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


		bool teleported = oPlayer.Timer.IsBonusMode
			? oPlayer.Timer.Bonus != 0 && TeleportToZone(player, ZoneType.BonusStart, oPlayer.Timer.Bonus)
			: oPlayer.Timer.Stage > 1 && TeleportToZone(player, ZoneType.StageStart, oPlayer.Timer.Stage);

		if (!teleported) // Reset back to map start
			TeleportToZone(player, ZoneType.MapStart, 1);
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
		ZoneType zoneType = stage == 1 ? ZoneType.MapStart : ZoneType.StageStart;
		if (!CurrentMap.HasZone(zoneType, stage))
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

		if (stage > 1)
		{
			playerList[player.UserId ?? 0].Timer.Stage = stage;
			playerList[player.UserId ?? 0].Timer.IsStageMode = true;
		}
		TeleportToZone(player, zoneType, stage);

		// To-do: If you run this while you're in the start zone, endtouch for the start zone runs after you've teleported
		//        causing the timer to start. This needs to be fixed.
		return true;
	}

	[ConsoleCommand("css_hideself", "Toggle hiding your own player model (and first-person legs)")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayerToggleHideSelf(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var oPlayer))
			return;

		oPlayer.HideSelf = !oPlayer.HideSelf;
		oPlayer.Profile.SetSetting(PlayerProfile.SettingHideSelf, oPlayer.HideSelf ? "1" : "0"); // Kept over reconnects
		oPlayer.ApplySelfVisibility();
		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull[oPlayer.HideSelf ? "hideself_on" : "hideself_off"]}");
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

		if (CurrentMap.HasZone(ZoneType.BonusStart, (short)bonus))
		{
			playerList[player.UserId ?? 0].Timer.Reset();
			playerList[player.UserId ?? 0].Timer.IsBonusMode = true;
			playerList[player.UserId ?? 0].Timer.Bonus = (short)bonus;

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

			TeleportToZone(player, ZoneType.BonusStart, (short)bonus);
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
			if (!playerList.TryGetValue(player.UserId ?? 0, out var oPlayer))
				return;

			var items = GetSpectateCandidates(player).Select(SpectateMenuItem).ToList();
			if (items.Count == 0)
			{
				player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["spec_none"]}");
				return;
			}

			MenuPresenter.Show(oPlayer, new HudMenu("Spectate · M or !r to rejoin", [new HudMenuTab("Players", items)]));
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
	/// A !spec menu row: the name plus what they're doing right now, updated while the menu is open -
	/// a player's running time and stage/bonus (or "idle"), a replay bot's replay and its time.
	/// </summary>
	private HudMenuItem SpectateMenuItem(CCSPlayerController target)
	{
		static string Time(int ticks) => PlayerHud.FormatTime(ticks, PlayerTimer.TimeFormatStyle.Full);

		var replay = CurrentMap.ReplayManager.Pool.Find(s => s.Controller != null && s.Controller.Equals(target));
		if (replay != null)
		{
			return new HudMenuItem(target.PlayerName, p => SpectateTarget(p, target),
				PlayerHud.ReplayTypeLabel(replay), () => Time(replay.ReplayCurrentRunTime));
		}

		playerList.TryGetValue(target.UserId ?? 0, out var targetPlayer);
		return new HudMenuItem(target.PlayerName, p => SpectateTarget(p, target), "", () =>
		{
			if (targetPlayer == null || !targetPlayer.Timer.IsRunning)
				return "idle";

			string where = targetPlayer.Timer.IsBonusMode ? $"B{targetPlayer.Timer.Bonus}"
				: CurrentMap.Stages > 0 ? $"S{Math.Max((short)1, targetPlayer.Timer.Stage)}"
				: "";
			return $"{Time(targetPlayer.Timer.Ticks)}  {where}".TrimEnd();
		});
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
		{
			p.Timer.Start();
			p.ResetSync();
		}

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
}
