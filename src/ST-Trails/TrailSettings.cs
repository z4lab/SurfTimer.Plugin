using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;

namespace SurfTimer;

/// <summary>
/// cfg/SurfTimer/trail_settings.json - trails on/off, their look and the color per group. Created with the
/// defaults when missing; changed through !surfadmin (Server - Trails). The role flags (root / admin / VIP)
/// are the chat processor's (chat_settings.json).
/// </summary>
internal sealed class TrailSettings
{
	private const string ConfigPath = "/csgo/cfg/SurfTimer/trail_settings.json";

	[JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
	[JsonPropertyName("length_seconds")] public double LengthSeconds { get; set; } = 1.5;
	[JsonPropertyName("width")] public double Width { get; set; } = 3.0;
	[JsonPropertyName("segment_ticks")] public int SegmentTicks { get; set; } = 6;
	[JsonPropertyName("min_speed")] public double MinSpeed { get; set; } = 50;
	[JsonPropertyName("colors")] public ColorSettings Colors { get; set; } = new();

	internal sealed class ColorSettings
	{
		[JsonPropertyName("1")] public string First { get; set; } = "#FFD700";
		[JsonPropertyName("2")] public string Second { get; set; } = "#C0C0C0";
		[JsonPropertyName("3")] public string Third { get; set; } = "#CD7F32";
		[JsonPropertyName("top10")] public string Top10 { get; set; } = "#4FC3F7";
		[JsonPropertyName("top50")] public string Top50 { get; set; } = "#7986CB";
		[JsonPropertyName("top100")] public string Top100 { get; set; } = "#8FD18B";
		[JsonPropertyName("vip")] public string Vip { get; set; } = "#A0E060";
		[JsonPropertyName("admin")] public string Admin { get; set; } = "#B070E0";
		[JsonPropertyName("root")] public string Root { get; set; } = "#E53935";
		[JsonPropertyName("replay")] public string Replay { get; set; } = "#FFFFFF";
	}

	/// <summary>Segments per trail, from its length and how often a segment is drawn</summary>
	internal int SegmentCount => Math.Clamp((int)Math.Round(LengthSeconds * 64 / Math.Max(1, SegmentTicks)), 1, 64);

	/// <summary>Keeps values in the ranges the panel allows (hand-edited files too)</summary>
	internal void Clamp()
	{
		LengthSeconds = Math.Clamp(LengthSeconds, 0.5, 5);
		Width = Math.Clamp(Width, 1, 10);
		SegmentTicks = Math.Clamp(SegmentTicks, 2, 16);
		MinSpeed = Math.Clamp(MinSpeed, 0, 1000);
	}

	// ---- Loading / saving ----

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

	internal static TrailSettings Current { get; private set; } = new();

	private static string FullPath => Server.GameDirectory + ConfigPath;

	/// <summary>Loads the file (creates it with the defaults when missing) - returns an error, or null</summary>
	internal static string? Load()
	{
		try
		{
			if (!File.Exists(FullPath))
			{
				Current = new TrailSettings();
				Save();
				return null;
			}

			Current = JsonSerializer.Deserialize<TrailSettings>(File.ReadAllText(FullPath), JsonOptions) ?? new TrailSettings();
			Current.Clamp();
			return null;
		}
		catch (Exception ex)
		{
			Current = new TrailSettings();
			return ex.Message;
		}
	}

	internal static void Save()
	{
		string temp = FullPath + ".tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
		File.Move(temp, FullPath, overwrite: true);
	}
}

/// <summary>
/// Trail colors: "#RRGGBB" or "rainbow", and the palette offered in !options / !surfadmin.
/// </summary>
internal static class TrailColors
{
	internal const string Rainbow = "rainbow";

	private static readonly Regex HexPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

	internal static readonly IReadOnlyList<(string Name, string Hex)> Palette =
	[
		("Red", "#E53935"), ("Orange", "#FB8C00"), ("Gold", "#FFD700"), ("Yellow", "#FFEB3B"),
		("Lime", "#A0E060"), ("Green", "#43A047"), ("Cyan", "#00E5FF"), ("Light blue", "#4FC3F7"),
		("Blue", "#1E88E5"), ("Purple", "#B070E0"), ("Pink", "#F06292"), ("White", "#FFFFFF"),
	];

	internal static bool IsHex(string value) => HexPattern.IsMatch(value);

	/// <summary>A color players can pick (hex or rainbow)</summary>
	internal static bool IsValidCustom(string value) => value == Rainbow || IsHex(value);

	/// <summary>Palette name of a hex color, or the hex itself</summary>
	internal static string Describe(string value) => value == Rainbow ? "Rainbow"
		: Palette.FirstOrDefault(p => p.Hex.Equals(value, StringComparison.OrdinalIgnoreCase)).Name ?? value.ToUpperInvariant();

	/// <summary>
	/// A color value as a Color - rainbow moves along the hue with every segment (step).
	/// </summary>
	internal static Color Resolve(string value, int step)
	{
		if (value == Rainbow)
			return FromHue(step * 24 % 360);

		return IsHex(value)
			? Color.FromArgb(255,
				int.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber),
				int.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber),
				int.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber))
			: Color.White;
	}

	private static Color FromHue(int hue)
	{
		double h = hue / 60.0;
		double x = 1 - Math.Abs(h % 2 - 1);
		var (r, g, b) = (int)h switch
		{
			0 => (1.0, x, 0.0),
			1 => (x, 1.0, 0.0),
			2 => (0.0, 1.0, x),
			3 => (0.0, x, 1.0),
			4 => (x, 0.0, 1.0),
			_ => (1.0, 0.0, x),
		};
		return Color.FromArgb(255, (int)(r * 255), (int)(g * 255), (int)(b * 255));
	}
}
