using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// cfg/SurfTimer/chat_settings.json - the chat processor's format, colors, role flags and anti-spam.
/// Created with the defaults when missing; changed through !admin (Server - Chat).
/// Format placeholders: {rank} (colored [#n] / [-]), {ranknum}, {points}, {name} (colored), {message},
/// {country} (ISO code, unknown_country if unknown), {team} (CT / T / SPEC), {prefix} (team / spec /
/// dead tags) and color names like {grey}.
/// </summary>
internal sealed class ChatSettings
{
	private const string ConfigPath = "/csgo/cfg/SurfTimer/chat_settings.json";
	internal const string DefaultFormat = "{rank} ~ {name}: {message}";

	[JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
	[JsonPropertyName("format")] public string Format { get; set; } = DefaultFormat;
	[JsonPropertyName("team_prefix")] public string TeamPrefix { get; set; } = "(Team)";
	[JsonPropertyName("spectator_prefix")] public string SpectatorPrefix { get; set; } = "*SPEC*";
	[JsonPropertyName("dead_prefix")] public string DeadPrefix { get; set; } = "*DEAD*";
	/// <summary>{country} for players without a known country (GeoIP failed / local network)</summary>
	[JsonPropertyName("unknown_country")] public string UnknownCountry { get; set; } = "--";
	[JsonPropertyName("rank_colors")] public RankColorSettings RankColors { get; set; } = new();
	[JsonPropertyName("name_colors")] public NameColorSettings NameColors { get; set; } = new();
	[JsonPropertyName("flags")] public FlagSettings Flags { get; set; } = new();
	[JsonPropertyName("antispam")] public AntiSpamSettings AntiSpam { get; set; } = new();

	internal sealed class RankColorSettings
	{
		[JsonPropertyName("1")] public string First { get; set; } = "gold";
		[JsonPropertyName("2")] public string Second { get; set; } = "silver";
		[JsonPropertyName("3")] public string Third { get; set; } = "orange";
		[JsonPropertyName("top10")] public string Top10 { get; set; } = "lightblue";
		[JsonPropertyName("other")] public string Other { get; set; } = "grey";
		[JsonPropertyName("unranked")] public string Unranked { get; set; } = "grey";
	}

	internal sealed class NameColorSettings
	{
		[JsonPropertyName("root")] public string Root { get; set; } = "red";
		[JsonPropertyName("admin")] public string Admin { get; set; } = "purple";
		[JsonPropertyName("vip")] public string Vip { get; set; } = "lime";
		[JsonPropertyName("default")] public string Default { get; set; } = "team";
	}

	internal sealed class FlagSettings
	{
		[JsonPropertyName("root")] public string Root { get; set; } = "@css/root";
		[JsonPropertyName("admin")] public string Admin { get; set; } = "@css/generic";
		[JsonPropertyName("vip")] public string Vip { get; set; } = "@css/reservation";
	}

	internal sealed class AntiSpamSettings
	{
		[JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
		[JsonPropertyName("min_gap_seconds")] public double MinGapSeconds { get; set; } = 1.0;
		[JsonPropertyName("duplicate_window_seconds")] public double DuplicateWindowSeconds { get; set; } = 10;
	}

	// ---- Colors ----

	/// <summary>Color names usable in the settings / format, and "team" for the player's team color</summary>
	internal static readonly IReadOnlyDictionary<string, char> Colors = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
	{
		["default"] = ChatColors.Default,
		["white"] = ChatColors.White,
		["red"] = ChatColors.Red,
		["darkred"] = ChatColors.DarkRed,
		["lightred"] = ChatColors.LightRed,
		["green"] = ChatColors.Green,
		["lime"] = ChatColors.Lime,
		["olive"] = ChatColors.Olive,
		["yellow"] = ChatColors.Yellow,
		["lightyellow"] = ChatColors.LightYellow,
		["gold"] = ChatColors.Gold,
		["orange"] = ChatColors.Orange,
		["silver"] = ChatColors.Silver,
		["grey"] = ChatColors.Grey,
		["bluegrey"] = ChatColors.BlueGrey,
		["lightblue"] = ChatColors.LightBlue,
		["blue"] = ChatColors.Blue,
		["darkblue"] = ChatColors.DarkBlue,
		["purple"] = ChatColors.Purple,
		["lightpurple"] = ChatColors.LightPurple,
		["magenta"] = ChatColors.Magenta,
	};

	internal const string TeamColor = "team";

	/// <summary>A color name as a chat color - unknown names fall back to the default color</summary>
	internal static char Color(string name, CsTeam team = CsTeam.None) =>
		name.Equals(TeamColor, StringComparison.OrdinalIgnoreCase) ? ChatColors.ForTeam(team)
		: Colors.TryGetValue(name, out char color) ? color
		: ChatColors.Default;

	internal static bool IsColor(string name, bool allowTeam) =>
		Colors.ContainsKey(name) || (allowTeam && name.Equals(TeamColor, StringComparison.OrdinalIgnoreCase));

	/// <summary>A format must show who wrote what</summary>
	internal static bool IsValidFormat(string format) =>
		format.Contains("{name}", StringComparison.OrdinalIgnoreCase) && format.Contains("{message}", StringComparison.OrdinalIgnoreCase)
		&& format.Length <= 128;

	// ---- Loading / saving ----

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

	internal static ChatSettings Current { get; private set; } = new();

	private static string FullPath => Server.GameDirectory + ConfigPath;

	/// <summary>
	/// Loads the file (creates it with the defaults when missing). A broken file keeps the defaults.
	/// </summary>
	/// <returns>An error message, or null</returns>
	internal static string? Load()
	{
		try
		{
			if (!File.Exists(FullPath))
			{
				Current = new ChatSettings();
				Save();
				return null;
			}

			Current = JsonSerializer.Deserialize<ChatSettings>(File.ReadAllText(FullPath), JsonOptions) ?? new ChatSettings();
			if (!IsValidFormat(Current.Format))
				Current.Format = DefaultFormat;
			return null;
		}
		catch (Exception ex)
		{
			Current = new ChatSettings();
			return ex.Message;
		}
	}

	/// <summary>
	/// Writes the current settings (temp file first, then swapped in).
	/// </summary>
	internal static void Save()
	{
		string temp = FullPath + ".tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
		File.Move(temp, FullPath, overwrite: true);
	}
}
