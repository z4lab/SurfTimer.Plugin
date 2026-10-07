using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Admin panel - Server tab: server info, changing / restarting the map, replay bots and the live timer
/// settings (saved to timer_settings.json).
/// </summary>
public partial class SurfTimer
{
	private PanelPage AdminServerRoot() => new("Server", ctx =>
	{
		var uptime = DateTime.UtcNow - LoadedAt;
		int humans = playerList.Values.Count(p => p.Controller.IsValid && !p.Controller.IsBot);
		int tickRate = Server.TickInterval > 0 ? (int)Math.Round(1 / Server.TickInterval) : 0;
		int playing = CurrentMap?.ReplayManager?.Pool.Count(s => s.IsPlaying) ?? 0;
		int slots = CurrentMap?.ReplayManager?.Pool.Count ?? 0;

		var rows = new List<HudMenuItem>
		{
			PanelContext.Info("SurfTimer", BuildInfo.Commit != null ? $"{ModuleVersion} · {BuildInfo.Commit}" : ModuleVersion,
				BuildInfo.BuildDate != null
					? $"built {BuildInfo.BuildDate} UTC · up {AdminFormat.Duration((long)uptime.TotalSeconds)}"
					: $"up {AdminFormat.Duration((long)uptime.TotalSeconds)}"),
			PanelContext.Info("Players", $"{humans} / {Server.MaxPlayers}", $"tick {tickRate}"),
			PanelContext.Info("Memory", AdminFormat.Bytes(GC.GetTotalMemory(false)), "managed heap"),
			AdminPermissions.Has(ctx.Player.Controller, AdminPermissions.ChangeMap)
				? ctx.Nav("Change map", CurrentMap?.Name ?? "", "recent maps, workshop id", AdminChangeMapPage)
				: PanelContext.Info("Change map", CurrentMap?.Name ?? "", "needs @css/changemap"),
			ctx.Nav("Replay bots", $"{playing} / {slots} playing", "", AdminBotsPage),
			ctx.Nav("Timer settings", "", "saved to timer_settings.json", AdminTimerSettingsPage),
			ctx.Nav("Chat", ChatSettings.Current.Enabled ? "on" : "off", "format, colors, anti-spam", AdminChatPage),
			ctx.Nav("Trails", TrailSettings.Current.Enabled ? "on" : "off", "look and colors per group", AdminTrailsPage),
		};

		if (CurrentMap != null)
		{
			string name = CurrentMap.Name!;
			ulong? workshopId = CurrentMap.WorkshopId;
			rows.Add(ctx.Danger("Restart map", name, () => PanelContext.Confirm("Restart map",
				_ => [PanelContext.Info("Reloads", name, "everyone's running times are lost")], "Restart", c =>
				{
					c.Audit("restart map", "map", CurrentMap.ID, name);
					c.Player.HUD.CloseMenu();
					RestartCurrentMap();
				})));
		}
		return rows;
	});

	// ---- Map change (also used by !changemap) ----

	/// <summary>
	/// Loads the current map again (by workshop id when known, else by name from the collection).
	/// </summary>
	internal void RestartCurrentMap()
	{
		if (CurrentMap?.Name == null)
			return;

		if (CurrentMap.WorkshopId is ulong workshopId)
			ChangeToWorkshopMap(workshopId, CurrentMap.Name);
		else
			ChangeLevelTo(CurrentMap.Name);
	}

	private static readonly Regex MapNamePattern = new("^[A-Za-z0-9_\\-]+$", RegexOptions.Compiled);

	internal static bool IsValidMapName(string mapName) => MapNamePattern.IsMatch(mapName);

	/// <summary>
	/// Changes to a map by name: by its workshop id when it was played before (works even outside the
	/// collection), else from the server's workshop collection (host_workshop_collection). A change that
	/// doesn't happen is reported - the map isn't available.
	/// </summary>
	internal void ChangeLevelTo(string mapName)
	{
		// The name goes into a server command, so nothing like "surf_x; quit" gets through
		if (!IsValidMapName(mapName))
			return;

		// A map of the server's workshop addons - loaded by its workshop id, no lookup needed
		if (WorkshopIdOf(mapName) is ulong listedId)
		{
			ChangeToWorkshopMap(listedId, mapName);
			return;
		}

		Task.Run(async () =>
		{
			ulong? workshopId = null;
			try
			{
				workshopId = (await MapRepository.GetByNameAsync(mapName))?.WorkshopId;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[{ClassName}] Looking up map '{Map}' failed - changing by name", nameof(SurfTimer), mapName);
			}

			Server.NextFrame(() =>
			{
				if (workshopId is ulong id && id > 0)
				{
					ChangeToWorkshopMap(id, mapName);
					return;
				}

				Server.PrintToChatAll($"{Config.PluginPrefix} Changing map to {ChatColors.Green}{mapName}{ChatColors.Default}...");
				// A moment for the chat message to arrive
				AddTimer(2.0f, () => Server.ExecuteCommand($"ds_workshop_changelevel {mapName}"));
				ReportIfMapUnchanged(mapName, MapChangeTimeoutSeconds, "isn't in the workshop collection");
			});
		});
	}

