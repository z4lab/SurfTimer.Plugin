using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	[GameEventHandler]
	public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		var controller = @event.Userid;
		if (controller == null || !controller.IsValid)
			return HookResult.Continue;

		if (!controller.IsBot)
		{
			// Re-apply !hideself on every spawn - the game resets the pawn's render state. Slight
			// delay so the spawn has finished setting up the model/weapons first.
			AddTimer(0.1f, () =>
			{
				if (controller.IsValid && playerList.TryGetValue(controller.UserId ?? 0, out var spawnedPlayer))
					spawnedPlayer.ApplySelfVisibility();
			});
			return HookResult.Continue;
		}

		if (CurrentMap.ReplayManager.IsControllerConnectedToReplayPlayer(controller))
			return HookResult.Continue;

		_logger.LogTrace("OnPlayerSpawn -> Player {Name} spawned.",
			controller.PlayerName
		);

		// Claim this newly-spawned bot for whichever pool slot is awaiting one
		foreach (var slot in CurrentMap.ReplayManager.Pool)
		{
			if (slot.Controller != null)
				continue;

			slot.SetController(controller, Config.ReplayRepeatCount);
			slot.LoadReplayData(Config.ReplayRepeatCount);

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
			_logger.LogInformation("[{ClassName}] OnPlayerSpawn -> PendingSpectatorUserId = {PendingSpectatorUserId} for bot {BotName}",
				nameof(SurfTimer), slot.PendingSpectatorUserId, controller.PlayerName
			);
			if (slot.PendingSpectatorUserId.HasValue)
			{
				bool found = playerList.TryGetValue(slot.PendingSpectatorUserId.Value, out var requester);
				_logger.LogInformation("[{ClassName}] OnPlayerSpawn -> Auto-spectate lookup: found={Found} controllerValid={Valid}",
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

		if (@event.Team != (int)CsTeam.Terrorist && @event.Team != (int)CsTeam.CounterTerrorist)
			return HookResult.Continue;

		if (controller.IsBot)
		{
			_logger.LogInformation("[{ClassName}] OnPlayerTeam -> Bot {BotName} joined team {Team}",
				nameof(SurfTimer), controller.PlayerName, @event.Team
			);

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

		var profile = PlayerProfile.CreateAsync(player.SteamID, name, country).GetAwaiter().GetResult();
		var movement = new CCSPlayer_MovementServices(player.PlayerPawn.Value!.MovementServices!.Handle);

		var p = new Player(player, movement, profile);

		// No lock - we use thread-safe method AddOrUpdate
		playerList.AddOrUpdate(player.UserId ?? 0, p, (_, _) => p);

		_ = p.Stats.LoadPlayerMapTimesData(p);

		// Go back to the Main Thread for chat message
		Server.NextFrame(() =>
		{
			// Print join messages
			Server.PrintToChatAll($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["player_connected",
				name, country]}"
			);
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

		for (int i = CurrentMap.ReplayManager.Pool.Count - 1; i >= 0; i--)
		{
			if (CurrentMap.ReplayManager.Pool[i].Controller != null && CurrentMap.ReplayManager.Pool[i].Controller!.Equals(player))
			{
				CurrentMap.ReplayManager.Pool[i].Reset();
				CurrentMap.ReplayManager.Pool.RemoveAt(i);
			}
		}

		if (player.IsBot || !player.IsValid)
		{
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

				if (playerList.TryGetValue(userId, out var playerData))
				{
					_ = playerData.Profile.UpdatePlayerProfile(player.PlayerName);
					playerList.TryRemove(userId, out _);
				}
			}
			return HookResult.Continue;
		}
	}

}
