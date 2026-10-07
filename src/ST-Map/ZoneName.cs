using System.Text.RegularExpressions;

namespace SurfTimer;

/// <summary>
/// What a zone does. The values are stored in the database (zones.zone_type) - never renumber them.
/// </summary>
internal enum ZoneType : byte
{
	MapEnd = 0,
	MapStart = 1,
	StageStart = 2,
	Checkpoint = 3,
	BonusStart = 4,
	BonusEnd = 5,
	/// <summary>Touching it stops the timer</summary>
	Stop = 6,
	/// <summary>Touching it sends the player back like !rs</summary>
	TeleportBack = 7,
	/// <summary>Horizontal speed is capped to the zone's value while inside</summary>
	SpeedCap = 8,
	Unknown = 255,
}

/// <summary>
/// One active zone (see ZoneDefinition for the stored form). Several can share Type+Number; Teleport is where
/// players are placed when sent there. Mins / Maxs are its world-space bounds; prisms and trigger zones carry
/// their shape in Geometry (for trigger zones: the trigger's rotated bounds - their real touches come from
/// the trigger's own events).
/// </summary>
internal sealed record ZoneInfo(int ZoneId, string Name, ZoneType Type, short Number, VectorT Teleport, QAngleT? Angles,
	VectorT Mins, VectorT Maxs, float? Value = null)
{
	internal ZoneShape Shape { get; init; }
	internal ZoneGeometry? Geometry { get; init; }
	internal string? TriggerName { get; init; }
	internal VectorT? TriggerOrigin { get; init; }

	/// <summary>Detected by its map trigger's touch events, not by the tick overlap test</summary>
	internal bool IsTriggerLinked => Shape == ZoneShape.Trigger;

	/// <summary>Whether a box (e.g. a player's hull) overlaps this zone - a trigger zone by its rotated bounds</summary>
	internal bool Overlaps(in VectorT mins, in VectorT maxs)
	{
		if (mins.X > Maxs.X || maxs.X < Mins.X || mins.Y > Maxs.Y || maxs.Y < Mins.Y || mins.Z > Maxs.Z || maxs.Z < Mins.Z)
			return false;
		return Shape == ZoneShape.Box || Geometry == null || Geometry.Overlaps(mins, maxs);
	}

	internal bool Contains(in VectorT point)
	{
		if (point.X < Mins.X || point.X > Maxs.X || point.Y < Mins.Y || point.Y > Maxs.Y || point.Z < Mins.Z || point.Z > Maxs.Z)
			return false;
		return Shape == ZoneShape.Box || Geometry == null || Geometry.Contains(point);
	}
}

/// <summary>
/// Single source of truth for zone trigger names. Several triggers may share a role+number, either
/// with the exact same name or with an underscore suffix (map_start_2, s2_start_left, bonus1_end_b,
/// map_cp3_a); map_start/map_end may also take digits directly (map_start2, map_end2).
/// Stage 1 (s1_start / stage1_start) is the map start.
/// </summary>
internal static class ZoneName
{
	private const string Suffix = @"(?:_[a-z0-9]+)?";
	private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

	private static readonly Regex MapStart = new(@"^map_start(?:\d+|_[a-z0-9]+)?$", Options);
	private static readonly Regex StageOneStart = new($@"^s(?:tage)?1_start{Suffix}$", Options);
	private static readonly Regex MapEnd = new(@"^map_end(?:\d+|_[a-z0-9]+)?$", Options);
	private static readonly Regex StageStart = new($@"^s(?:tage)?([1-9][0-9]?)_start{Suffix}$", Options);
	private static readonly Regex Checkpoint = new($@"^map_c(?:p|heckpoint)([1-9][0-9]?){Suffix}$", Options);
	private static readonly Regex BonusStart = new($@"^b(?:onus)?([1-9][0-9]?)_start{Suffix}$", Options);
	private static readonly Regex BonusEnd = new($@"^b(?:onus)?([1-9][0-9]?)_end{Suffix}$", Options);

	/// <summary>
	/// The loaded map runs "stages as checkpoints" (map setting stages_as_checkpoints): stage starts
	/// count as checkpoints and the map is linear. Set when the map object is created.
	/// </summary>
	internal static bool StagesAsCheckpoints { get; set; }

	/// <summary>
	/// Applies the map's zone mode to a parsed zone. With stages as checkpoints stage N's start is
	/// checkpoint N-1 (stage 1 is the map start), and the map's own checkpoint zones are ignored so the
	/// numbering stays unique. Returns false for a zone that doesn't count.
	/// </summary>
	public static bool Remap(ref ZoneType type, ref short number)
	{
		if (!StagesAsCheckpoints)
			return true;

		if (type == ZoneType.Checkpoint)
			return false;

		if (type == ZoneType.StageStart)
		{
			type = ZoneType.Checkpoint;
			number = (short)(number - 1);
		}
		return true;
	}

	/// <summary>
	/// Parses a trigger name into its zone role and number (map start = 1, map end = 0).
	/// </summary>
	public static bool TryParse(string? name, out ZoneType type, out short number)
	{
		type = ZoneType.Unknown;
		number = 0;
		if (string.IsNullOrEmpty(name))
			return false;

		if (MapStart.IsMatch(name) || StageOneStart.IsMatch(name))
		{
			type = ZoneType.MapStart;
			number = 1;
			return true;
		}

		if (MapEnd.IsMatch(name))
		{
			type = ZoneType.MapEnd;
			return true;
		}

		return TryMatch(StageStart, name, ZoneType.StageStart, ref type, ref number)
			|| TryMatch(Checkpoint, name, ZoneType.Checkpoint, ref type, ref number)
			|| TryMatch(BonusStart, name, ZoneType.BonusStart, ref type, ref number)
			|| TryMatch(BonusEnd, name, ZoneType.BonusEnd, ref type, ref number);
	}

	/// <summary>
	/// Parses a spawn destination name: the zone name prefixed with "spawn_" (spawn_map_start,
	/// spawn_s2_start_b, spawn_b1_start).
	/// </summary>
	public static bool TryParseSpawn(string? name, out ZoneType type, out short number)
	{
		type = ZoneType.Unknown;
		number = 0;
		if (string.IsNullOrEmpty(name) || !name.StartsWith("spawn_", StringComparison.OrdinalIgnoreCase))
			return false;

		return TryParse(name["spawn_".Length..], out type, out number);
	}

	private static bool TryMatch(Regex regex, string name, ZoneType matchType, ref ZoneType type, ref short number)
	{
		var match = regex.Match(name);
		if (!match.Success)
			return false;

		type = matchType;
		number = short.Parse(match.Groups[1].Value);
		return true;
	}
}
