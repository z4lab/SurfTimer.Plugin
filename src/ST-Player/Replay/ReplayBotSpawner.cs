using System.Runtime.InteropServices;
using System.Text.Json;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Creates replay bots by calling the game's internal CreateBot function directly (the approach of
/// cs2kz-metamod / cs2surf-metamod). bot_add / bot_quota go through the bot manager, which refuses
/// maps without a navigation mesh - CreateBot is what those paths call after their checks, so it
/// works on every map.
///
/// The signature lives in the plugin's data/gamedata.json so a CS2 update only needs new bytes there
/// (find it via the string "Unable to create bot: CreateFakeClient() returned null." in server.dll /
/// libserver.so, or copy it from cs2kz-metamod's gamedata/cs2kz-core.games.txt "CreateBot").
/// </summary>
internal static class ReplayBotSpawner
{
	// Defaults - current since the CS2 update of 2026-05-15 (cs2kz-metamod master, 2026-09-11)
	private const string DefaultWindowsSignature = "48 89 5C 24 10 48 89 6C 24 18 57 48 81 EC C0 00 00 00 41 0F B6 D8";
	private const string DefaultLinuxSignature = "55 48 89 E5 41 57 41 56 41 55 49 89 FD 41 54 41 89 D4 53 48 81 EC C8 00 00 00";

	private const int TeamT = 2;

	// BotProfile, as the game reads it (default x64 layout, see cs2kz-metamod src/utils/interfaces.h):
	// char* botName at 0, u16 weaponPreferences[16] at 36, int teamNum at 96, ~160 bytes in total.
	// Everything else stays 0.
	private const int BotProfileSize = 256; // Rounded up - only zeroes past the real struct
	private const int BotNameOffset = 0;
	private const int WeaponPreferencesOffset = 36;
	private const int TeamNumOffset = 96;

	// CCSPlayerController* CreateBot(BotProfile* profile, int team, bool isNotFromConsole)
	private static MemoryFunctionWithReturn<IntPtr, int, bool, IntPtr>? _createBot;
	private static bool _resolved;

	// Allocated once and never freed: bots keep a pointer to their profile, so freeing it while one
	// exists could crash the server (256 bytes per plugin load)
	private static IntPtr _profile;

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ReplayBotSpawner");

	/// <summary>
	/// Creates a bot directly. Main thread only. Returns null when direct creation is off, the
	/// signature isn't found or the call fails - the caller's bot_quota fallback still applies then.
	/// </summary>
	internal static CCSPlayerController? TryCreate()
	{
		if (!Config.ReplayBotDirectSpawn || !Resolve())
			return null;

		try
		{
			IntPtr pointer = _createBot!.Invoke(GetProfile(), TeamT, true);
			if (pointer == IntPtr.Zero)
			{
				Logger.LogWarning("[ReplayBotSpawner] CreateBot returned no bot");
				return null;
			}

			var bot = new CCSPlayerController(pointer);
			Logger.LogInformation("[ReplayBotSpawner] CreateBot created bot #{Slot}", bot.IsValid ? bot.Slot : -1);
			return bot.IsValid ? bot : null;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "[ReplayBotSpawner] CreateBot failed - falling back to bot_quota");
			return null;
		}
	}

	private static bool Resolve()
	{
		if (_resolved)
			return _createBot != null;
		_resolved = true;

		string signature = LoadSignature();
		try
		{
			var function = new MemoryFunctionWithReturn<IntPtr, int, bool, IntPtr>(signature);
			if (function.Handle == IntPtr.Zero)
			{
				Logger.LogError("[ReplayBotSpawner] CreateBot signature not found ({Signature}) - falling back to bot_quota. Update \"CreateBot\" in data/gamedata.json",
					signature);
				return false;
			}
			_createBot = function;
			return true;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "[ReplayBotSpawner] CreateBot signature could not be resolved - falling back to bot_quota");
			return false;
		}
	}

	/// <summary>
	/// data/gamedata.json: { "CreateBot": { "windows": "...", "linux": "..." } } - built-in defaults
	/// when the file or entry is missing.
	/// </summary>
	private static string LoadSignature()
	{
		string platform = OperatingSystem.IsLinux() ? "linux" : "windows";
		string fallback = OperatingSystem.IsLinux() ? DefaultLinuxSignature : DefaultWindowsSignature;
		string path = Config.PluginPath + "data/gamedata.json";

		try
		{
			if (File.Exists(path))
			{
				using var document = JsonDocument.Parse(File.ReadAllText(path));
				if (document.RootElement.TryGetProperty("CreateBot", out var entry)
					&& entry.TryGetProperty(platform, out var signature)
					&& !string.IsNullOrWhiteSpace(signature.GetString()))
					return signature.GetString()!;
			}
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "[ReplayBotSpawner] Could not read {Path} - using the built-in CreateBot signature", path);
		}

		return fallback;
	}

	private static IntPtr GetProfile()
	{
		if (_profile != IntPtr.Zero)
			return _profile;

		IntPtr profile = Marshal.AllocHGlobal(BotProfileSize);
		Marshal.Copy(new byte[BotProfileSize], 0, profile, BotProfileSize);

		Marshal.WriteIntPtr(profile, BotNameOffset, Marshal.StringToHGlobalAnsi("")); // Named after spawn
		Marshal.WriteInt16(profile, WeaponPreferencesOffset, unchecked((short)0xFFFF));
		Marshal.WriteInt32(profile, TeamNumOffset, TeamT);

		_profile = profile;
		return profile;
	}
}
