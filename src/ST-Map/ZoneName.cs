using CounterStrikeSharp.API.Core;
using System.Text.RegularExpressions;

namespace SurfTimer;

internal enum ZoneType
{
	MapEnd,
	MapStart,
	StageStart,
	Checkpoint,
	BonusStart,
	BonusEnd,
	Unknown
}

/// <summary>
/// One zone trigger. Several can share Type+Number; Teleport is where players are placed when sent there.
/// </summary>
internal sealed record ZoneInfo(uint TriggerIndex, string Name, ZoneType Type, short Number, VectorT Teleport, QAngleT? Angles)
{
	/// <summary>
	/// Classifies a touched trigger by its name (Teleport is just its origin - only the map's zone
	/// registry resolves real teleport targets).
	/// </summary>
	public static bool TryFromTrigger(CBaseTrigger trigger, out ZoneInfo zone)
	{
		zone = null!;
		string? name = trigger.Entity?.Name;
		if (!ZoneName.TryParse(name, out var type, out var number))
			return false;

		zone = new ZoneInfo(trigger.Index, name!, type, number, trigger.AbsOrigin!.ToVector_t(), null);
		return true;
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
