/*

    Official Timer plugin for the CS2 Surf Initiative.
    Copyright (C) 2024  Liam C. (Infra)
    Copyright (C) 2025  tslashd
    Copyright (C) 2026  z4lab
    Copyright (C) 2026  13ace37

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Original Source: https://github.com/CS2Surf/Timer
	Modified Fork: https://github.com/z4lab/SurfTimer.Plugin
*/

#define DEBUG

using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

// Gameplan: https://github.com/z4lab/SurfTimer.Plugin/tree/dev/README.md
[MinimumApiVersion(337)]
public partial class SurfTimer : BasePlugin
{
	private readonly ILogger<SurfTimer> _logger;
	public static IServiceProvider ServiceProvider { get; private set; } = null!;

	// Inject ILogger and store IServiceProvider globally
	public SurfTimer(ILogger<SurfTimer> logger, IServiceProvider serviceProvider)
	{
		_logger = logger;
		ServiceProvider = serviceProvider;
	}

	// Metadata
	public override string ModuleName => $"z4lab/{Config.PluginName}";
	public override string ModuleVersion => "1.0.1";
	public override string ModuleDescription => Config.PluginName;
	public override string ModuleAuthor => "z4lab";

	// Globals
	private readonly ConcurrentDictionary<int, Player> playerList = new();

	// For code outside the plugin class (chat announcements, transmit) - set in Load
	private static SurfTimer? _instance;
	internal static IEnumerable<Player> OnlinePlayers => _instance?.playerList.Values ?? Enumerable.Empty<Player>();
	internal static Database DB { get; private set; } = null!;
	public static Map CurrentMap { get; private set; } = null!;

	// server_settings.cfg was run for the first player of this map (Players.cs, OnPlayerConnectFull)
	private bool _serverSettingsAppliedForPlayers;

