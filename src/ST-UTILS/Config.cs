using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CounterStrikeSharp.API;

namespace SurfTimer;

public static class Config
{
	public static string PluginName => Assembly.GetExecutingAssembly().GetName().Name ?? "";
	/// <summary>
	/// Chat prefix: the admin's (!surfadmin - Server - Chat - Prefix, chat_settings.json), else the language file's.
	/// </summary>
	public static string PluginPrefix
	{
		get
		{
			string custom = ChatSettings.Current.Prefix;
			if (custom.Length == 0)
				return LocalizationService.LocalizerNonNull["prefix"];
			if (!ReferenceEquals(custom, _prefixSource))
			{
				_prefixSource = custom;
				_prefixColored = ChatSettings.Colorize(custom);
			}
			return _prefixColored;
		}
	}

	private static string? _prefixSource;
	private static string _prefixColored = "";
	public static string PluginPath =>
		$"{Server.GameDirectory}/csgo/addons/counterstrikesharp/plugins/{PluginName}/";

	/// <summary>
	/// Placeholder for amount of styles
	/// </summary>
	public static readonly ImmutableList<int> Styles = [0]; // Add all supported style IDs

	public static bool ReplaysEnabled { get; private set; } = TimerSettings.GetReplaysEnabled();
	public static readonly int ReplaysPre = TimerSettings.GetReplaysPre();

	/// <summary>
	/// Server-driven HUD via a custom_hud_layout entity - needs the SurfTimer HUD Workshop addon on clients.
	/// </summary>
	public static readonly bool CustomHudEnabled = TimerSettings.GetCustomHudEnabled();
	public static readonly string CustomHudLayout = TimerSettings.GetCustomHudLayout();

	/// <summary>
	/// Pickers (!replay, !spec) as the clickable centre popup of the custom HUD instead of chat menus.
	/// Needs an addon version that has the popup (st_menu) - with an older one the player would be put in
	/// cursor mode without a visible menu to close, so turn this off until the new addon is live.
	/// </summary>
	public static bool PopupMenus { get; private set; } = TimerSettings.GetBool("popup_menus", true);

	/// <summary>
	/// CS2's round timer (top centre) stands still at 13:37 - a round time that runs out kicks everyone as idle
	/// (RoundTimer.cs).
	/// </summary>
	public static bool RoundTimerFreeze { get; private set; } = TimerSettings.GetBool("round_timer_freeze", true);

	/// <summary>
	/// The CS2 sound event of a kind of sound (Sounds.cs) - timer_settings.json "sound_&lt;key&gt;" overrides the default,
	/// "" turns it off for everyone.
	/// </summary>
	internal static string SoundEvent(string key, string fallback) => TimerSettings.GetString($"sound_{key}", fallback);

	/// <summary>
	/// Points per stage / checkpoint segment WR (CS:GO SurfTimer's ck_wrcp_points, 0 = none).
	/// </summary>
	public static int PointsSegmentWr { get; private set; } = TimerSettings.GetInt("points_segment_wr", 0);

	/// <summary>
	/// Blocks chat the map sends through the server console (`say` from map scripts, e.g. ads).
	/// Note: also blocks `say` typed into the server console / RCON.
	/// </summary>
	public static bool BlockMapChat { get; private set; } = TimerSettings.GetBool("block_map_chat", true);

	/// <summary>
	/// Blocks bot kicks the plugin didn't ask for (`bot_kick`, `kick`/`kickid` on a bot) - maps that
	/// kick every bot would otherwise remove replay bots.
	/// </summary>
	public static bool ProtectReplayBots { get; private set; } = TimerSettings.GetBool("protect_replay_bots", true);

	/// <summary>
	/// Creates replay bots through the game's CreateBot function (works on maps without a nav mesh,
	/// where bot_add/bot_quota do nothing). Falls back to bot_quota when off or the signature breaks.
	/// </summary>
	public static bool ReplayBotDirectSpawn { get; private set; } = TimerSettings.GetBool("replay_bot_direct_spawn", true);

	/// <summary>
	/// Default airborne speed cap for bhops inside start zones (u/s, 0 = no cap) - maps can override it
	/// with their start_speed_cap setting.
	/// </summary>
	public static int StartSpeedCap { get; private set; } = TimerSettings.GetInt("start_speed_cap", 260);

	/// <summary>
	/// Default hard limit (u/s, horizontal) when leaving a run start - only on maps that turn the limit on
	/// (exit_speed_limit map setting), which can also set their own value.
	/// </summary>
	public static int StartExitSpeedLimit { get; private set; } = TimerSettings.GetInt("start_exit_speed_limit", 600);

	/// <summary>
	/// Timer settings the !surfadmin panel can change live - written back to timer_settings.json.
	/// </summary>
	internal static readonly IReadOnlyList<string> LiveBoolSettings =
		["popup_menus", "round_timer_freeze", "block_map_chat", "protect_replay_bots", "replay_bot_direct_spawn", "replays_enabled", "replay_permanent_map_bot", "clan_tags_enabled", "country_clan_tag"];

