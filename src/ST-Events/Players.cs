using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	private bool _customHudRecreatePending;

	[GameEventHandler]
	public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		var controller = @event.Userid;
		if (controller == null || !controller.IsValid)
			return HookResult.Continue;

		if (!controller.IsBot)
		{
			// First spawn on this map: the saved run is restored, else the map start (RunResume.cs) - right away, so the
			// spawn point's start zone doesn't count until the player is placed
			if (playerList.TryGetValue(controller.UserId ?? 0, out var firstSpawn))
				BeginFirstSpawn(firstSpawn);

			// Re-apply the hide-legs option on every spawn - the game resets the pawn's render state.
			// Slight delay so the spawn has finished setting up the model/weapons first.
			AddTimer(0.1f, () =>
			{
				if (controller.IsValid && playerList.TryGetValue(controller.UserId ?? 0, out var spawnedPlayer))
					spawnedPlayer.ApplySelfVisibility();
			});
			return HookResult.Continue;
		}

		// No map during a map change / the replay nav reload
		if (CurrentMap == null)
			return HookResult.Continue;

		// A replay bot respawned (round restart) - playback puts it back in place, the spawn loadout goes
		if (CurrentMap.ReplayManager.IsControllerConnectedToReplayPlayer(controller))
		{
			AddTimer(0.1f, () =>
			{
				if (controller.IsValid && controller.PawnIsAlive)
					controller.RemoveWeapons();
			});
			return HookResult.Continue;
		}

		_logger.LogTrace("OnPlayerSpawn -> Player {Name} spawned.",
			controller.PlayerName
		);

		// Claim this newly-spawned bot for whichever pool slot is awaiting one
		foreach (var slot in CurrentMap.ReplayManager.Pool)
		{
			if (slot.Controller != null)
				continue;

			// The permanent map bot loops forever, requested ones play Config.ReplayRepeatCount times
			int repeat = slot.IsPermanent ? -1 : Config.ReplayRepeatCount;
			slot.SetController(controller, repeat);
			slot.LoadReplayData(repeat);
			slot.LastWatchedAt = DateTime.UtcNow; // Grace period for the requester to start watching

			// CS2 kicks bots without a pending spectator team when another player joins (cs2kz-metamod)
			controller.PendingTeamNum = 1;
			controller.SwitchTeam(CsTeam.Terrorist);

			AddTimer(1.5f, () =>
			{
				if (slot.Controller == null || !slot.Controller.IsValid)
					return;

				slot.Controller.RemoveWeapons();
				slot.Start();
				slot.FormatBotName();
			});

			// Auto-spectate whoever requested this replay, if they're still around
			_logger.LogDebug("[{ClassName}] OnPlayerSpawn -> PendingSpectatorUserId = {PendingSpectatorUserId} for bot {BotName}",
				nameof(SurfTimer), slot.PendingSpectatorUserId, controller.PlayerName
			);
			if (slot.PendingSpectatorUserId.HasValue)
			{
				bool found = playerList.TryGetValue(slot.PendingSpectatorUserId.Value, out var requester);
				_logger.LogDebug("[{ClassName}] OnPlayerSpawn -> Auto-spectate lookup: found={Found} controllerValid={Valid}",
					nameof(SurfTimer), found, found && requester!.Controller.IsValid
				);

				if (found && requester!.Controller.IsValid)
					SpectateTarget(requester.Controller, controller);

				slot.PendingSpectatorUserId = null;
			}

			return HookResult.Continue;
		}

		return HookResult.Continue;
	}

	/// <summary>
	/// Joining T/CT mid-round doesn't spawn you (mp_respawn_on_death_* only fires on death, and
	/// round win conditions are ignored so no restart happens) - so respawn ourselves: humans
	/// rejoining from Spectator (M menu / jointeam), and freshly added bots a replay slot is
	/// waiting on (they're claimed in OnPlayerSpawn, which never fires if they stay dead).
	/// </summary>
	[GameEventHandler]
	public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
	{
		var controller = @event.Userid;
		if (controller == null || !controller.IsValid || @event.Disconnect)
			return HookResult.Continue;

		// Spectator and back (RunResume.cs): where they were is remembered, and coming back puts them there
		if (!controller.IsBot && playerList.TryGetValue(controller.UserId ?? 0, out var teamPlayer))
		{
			if (@event.Team == (int)CsTeam.Spectator)
				RememberSpecReturn(teamPlayer);
			else if ((@event.Team == (int)CsTeam.Terrorist || @event.Team == (int)CsTeam.CounterTerrorist)
				&& (@event.Oldteam == (int)CsTeam.Spectator || @event.Oldteam == (int)CsTeam.None))
				BeginReturnFromSpectator(teamPlayer);
		}

		if (@event.Team != (int)CsTeam.Terrorist && @event.Team != (int)CsTeam.CounterTerrorist)
			return HookResult.Continue;

		if (controller.IsBot)
		{
			_logger.LogDebug("[{ClassName}] OnPlayerTeam -> Bot {BotName} joined team {Team}",
				nameof(SurfTimer), controller.PlayerName, @event.Team
			);

			if (CurrentMap == null)
				return HookResult.Continue; // Map change - no replay slots

			bool slotAwaitingBot = CurrentMap.ReplayManager.Pool.Any(s => s.Controller == null);
			if (!slotAwaitingBot || CurrentMap.ReplayManager.IsControllerConnectedToReplayPlayer(controller))
				return HookResult.Continue;
		}

		AddTimer(0.1f, () =>
		{
			if (!controller.IsValid || controller.PawnIsAlive)
				return;

			if (controller.Team != CsTeam.Terrorist && controller.Team != CsTeam.CounterTerrorist)
				return;

			controller.Respawn();
		});

		return HookResult.Continue;
	}

	[GameEventHandler]
	public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
	{
		var player = @event.Userid;

		string name = player!.PlayerName;
		string country;

		// GeoIP
		// Check if the IP is private before attempting GeoIP lookup
		string ipAddress = player.IpAddress!.Split(":")[0];
		if (!Extensions.IsPrivateIP(ipAddress))
		{
			DatabaseReader geoipDB = new(Config.PluginPath + "data/GeoIP/GeoLite2-Country.mmdb");
			country = geoipDB.Country(ipAddress).Country.IsoCode ?? "XX";
			geoipDB.Dispose();
		}
		else
		{
			country = "LL";  // Handle local IP appropriately
		}

		if (DB == null)
		{
			_logger.LogCritical("OnPlayerConnect -> DB object is null, this shouldn't happen.");
			Exception ex = new("CS2 Surf ERROR >> OnPlayerConnect -> DB object is null, this shouldn't happen.");
			throw ex;
		}

		int? mapId = CurrentMap != null && CurrentMap.ID > 0 ? CurrentMap.ID : null;
		ulong steamId = player.SteamID;
		var profile = Task.Run(() => PlayerProfile.CreateAsync(steamId, name, country, mapId)).GetAwaiter().GetResult();
		var movement = new CCSPlayer_MovementServices(player.PlayerPawn.Value!.MovementServices!.Handle);

		// Options (legs, hiding, chat, HUD) come from the profile's stored settings
		var p = new Player(player, movement, profile);

		// No lock - we use thread-safe method AddOrUpdate
		playerList.AddOrUpdate(player.UserId ?? 0, p, (_, _) => p);

		// First player on this map: the game mode config may have run after our map start apply
		if (!player.IsBot && !_serverSettingsAppliedForPlayers)
		{
			_serverSettingsAppliedForPlayers = true;
			ApplyServerSettings("first player joined");
		}

		// CS2 can drop custom HUD texts for existing players when someone joins - resend everyone's
		CustomHud.ResendAll();

		// The joining client only shows the HUD if the entity spawns while it's in game - re-create it
		// once its HUD has loaded (debounced, so several joins at once re-create it only once)
		if (Config.CustomHudEnabled && !player.IsBot && !_customHudRecreatePending)
		{
			_customHudRecreatePending = true;
			AddTimer(1.0f, () =>
			{
				_customHudRecreatePending = false;
				CustomHud.Recreate();
			});
		}

		_ = p.Stats.LoadPlayerMapTimesData(p);

		if (!player.IsBot)
		{
			// The saved run of this map, read in the background - restored on the first spawn (RunResume.cs)
			if (CurrentMap != null && CurrentMap.ID > 0)
				PlayerStateService.BeginLoad(p);

			// Already spawned before this event: placed now
			Server.NextFrame(() =>
			{
				if (player.IsValid && player.PawnIsAlive)
					BeginFirstSpawn(p);
			});
		}

		// Go back to the Main Thread for chat message
		Server.NextFrame(() =>
		{
			// Print join messages
			ChatAnnounce.Send(ChatAnnounce.Kind.Connect, null, $"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["player_connected",
				name, country]}");
			_logger.LogTrace("[{Prefix}] {PlayerName} has connected from {Country}.",
				Config.PluginName, name, playerList[player.UserId ?? 0].Profile.Country
			);
		});
		return HookResult.Continue;
	}

	[GameEventHandler] // Player Disconnect Event
	public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
	{
		var player = @event.Userid;

		if (player == null)
		{
			_logger.LogError("OnPlayerDisconnect -> 'player' is NULL ({IsNull})",
				player == null
			);
			return HookResult.Continue;
		}

		// No map during a map change (players are disconnected after the old map is cleaned up)
		var pool = CurrentMap?.ReplayManager.Pool;
		for (int i = (pool?.Count ?? 0) - 1; i >= 0; i--)
		{
			if (pool![i].Controller != null && pool[i].Controller!.Equals(player))
			{
				pool[i].Reset();
				pool.RemoveAt(i);
			}
		}

		if (player.IsBot || !player.IsValid)
		{
			if (player.IsBot)
			{
				// Reason is the engine's disconnect reason code - a kick right after "Bot connected" means
				// something removed it (e.g. a map script)
				_logger.LogDebug("[{ClassName}] Bot disconnected: {Name} (reason {Reason})",
					nameof(SurfTimer), @event.Name, @event.Reason);
			}
			return HookResult.Continue;
		}
		else
		{
			if (DB == null)
			{
				_logger.LogCritical("OnPlayerDisconnect -> DB object is null, this shouldnt happen.");
				Exception ex = new("CS2 Surf ERROR >> OnPlayerDisconnect -> DB object is null, this shouldnt happen.");
				throw ex;
			}

			if (!playerList.ContainsKey(player.UserId ?? 0))
			{
				_logger.LogError("OnPlayerDisconnect -> playerList does NOT contain player.UserId, this shouldn't happen. Player: {PlayerName} ({UserId})",
					player.PlayerName, player.UserId
				);
			}
			else
			{
				int userId = player.UserId ?? 0;
				ChatPrompt.Forget(userId);
				ForgetChatSpam(userId);

				// Their replay bot stays until nobody watches it (OnTick upkeep)
				foreach (var slot in CurrentMap?.ReplayManager.Pool ?? [])
				{
					if (slot.RequesterUserId == userId)
						slot.RequesterUserId = null;
				}

				if (playerList.TryGetValue(userId, out var playerData))
				{
					// Release cursor mode, or whoever gets this slot next could start with it
					playerData.HUD.CloseMenu();
					PlayerStateService.SaveFinal(playerData, "disconnect"); // The running run, to resume (any server)
					RemoveCenterSpeed(playerData.CenterSpeedView);
					StatsService.Flush(playerData, final: true); // Also closes the session
					playerList.TryRemove(userId, out _);
				}
			}
			return HookResult.Continue;
		}
	}

}