	/* ========== MAP START HOOKS ========== */
	public void OnMapStart(string mapName)
	{
		// After the game mode config of the new map (see ApplyServerSettings) - twice, as its timing varies
		_serverSettingsAppliedForPlayers = false;
		AddTimer(2f, () => ApplyServerSettings("map start"), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
		AddTimer(10f, () => ApplyServerSettings("map start, again"), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

		// The server's map list (MapList.cs) - after the workshop collection is known
		AddTimer(5f, RefreshMapList, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

		// Initialise Map Object
		if ((CurrentMap == null || CurrentMap.Name!.Equals(mapName)) && mapName.Contains("surf_"))
		{
			_logger.LogInformation($"[CS2 Surf] {Config.PluginName} Initializing Map object for {mapName}");

			Server.NextWorldUpdateAsync(async () => // NextWorldUpdate runs even during server hibernation
			{
				_logger.LogInformation($"[CS2 Surf] {Config.PluginName} {ModuleVersion} - loading map {mapName}");
				var (stagesAsCheckpoints, zones) = LoadMapBootstrap(mapName);
				CurrentMap = new Map(mapName, stagesAsCheckpoints, zones);
				await CurrentMap.InitializeAsync();
			});
		}
	}

	/// <summary>
	/// What's needed before the map's zones are read: its stages_as_checkpoints setting and its stored zones
	/// (empty = import them from the map). Loaded right away - two small queries on the main thread, as on
	/// player connects. Without the database: stages, and zones from the map.
	/// </summary>
	private (bool StagesAsCheckpoints, List<ZoneDefinition> Zones) LoadMapBootstrap(string mapName)
	{
		try
		{
			return Task.Run(async () =>
			{
				string? value = await MapRepository.GetSettingByNameAsync(mapName, Map.SettingStagesAsCheckpoints);
				var zones = await ZoneRepository.GetByMapNameAsync(mapName);
				return (value is "1" or "true", zones);
			}).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[{Prefix}] Reading the zones of {Map} failed - using the map's own zones", Config.PluginName, mapName);
			return (false, new List<ZoneDefinition>());
		}
	}

	public void OnMapEnd()
	{
		if (CurrentMap is not null)
		{
			_logger.LogInformation(
				"[{Prefix}] Map ({MapName}) ended. Cleaning up resources...",
				Config.PluginName,
				CurrentMap.Name
			);
		}

		// Playtime / attempts of this map - players come back as new Player objects
		foreach (var player in playerList.Values)
			StatsService.Flush(player, final: true);

		// Per-map cvar overrides back to the server's values, open chat prompts dropped
		MapCvars.RestoreAll();
		ChatPrompt.ForgetAll();
		ClearHeldWeapons();
		ForgetTrails();
		_zoneEditor = null; // The editor's draft goes with the map (unsaved changes are lost)
		ForgetZoneOutlines();

		// Clear/reset stuff here
		CurrentMap = null!;
		playerList.Clear();
	}

	[GameEventHandler]
	public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
	{
		// Load cvars/other configs here
		// Execute server_settings.cfg

		ConVarHelper.RemoveCheatFlagFromConVar("bot_stop");
		ConVarHelper.RemoveCheatFlagFromConVar("bot_freeze");
		ConVarHelper.RemoveCheatFlagFromConVar("bot_zombie");

		ApplyServerSettings("round start");
		return HookResult.Continue;
	}

	/// <summary>
	/// Runs SurfTimer/server_settings.cfg, then the map's own cvar overrides (those win). Not only on round
	/// start: after a map change CS2 also runs its game mode config (mp_timelimit, no autobhop, ...) and
	/// the order varies - so it's also run shortly after map start and when the first player joins.
	/// </summary>
	private void ApplyServerSettings(string reason)
	{
		Server.ExecuteCommand("execifexists SurfTimer/server_settings.cfg");
		AddTimer(0.5f, () =>
		{
			if (CurrentMap != null)
				MapCvars.Apply(CurrentMap.Settings);
		}, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

		_logger.LogInformation("[{Prefix}] Executed configuration: server_settings.cfg ({Reason})", Config.PluginName, reason);
	}

	/* ========== PLUGIN LOAD ========== */
	/// <summary>
	/// Other plugins' APIs are available now - CS2-SimpleAdmin's gags for the chat processor.
	/// </summary>
	public override void OnAllPluginsLoaded(bool hotReload)
	{
		SimpleAdminGags.Resolve(_logger);
	}

	public override void Load(bool hotReload)
	{
		_instance = this;
		LocalizationService.Init(Localizer);

		// === Database: connect, create / upgrade the schema, clean up after a crash ===
		try
		{
			DB = new Database(Config.MySql.GetSettings());
			Task.Run(async () =>
			{
				await MigrationRunner.RunAsync(DB, _logger);
				int closed = await PlayerRepository.CloseStaleSessionsAsync();
				if (closed > 0)
					_logger.LogInformation("[{Prefix}] Closed {Count} player session(s) left open by a crash", Config.PluginName, closed);
			}).GetAwaiter().GetResult();

			_logger.LogInformation("[{Prefix}] Database ready (table prefix '{TablePrefix}').", Config.PluginName, DB.TablePrefix);
		}
		catch (Exception ex)
		{
			_logger.LogCritical(ex, "[{Prefix}] Database setup failed - check cfg/SurfTimer/database.json and that the database exists.",
				Config.PluginName);
			throw new Exception($"[{Config.PluginName}] Database setup failed: {ex.Message}", ex);
		}

		_logger.LogInformation(
			"""
                [CS2 Surf] {PluginName} plugin loaded. Version: {ModuleVersion}
                [CS2 Surf] This plugin is licensed under the GNU Affero General Public License v3.0. See LICENSE for more information. 
                Source code: https://github.com/z4lab/SurfTimer.Plugin
            """,
			Config.PluginName,
			ModuleVersion
		);

		// Map Start Hook
		RegisterListener<Listeners.OnMapStart>(OnMapStart);
		// Map End Hook
		RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
		// Tick listener
		RegisterListener<Listeners.OnTick>(OnTick);
		// Block map scripts' chat and bot kicks (see MapCommandFilter.cs)
		RegisterMapCommandFilter();
		// Hiding players / bots per viewer, and undoing maps that hide players (see Visibility.cs)
		RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
		AddTimer(0.5f, EnforcePlayerVisibility, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
		// Dropped weapons are removed at once (see ST-Player/Weapons.cs)
		RegisterListener<Listeners.OnEntityParentChanged>(OnWeaponParentChanged);
		// Trails of ranked players / staff / replay bots (see ST-Trails/Trails.cs)
		LoadTrailSettings();
		// Player chat: formatted lines, hidden commands, admin panel prompts (see ST-Chat/ChatProcessor.cs)
		RegisterChatProcessor();
		// Ranks shown in chat - also refreshed whenever points change
		AddTimer(300f, QueueServerRankRefresh, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
		// Timer bans that ran out: times shown again - now and every hour
		LiftExpiredBans();
		AddTimer(3600f, LiftExpiredBans, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
		// Popup menu clicks (custom HUD buttons) - only our own layout, only players with a menu open
		RegisterListener<Listeners.OnCustomHudClicked>((player, layout, buttonId) =>
		{
			if (player == null || !player.IsValid || !CustomHud.IsOwnLayout(layout))
				return;

			if (playerList.TryGetValue(player.UserId ?? 0, out var clicker))
				clicker.HUD.OnMenuClick(buttonId);
		});
		// The custom HUD layout comes from a Workshop addon, not the map, so precache it. The manifest
		// wants the compiled resource type (.vxml); the entity itself takes the source name (.xml).
		RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
		{
			if (Config.CustomHudEnabled)
				manifest.AddResource(Path.ChangeExtension(Config.CustomHudLayout, ".vxml"));
		});
		// Maps show their own center messages ("Stage 7") through hint/text entities - with the custom
		// HUD those would cover it, so they're removed as they spawn (map load and round restarts)
		RegisterListener<Listeners.OnEntitySpawned>(entity =>
		{
			if (!Config.CustomHudEnabled || !CustomHud.MapMessageEntities.Contains(entity.DesignerName))
				return;

			var handle = entity.EntityHandle;
			Server.NextFrame(() => CustomHud.RemoveMapMessageEntity(handle.Value));
		});

		// Zones aren't the map's triggers any more - they're checked every tick (ZoneTracker.cs)
	}
}