	internal static bool GetLiveBool(string key) => key switch
	{
		"popup_menus" => PopupMenus,
		"round_timer_freeze" => RoundTimerFreeze,
		"block_map_chat" => BlockMapChat,
		"protect_replay_bots" => ProtectReplayBots,
		"replay_bot_direct_spawn" => ReplayBotDirectSpawn,
		"replays_enabled" => ReplaysEnabled,
		"replay_permanent_map_bot" => ReplayPermanentMapBot,
		"clan_tags_enabled" => ClanTagsEnabled,
		"country_clan_tag" => CountryClanTag,
		_ => false,
	};

	/// <summary>
	/// Changes a timer setting live and saves it to timer_settings.json (other keys and values are kept).
	/// </summary>
	internal static void SaveTimerSetting(string key, JsonNode value)
	{
		TimerSettings.Write(key, value);
		ReloadLiveSettings();
	}

	private static void ReloadLiveSettings()
	{
		ReplaysEnabled = TimerSettings.GetReplaysEnabled();
		PopupMenus = TimerSettings.GetBool("popup_menus", true);
		RoundTimerFreeze = TimerSettings.GetBool("round_timer_freeze", true);
		PointsSegmentWr = TimerSettings.GetInt("points_segment_wr", 0);
		BlockMapChat = TimerSettings.GetBool("block_map_chat", true);
		ProtectReplayBots = TimerSettings.GetBool("protect_replay_bots", true);
		ReplayBotDirectSpawn = TimerSettings.GetBool("replay_bot_direct_spawn", true);
		StartSpeedCap = TimerSettings.GetInt("start_speed_cap", 260);
		StartExitSpeedLimit = TimerSettings.GetInt("start_exit_speed_limit", 600);
		ReplayPoolCap = Math.Clamp(TimerSettings.GetInt("replay_pool_cap", 5), 1, 10);
		ReplayPermanentMapBot = TimerSettings.GetBool("replay_permanent_map_bot", false);
		IdleThresholdSeconds = Math.Max(0, TimerSettings.GetInt("idle_threshold_seconds", 60));
		CenterSpeedFont = TimerSettings.GetString("center_speed_font", "Consolas");
		ResumeExpiryDays = Math.Clamp(TimerSettings.GetInt("resume_expiry_days", 3), 0, 365);
		ResumeExpiryVipDays = Math.Clamp(TimerSettings.GetInt("resume_expiry_vip_days", 7), 0, 365);
		SavelocLimit = Math.Max(1, TimerSettings.GetInt("saveloc_limit", 10000));
		WorkshopContentPath = TimerSettings.GetString("workshop_content_path", "").Trim();
		ClanTagsEnabled = TimerSettings.GetBool("clan_tags_enabled", true);
		CountryClanTag = TimerSettings.GetBool("country_clan_tag", true);
	}

	/// <summary>
	/// The player's country as a tag in front of their clan tag on the scoreboard: [DE][z4lab], or just
	/// [DE] with clan tags off.
	/// </summary>
	public static bool CountryClanTag { get; private set; } = TimerSettings.GetBool("country_clan_tag", true);

	/// <summary>
	/// Players' clan tags on the scoreboard - off clears them (and puts them back when turned on again).
	/// </summary>
	public static bool ClanTagsEnabled { get; private set; } = TimerSettings.GetBool("clan_tags_enabled", true);

	/// <summary>
	/// Seconds without movement, input or looking around after which a player's replay recording is
	/// stopped and freed (0 = off). The timer keeps running.
	/// </summary>
	/// <summary>Savelocs per map session (all players) - new ones are refused beyond it</summary>
	/// <summary>
	/// Where the server downloads workshop items (steamapps/workshop/content/730) - only needed when it isn't
	/// in one of the usual places under game/bin. Empty = search the usual places (MapList.cs).
	/// </summary>
	public static string WorkshopContentPath { get; private set; } = TimerSettings.GetString("workshop_content_path", "").Trim();

	public static int SavelocLimit { get; private set; } = Math.Max(1, TimerSettings.GetInt("saveloc_limit", 10000));

	public static int IdleThresholdSeconds { get; private set; } = Math.Max(0, TimerSettings.GetInt("idle_threshold_seconds", 60));

	/// <summary>
	/// Font of the centre speed (Options - HUD - Center speed) - a monospace font the players' PCs have, so the number
	/// doesn't jump around as its digits change.
	/// </summary>
	public static string CenterSpeedFont { get; private set; } = TimerSettings.GetString("center_speed_font", "Consolas");

	/// <summary>
	/// Days a saved in-progress run can be resumed after the player left (PlayerStateService) - VIPs get their own,
	/// 0 = never expires.
	/// </summary>
	public static int ResumeExpiryDays { get; private set; } = Math.Clamp(TimerSettings.GetInt("resume_expiry_days", 3), 0, 365);
	public static int ResumeExpiryVipDays { get; private set; } = Math.Clamp(TimerSettings.GetInt("resume_expiry_vip_days", 7), 0, 365);

	/// <summary>
	/// Maximum number of requested replay bots at a time (!replay) - the permanent map bot isn't counted.
	/// </summary>
	public static int ReplayPoolCap { get; private set; } = Math.Clamp(TimerSettings.GetInt("replay_pool_cap", 5), 1, 10);