	/// <summary>
	/// Changes to any workshop map by its id.
	/// </summary>
	internal void ChangeToWorkshopMap(ulong workshopId, string? label = null)
	{
		Server.PrintToChatAll($"{Config.PluginPrefix} Changing map to {ChatColors.Green}{label ?? workshopId.ToString()}{ChatColors.Default}...");
		AddTimer(2.0f, () => Server.ExecuteCommand($"host_workshop_map {workshopId}"));
		// A map not downloaded yet is downloaded first - more time before calling it failed
		ReportIfMapUnchanged(label ?? workshopId.ToString(), WorkshopChangeTimeoutSeconds, "couldn't be loaded (wrong workshop id or download failed)");
	}

	private const float MapChangeTimeoutSeconds = 12f;
	private const float WorkshopChangeTimeoutSeconds = 90f;

	/// <summary>
	/// Still on the same map after the timeout: the change didn't happen - say so instead of nothing.
	/// The timer dies with a map change.
	/// </summary>
	private void ReportIfMapUnchanged(string target, float seconds, string why)
	{
		string? current = CurrentMap?.Name;
		AddTimer(seconds, () =>
		{
			if (CurrentMap?.Name != current)
				return;
			Server.PrintToChatAll($"{Config.PluginPrefix} {ChatColors.Red}{target} {why}{ChatColors.Default} - staying on {current}.");
			_logger.LogWarning("[{ClassName}] Map change to '{Target}' didn't happen ({Why})", nameof(SurfTimer), target, why);
		}, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
	}

	/// <summary>
	/// A typed map name checked against the server's map list: the map it means, or an error with suggestions.
	/// Without a list every valid name passes.
	/// </summary>
	internal string? CheckMapName(string text, out string mapName)
	{
		mapName = text;
		if (MapList.Count == 0)
			return null;

		var resolved = ResolveMap(text);
		if (resolved != null)
		{
			mapName = resolved;
			return null;
		}

		var suggestions = SuggestMaps(text);
		return LocalizationService.LocalizerNonNull["map_not_on_server", text, suggestions.Count > 0 ? string.Join(", ", suggestions) : "-"];
	}

	private sealed class MapsData(List<MapRepository.MapRow> maps)
	{
		internal List<MapRepository.MapRow> Maps { get; } = maps;
	}

	private PanelPage AdminChangeMapPage() => new("Change map", ctx =>
	{
		if (!AdminPermissions.Has(ctx.Player.Controller, AdminPermissions.ChangeMap))
			return [PanelContext.Info("No access", "", "changing the map needs @css/changemap")];

		var rows = new List<HudMenuItem>
		{
			ctx.Ask("Map name or workshop id", "", "type in chat", LocalizationService.LocalizerNonNull["prompt_change_map"], text =>
			{
				if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id > 0)
				{
					ctx.Audit("change map", "workshop", (long)id, text);
					ChangeToWorkshopMap(id);
					return null;
				}
				if (!IsValidMapName(text))
					return "A map name (letters, digits, _ and -) or a workshop id";
				if (CheckMapName(text, out string mapName) is { } error)
					return error;
				ctx.Audit("change map", "map", null, mapName);
				ChangeLevelTo(mapName);
				return null;
			}),
		};

		// The server's map list (its workshop addons, MapList.cs) - every map it can load, merged with what was played
		string filter = ctx.Page.State.TryGetValue("filter", out var f) && f is string s ? s : "";
		rows.Add(ctx.Act("Refresh map list", MapList.Count > 0 ? $"{MapList.Count} maps" : "unknown",
			MapListUpdatedAt is DateTime updated ? $"read {AdminFormat.Ago(updated)} ago" : "workshop addons", () =>
			{
				RefreshMapList();
				ctx.Session.Status = "Reading the map list…";
				AddTimer(2f, () => PanelRefresh(ctx.Session));
			}));
		if (MapList.Count > 0)
		{
			rows.Add(ctx.Ask("Search map", filter.Length > 0 ? filter : "", "filters the list below", "Type part of a map name in chat", text =>
			{
				ctx.Page.State["filter"] = text.Trim();
				return null;
			}));
			if (filter.Length > 0)
				rows.Add(ctx.Act("Clear search", "", "", () => ctx.Page.State.Remove("filter")));
		}

		var recent = ctx.Load("maps", async () => new MapsData(await MapRepository.GetAllMapsAsync()));
		if (recent == null)
		{
			rows.Add(PanelContext.LoadingRow());
			return rows;
		}

		if (MapList.Count > 0)
		{
			var played = recent.Maps.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
			foreach (var listed in MapList.Where(m => filter.Length == 0 || m.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			{
				if (CurrentMap != null && listed.Equals(CurrentMap.Name, StringComparison.OrdinalIgnoreCase))
					continue;
				string name = listed;
				played.TryGetValue(name, out var row);
				string sub = row != null ? $"played {AdminFormat.Ago(row.LastPlayedAt)} ago{(row.Ranked ? " · ranked" : "")}" : "never played";
				rows.Add(ctx.Act(name, "", sub, () =>
				{
					ctx.Audit("change map", "map", row?.Id, name);
					ChangeLevelTo(name);
				}, closes: true));
			}
			return rows;
		}

		// No map list - the maps played before, newest first
		foreach (var map in recent.Maps.OrderByDescending(m => m.LastPlayedAt).Take(60).Where(m => m.Id != CurrentMap?.ID))
		{
			string name = map.Name;
			ulong? workshopId = map.WorkshopId;
			string sub = $"played {AdminFormat.Ago(map.LastPlayedAt)} ago{(map.Ranked ? " · ranked" : "")}";
			rows.Add(ctx.Act(name, "", sub, () =>
			{
				ctx.Audit("change map", "map", map.Id, name);
				if (workshopId != null)
					ChangeToWorkshopMap(workshopId.Value, name);
				else
					ChangeLevelTo(name);
			}, closes: true));
		}
		return rows;
	});

	// ---- Replay bots ----

	private PanelPage AdminBotsPage() => new("Replay bots", ctx =>
	{
		var pool = CurrentMap?.ReplayManager?.Pool;
		if (pool == null || pool.Count == 0)
			return [PanelContext.Info("No replay bots", "", "they spawn when a replay is requested")];

		var rows = pool.Select((slot, i) =>
		{
			string what = slot.Type == ReplayManager.BestSegmentsType
				? ReplayManager.BestSegmentsLabel
				: $"{CourseLabel(CourseKinds.FromRunType(slot.Type), (short)slot.Stage)} {(slot.RecordRank == 1 ? "WR" : "PB")}";
			string sub = slot.MapTimeID != -1 || slot.Type == ReplayManager.BestSegmentsType
				? $"{slot.RecordPlayerName} {AdminFormat.Time(slot.RecordRunTime)}"
				: "";

			if (!slot.IsPlaying)
				return PanelContext.Info($"Slot {i + 1}", slot.Controller == null ? "spawning" : "idle", "");

			return ctx.Act($"Slot {i + 1} · {what}", "playing", sub, () =>
			{
				slot.GoIdle();
				ctx.Session.Status = $"Slot {i + 1} stopped";
			});
		}).ToList();

		if (pool.Any(s => s.IsPlaying))
		{
			rows.Add(ctx.Act("Stop all replays", "", "bots leave after a moment", () =>
			{
				foreach (var slot in pool.Where(s => s.IsPlaying))
					slot.GoIdle();
				ctx.Audit("stop replays", "server", null, "all replay bots");
				ctx.Session.Status = "All replays stopped";
			}));
		}
		return rows;
	});

	// ---- Timer settings ----

	private static readonly Dictionary<string, (string Label, string Sub)> LiveSettingLabels = new()
	{
		["popup_menus"] = ("Popup menus", "clickable centre menus instead of chat menus"),
		["block_map_chat"] = ("Block map chat", "map scripts' say messages"),
		["protect_replay_bots"] = ("Protect replay bots", "block map bot kicks"),
		["replay_bot_direct_spawn"] = ("Direct bot spawn", "CreateBot instead of bot_quota"),
		["replays_enabled"] = ("Replays", "record and play replays"),
		["replay_permanent_map_bot"] = ("Permanent map replay bot", "loops the map WR, not counted in the max"),
		["clan_tags_enabled"] = ("Clan tags", "players' clan tags on the scoreboard"),
		["country_clan_tag"] = ("Country tag", "[DE] in front of the clan tag"),
	};

	private PanelPage AdminTimerSettingsPage() => new("Timer settings", ctx =>
	{
		var rows = Config.LiveBoolSettings.Select(key =>
		{
			var (label, sub) = LiveSettingLabels.GetValueOrDefault(key, (key, ""));
			return ctx.Toggle(label, Config.GetLiveBool(key), sub, on =>
			{
				// The popup can't stay open when popups are turned off
				if (key == "popup_menus" && !on)
					ctx.Player.HUD.CloseMenu();
				AdminSaveTimerSetting(ctx, key, JsonValue.Create(on), on ? "on" : "off");
				if (key == "replay_permanent_map_bot" || key == "replays_enabled")
					UpdatePermanentReplayBot(); // Spawns / removes the permanent map bot right away
			});
		}).ToList();

		rows.Add(ctx.Nav("Idle threshold", Config.IdleThresholdSeconds <= 0 ? "off" : $"{Config.IdleThresholdSeconds} s", "stops replay recording of idle players",
			() => AdminNumberSettingPage("Idle threshold", "idle_threshold_seconds", () => Config.IdleThresholdSeconds, [10, 60], 0, 3600, " s")));
		rows.Add(ctx.Nav("Replay bots max", Config.ReplayPoolCap.ToString(), "requested bots at a time",
			() => AdminNumberSettingPage("Replay bots max", "replay_pool_cap", () => Config.ReplayPoolCap, [1], 1, 10, "")));
		rows.Add(ctx.Nav("Saveloc limit", Config.SavelocLimit.ToString(), "savelocs per map, all players",
			() => AdminNumberSettingPage("Saveloc limit", "saveloc_limit", () => Config.SavelocLimit, [100, 1000], 1, 100000, "")));

		rows.Add(ctx.Nav("Start speed cap", Config.StartSpeedCap <= 0 ? "no cap" : $"{Config.StartSpeedCap} u/s", "default for all maps",
			() => AdminNumberSettingPage("Start speed cap", "start_speed_cap", () => Config.StartSpeedCap, [10, 50], 0, 10000, " u/s")));
		rows.Add(ctx.Nav("Exit speed limit", $"{Config.StartExitSpeedLimit} u/s", "default for maps that turn it on",
			() => AdminNumberSettingPage("Exit speed limit", "start_exit_speed_limit", () => Config.StartExitSpeedLimit, [10, 50], 100, 10000, " u/s")));
		rows.Add(ctx.Nav("Stage / CP WR points", Config.PointsSegmentWr.ToString(), "per segment WR",
			() => AdminNumberSettingPage("Stage / CP WR points", "points_segment_wr", () => Config.PointsSegmentWr, [1, 5], 0, 1000, "")));
		return rows;
	});

	private PanelPage AdminNumberSettingPage(string title, string key, Func<int> current, int[] steps, int min, int max, string unit) => new(title, ctx =>
	{
		int value = current();
		var rows = new List<HudMenuItem> { PanelContext.Info("Current", $"{value}{unit}", "timer_settings.json") };
		foreach (int step in steps)
		{
			rows.Add(ctx.Act($"+{step}", "", "", () => AdminSaveTimerSetting(ctx, key, JsonValue.Create(Math.Clamp(value + step, min, max)), "")));
			rows.Add(ctx.Act($"-{step}", "", "", () => AdminSaveTimerSetting(ctx, key, JsonValue.Create(Math.Clamp(value - step, min, max)), "")));
		}
		if (min == 0)
			rows.Add(ctx.Act("Off (0)", "", "", () => AdminSaveTimerSetting(ctx, key, JsonValue.Create(0), "")));
		return rows;
	});

	private void AdminSaveTimerSetting(PanelContext ctx, string key, JsonNode value, string label)
	{
		string text = label.Length > 0 ? label : value.ToJsonString();
		try
		{
			Config.SaveTimerSetting(key, value);
			ctx.Audit("timer setting", "server", null, $"{key} = {value.ToJsonString()}");
			ctx.Session.Status = $"{key} {text} - saved";
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[Admin] Saving timer setting {Key} failed", key);
			ctx.Session.Status = $"Saving {key} failed: {ex.Message}";
		}
	}
}
