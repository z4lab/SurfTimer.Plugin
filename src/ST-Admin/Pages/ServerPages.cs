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
			PanelContext.Info("SurfTimer", ModuleVersion, $"up {AdminFormat.Duration((long)uptime.TotalSeconds)}"),
			PanelContext.Info("Players", $"{humans} / {Server.MaxPlayers}", $"tick {tickRate}"),
			PanelContext.Info("Memory", AdminFormat.Bytes(GC.GetTotalMemory(false)), "managed heap"),
			ctx.Nav("Change map", CurrentMap?.Name ?? "", "recent maps, workshop id", AdminChangeMapPage),
			ctx.Nav("Replay bots", $"{playing} / {slots} playing", "", AdminBotsPage),
			ctx.Nav("Timer settings", "", "saved to timer_settings.json", AdminTimerSettingsPage),
			ctx.Nav("Chat", ChatSettings.Current.Enabled ? "on" : "off", "format, colors, anti-spam", AdminChatPage),
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
					if (workshopId != null)
						ChangeToWorkshopMap(workshopId.Value, name);
					else
						ChangeLevelTo(name);
				})));
		}
		return rows;
	});

	// ---- Map change (also used by !map) ----

	private static readonly Regex MapNamePattern = new("^[A-Za-z0-9_\\-]+$", RegexOptions.Compiled);

	internal static bool IsValidMapName(string mapName) => MapNamePattern.IsMatch(mapName);

	/// <summary>
	/// Changes to a map of the server's workshop collection (host_workshop_collection) by name.
	/// </summary>
	internal void ChangeLevelTo(string mapName)
	{
		// The name goes into a server command, so nothing like "surf_x; quit" gets through
		if (!IsValidMapName(mapName))
			return;

		Server.PrintToChatAll($"{Config.PluginPrefix} Changing map to {ChatColors.Green}{mapName}{ChatColors.Default}...");
		// A moment for the chat message to arrive
		AddTimer(2.0f, () => Server.ExecuteCommand($"ds_workshop_changelevel {mapName}"));
	}

	/// <summary>
	/// Changes to any workshop map by its id.
	/// </summary>
	internal void ChangeToWorkshopMap(ulong workshopId, string? label = null)
	{
		Server.PrintToChatAll($"{Config.PluginPrefix} Changing map to {ChatColors.Green}{label ?? workshopId.ToString()}{ChatColors.Default}...");
		AddTimer(2.0f, () => Server.ExecuteCommand($"host_workshop_map {workshopId}"));
	}

	private sealed class MapsData(List<MapRepository.MapRow> maps)
	{
		internal List<MapRepository.MapRow> Maps { get; } = maps;
	}

	private PanelPage AdminChangeMapPage() => new("Change map", ctx =>
	{
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
				ctx.Audit("change map", "map", null, text);
				ChangeLevelTo(text);
				return null;
			}),
		};

		var recent = ctx.Load("maps", async () => new MapsData(await MapRepository.GetRecentMapsAsync(60)));
		if (recent == null)
		{
			rows.Add(PanelContext.LoadingRow());
			return rows;
		}

		foreach (var map in recent.Maps.Where(m => m.Id != CurrentMap?.ID))
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