	/// <summary>
	/// An always-on bot looping the map WR (not counted in ReplayPoolCap).
	/// </summary>
	public static bool ReplayPermanentMapBot { get; private set; } = TimerSettings.GetBool("replay_permanent_map_bot", false);

	/// <summary>
	/// Seconds a requested replay bot may go without any spectator before it's removed.
	/// </summary>
	public const int ReplayUnwatchedTimeoutSeconds = 10;
	/// <summary>
	/// How many times a replay bot repeats its replay before going idle.
	/// </summary>
	public const int ReplayRepeatCount = 3;
	/// <summary>
	/// Seconds an idle (finished, unclaimed) replay bot waits before being kicked.
	/// </summary>
	public const int ReplayIdleTimeoutSeconds = 2;

	// Helper class/methods for configuration loading
	private static class ConfigLoader
	{
		private static readonly Dictionary<string, JsonDocument> _configDocuments = new();

		public static JsonDocument GetConfigDocument(string configPath)
		{
			if (!_configDocuments.ContainsKey(configPath))
			{
				var fullPath = Server.GameDirectory + configPath;
				_configDocuments[configPath] = JsonDocument.Parse(File.ReadAllText(fullPath));
			}
			return _configDocuments[configPath];
		}

		/// <summary>
		/// Sets one key of a JSON config file (written to a temp file first, then swapped in) and drops
		/// the cached document so the next read sees it.
		/// </summary>
		public static void WriteKey(string configPath, string key, JsonNode value)
		{
			var fullPath = Server.GameDirectory + configPath;
			var root = JsonNode.Parse(File.ReadAllText(fullPath)) as JsonObject
				?? throw new InvalidOperationException($"{configPath} is not a JSON object");
			root[key] = value;

			string temp = fullPath + ".tmp";
			File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
			File.Move(temp, fullPath, overwrite: true);

			if (_configDocuments.Remove(configPath, out var old))
				old.Dispose();
		}
	}

	/// <summary>
	/// Values from `timer_settings.json`
	/// </summary>
	private static class TimerSettings
	{
		private const string TIMER_CONFIG_PATH = "/csgo/cfg/SurfTimer/timer_settings.json";
		private static JsonDocument ConfigDocument =>
			ConfigLoader.GetConfigDocument(TIMER_CONFIG_PATH);

		public static bool GetReplaysEnabled()
		{
			return ConfigDocument.RootElement.GetProperty("replays_enabled").GetBoolean();
		}

		public static void Write(string key, JsonNode value) => ConfigLoader.WriteKey(TIMER_CONFIG_PATH, key, value);

		public static int GetReplaysPre()
		{
			return ConfigDocument.RootElement.GetProperty("replays_pre").GetInt32();
		}

		// Optional keys - older configs without them keep the classic center HTML HUD
		public static bool GetCustomHudEnabled()
		{
			return ConfigDocument.RootElement.TryGetProperty("custom_hud_enabled", out var value) && value.GetBoolean();
		}

		public static bool GetBool(string key, bool defaultValue)
		{
			return ConfigDocument.RootElement.TryGetProperty(key, out var value)
				&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: defaultValue;
		}

		public static int GetInt(string key, int defaultValue)
		{
			return ConfigDocument.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
				&& value.TryGetInt32(out int number)
				? number
				: defaultValue;
		}

		public static string GetString(string key, string defaultValue)
		{
			return ConfigDocument.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
				? value.GetString()!
				: defaultValue;
		}

		public static string GetCustomHudLayout()
		{
			return ConfigDocument.RootElement.TryGetProperty("custom_hud_layout", out var value)
				? value.GetString()!
				: "panorama/layout/custom_game/surftimer_hud.xml"; // Source name - clients reject ".vxml" here
		}
	}

	public static class MySql
	{
		private const string DB_CONFIG_PATH = "/csgo/cfg/SurfTimer/database.json";
		private static JsonDocument ConfigDocument =>
			ConfigLoader.GetConfigDocument(DB_CONFIG_PATH);

		/// <summary>
		/// Connection settings for the MariaDB / MySQL database.
		/// </summary>
		internal static DatabaseSettings GetSettings()
		{
			var root = ConfigDocument.RootElement;
			string String(string key, string fallback = "") =>
				root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : fallback;
			uint Number(string key, uint fallback) =>
				root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint number) ? number : fallback;

			string prefix = String("table_prefix");
			// Goes into every table name - only letters, digits and underscores
			if (!System.Text.RegularExpressions.Regex.IsMatch(prefix, "^[A-Za-z0-9_]*$"))
				throw new InvalidOperationException($"database.json: table_prefix '{prefix}' may only contain letters, digits and '_'");

			uint timeout = Number("timeout", 10);
			return new DatabaseSettings(
				Host: String("host"),
				Port: Number("port", 3306),
				Database: String("database"),
				User: String("user"),
				Password: String("password"),
				ConnectTimeoutSeconds: timeout == 0 ? 10 : timeout,
				TablePrefix: prefix,
				MaxPoolSize: Number("max_pool_size", 20));
		}
	}
}
