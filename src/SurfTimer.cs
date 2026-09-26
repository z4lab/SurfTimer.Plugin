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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurfTimer.Data;
using SurfTimer.Shared.Data;
using SurfTimer.Shared.Data.MySql;

namespace SurfTimer;

// Gameplan: https://github.com/z4lab/SurfTimer.Plugin/tree/dev/README.md
[MinimumApiVersion(337)]
public partial class SurfTimer : BasePlugin
{
	private readonly ILogger<SurfTimer> _logger;
	public static IServiceProvider ServiceProvider { get; private set; } = null!;
	private readonly IDataAccessService? _dataService;

	// Inject ILogger and store IServiceProvider globally
	public SurfTimer(ILogger<SurfTimer> logger, IServiceProvider serviceProvider)
	{
		_logger = logger;
		ServiceProvider = serviceProvider;
		_dataService = ServiceProvider.GetRequiredService<IDataAccessService>();
	}

	// Metadata
	public override string ModuleName => $"z4lab/{Config.PluginName}";
	public override string ModuleVersion => "1.0.1";
	public override string ModuleDescription => Config.PluginName;
	public override string ModuleAuthor => "z4lab";

	// Globals
	private readonly ConcurrentDictionary<int, Player> playerList = new();
	internal static IDatabaseService DB { get; private set; } = null!;
	public static Map CurrentMap { get; private set; } = null!;

	/* ========== MAP START HOOKS ========== */
	public void OnMapStart(string mapName)
	{
		// Initialise Map Object
		if ((CurrentMap == null || CurrentMap.Name!.Equals(mapName)) && mapName.Contains("surf_"))
		{
			_logger.LogInformation($"[CS2 Surf] {Config.PluginName} Initializing Map object for {mapName}");

			Server.NextWorldUpdateAsync(async () => // NextWorldUpdate runs even during server hibernation
			{
				_logger.LogInformation($"[CS2 Surf] {Config.PluginName} {ModuleVersion} - loading map {mapName}");
				CurrentMap = new Map(mapName);
				await CurrentMap.InitializeAsync();
			});
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

		// Round restarts re-create the map's triggers without firing EndTouch for the old ones, so any
		// "inside zone" state would otherwise stay stuck (e.g. anti-prehop applying outside start zones)
		foreach (var player in playerList.Values)
			player.TouchingTriggers.Clear();

		Server.ExecuteCommand("execifexists SurfTimer/server_settings.cfg");
		_logger.LogTrace(
			"[{Prefix}] Executed configuration: server_settings.cfg",
			Config.PluginName
		);
		return HookResult.Continue;
	}

	/* ========== PLUGIN LOAD ========== */
	public override void Load(bool hotReload)
	{
		LocalizationService.Init(Localizer);

		// === Dapper bootstrap (snake_case mapping + type handlers) + DB init ===
		DapperBootstrapper.Init();
		var connString = Config.MySql.GetConnectionString();
		var factory = new MySqlConnectionStringFactory(connString);
		DB = new DapperDatabaseService(factory);

		bool accessService = false;

		try
		{
			accessService = Task.Run(() => _dataService!.PingAccessService())
				.GetAwaiter()
				.GetResult();
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"[{Prefix}] PingAccessService threw an exception.",
				Config.PluginName
			);
		}

		if (accessService)
		{
			_logger.LogInformation(
				"[{Prefix}] DB connection established.",
				Config.PluginName
			);
		}
		else
		{
			_logger.LogCritical(
				"[{Prefix}] Error connecting to the DB.",
				Config.PluginName
			);

			Exception exception = new(
				$"[{Config.PluginName}] Error connecting to the DB"
			);
			throw exception;
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

		HookEntityOutput("trigger_multiple", "OnStartTouch", OnTriggerStartTouch);
		HookEntityOutput("trigger_multiple", "OnEndTouch", OnTriggerEndTouch);
	}
}
