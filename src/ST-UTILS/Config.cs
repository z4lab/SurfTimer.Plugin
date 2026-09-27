using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;

namespace SurfTimer;

public static class Config
{
	public static string PluginName => Assembly.GetExecutingAssembly().GetName().Name ?? "";
	public static readonly string PluginPrefix = LocalizationService.LocalizerNonNull["prefix"];
	public static string PluginPath =>
		$"{Server.GameDirectory}/csgo/addons/counterstrikesharp/plugins/{PluginName}/";

	/// <summary>
	/// Placeholder for amount of styles
	/// </summary>
	public static readonly ImmutableList<int> Styles = [0]; // Add all supported style IDs

	public static readonly bool ReplaysEnabled = TimerSettings.GetReplaysEnabled();
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
	public static readonly bool PopupMenus = TimerSettings.GetBool("popup_menus", true);

	/// <summary>
	/// Blocks chat the map sends through the server console (`say` from map scripts, e.g. ads).
	/// Note: also blocks `say` typed into the server console / RCON.
	/// </summary>
	public static readonly bool BlockMapChat = TimerSettings.GetBool("block_map_chat", true);

	/// <summary>
	/// Blocks bot kicks the plugin didn't ask for (`bot_kick`, `kick`/`kickid` on a bot) - maps that
	/// kick every bot would otherwise remove replay bots.
	/// </summary>
	public static readonly bool ProtectReplayBots = TimerSettings.GetBool("protect_replay_bots", true);

	/// <summary>
	/// Creates replay bots through the game's CreateBot function (works on maps without a nav mesh,
	/// where bot_add/bot_quota do nothing). Falls back to bot_quota when off or the signature breaks.
	/// </summary>
	public static readonly bool ReplayBotDirectSpawn = TimerSettings.GetBool("replay_bot_direct_spawn", true);

	/// <summary>
	/// Maximum number of distinct replays that can play concurrently via !replay.
	/// </summary>
	public const int ReplayPoolCap = 3;
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
		/// Retrieves the connection details for connecting to the MySQL Database
		/// </summary>
		/// <returns>A connection string</returns>
		public static string GetConnectionString()
		{
			string host = ConfigDocument.RootElement.GetProperty("host").GetString()!;
			string database = ConfigDocument.RootElement.GetProperty("database").GetString()!;
			string user = ConfigDocument.RootElement.GetProperty("user").GetString()!;
			string password = ConfigDocument.RootElement.GetProperty("password").GetString()!;
			int port = ConfigDocument.RootElement.GetProperty("port").GetInt32()!;
			int timeout = ConfigDocument.RootElement.GetProperty("timeout").GetInt32()!;

			string connString =
				$"Server={host};User={user};Password={password};Database={database};Port={port};Connect Timeout={timeout};Allow User Variables=true";

			return connString;
		}
	}
}
