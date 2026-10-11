using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core;
using SurfTimer.Api;

namespace SurfTimer;

/// <summary>
/// The timer's own live values a player can place in the bottom HUD and the center HUD (!options - HUD). Stored in the
/// layouts by their lowercase name - existing names must not change. The order here is the menus' order.
/// </summary>
internal enum HudFieldKind
{
	Timer,
	/// <summary>Time in the current stage / checkpoint segment</summary>
	SegmentTime,
	Speed,
	Prespeed,
	Keys,
	Sync,
	/// <summary>Sync of the current stage / checkpoint segment</summary>
	SegmentSync,
	/// <summary>Mechanical energy (Energy.cs)</summary>
	Energy,
	/// <summary>Projected finish (PaceTracker) against the splits panel's target</summary>
	Pace,
	/// <summary>Ahead / behind that reference</summary>
	PaceDelta,
	/// <summary>Energy difference to the reference at the same spot</summary>
	EnergyDiff,
	/// <summary>Speed difference to the reference at the same spot</summary>
	SpeedDiff,
	/// <summary>The last checkpoint / stage split against the splits panel's target</summary>
	LastSplit,
	/// <summary>Stage 3/7, CP 4/12, Bonus 2</summary>
	Progress,
	Pb,
	Wr,
	Rank,
	/// <summary>Jumps in the current run</summary>
	Jumps,
	/// <summary>Strafes in the current run (air strafe key changes)</summary>
	Strafes,
}

/// <summary>
/// Every field a HUD layout can hold: the timer's own (HudFieldKind, key = its lowercase name) and the ones addons
/// register through SurfTimer.Api (key "addon.name"). Layouts keep addon keys while the addon isn't loaded - such a
/// field just shows nothing.
/// </summary>
internal static class HudFieldCatalog
{
	internal sealed record Definition(string Key, string Label, string Description, HudFieldKind? Kind);

	/// <summary>A field an addon registered: its value per viewer</summary>
	internal sealed record AddonField(string Key, string Label, string Description, Func<CCSPlayerController, HudValue?> Value);

	private static readonly Regex AddonKey = new("^[a-z0-9_]+\\.[a-z0-9_]+$", RegexOptions.Compiled);

	private static readonly Dictionary<HudFieldKind, (string Label, string Description)> Builtin = new()
	{
		[HudFieldKind.Timer] = ("Timer", "run time"),
		[HudFieldKind.SegmentTime] = ("Segment time", "time in the current stage / checkpoint"),
		[HudFieldKind.Speed] = ("Speed", "on your speed axes"),
		[HudFieldKind.Prespeed] = ("Prespeed", "live in a start zone, then the exit speed"),
		[HudFieldKind.Keys] = ("Keys", "W A S D, jump, crouch"),
		[HudFieldKind.Sync] = ("Sync", "strafe sync of the run"),
		[HudFieldKind.SegmentSync] = ("Segment sync", "sync of the current stage / checkpoint"),
		[HudFieldKind.Energy] = ("Energy", "speed and height in one number"),
		[HudFieldKind.Pace] = ("Pace", "projected finish vs your splits target"),
		[HudFieldKind.PaceDelta] = ("Pace delta", "ahead / behind that run"),
		[HudFieldKind.EnergyDiff] = ("Energy vs ref", "energy difference at the same spot"),
		[HudFieldKind.SpeedDiff] = ("Speed vs ref", "speed difference at the same spot"),
		[HudFieldKind.LastSplit] = ("Last split", "last checkpoint / stage vs your splits target"),
		[HudFieldKind.Progress] = ("Stage / CP", "stage, checkpoint or bonus you're on"),
		[HudFieldKind.Pb] = ("PB", "your best time for this run"),
		[HudFieldKind.Wr] = ("WR", "the record for this run"),
		[HudFieldKind.Rank] = ("Rank", "your rank for this run"),
		[HudFieldKind.Jumps] = ("Jumps", "jumps in this run"),
		[HudFieldKind.Strafes] = ("Strafes", "strafes in this run"),
	};

	private static readonly Dictionary<string, AddonField> Addons = new();

	internal static string KeyOf(HudFieldKind kind) => kind.ToString().ToLowerInvariant();

	/// <summary>The timer's own field of a key - null for addon keys</summary>
	internal static HudFieldKind? KindOf(string key) =>
		Enum.TryParse(key, ignoreCase: true, out HudFieldKind kind) && Enum.IsDefined(kind) && key.All(char.IsLetter) ? kind : null;

	/// <summary>A key a layout may hold: one of the timer's fields, or an addon key (registered or not)</summary>
	internal static bool IsValidKey(string key) => KindOf(key) != null || AddonKey.IsMatch(key);

	/// <summary>The fields offered in the menus: the timer's, then the addons' registered now</summary>
	internal static IEnumerable<Definition> All =>
		Builtin.OrderBy(b => b.Key).Select(b => new Definition(KeyOf(b.Key), b.Value.Label, b.Value.Description, b.Key))
			.Concat(Addons.Values.OrderBy(a => a.Label).Select(a => new Definition(a.Key, a.Label, a.Description, null)));

	internal static string Label(string key) =>
		KindOf(key) is HudFieldKind kind ? Builtin[kind].Label
		: Addons.TryGetValue(key, out var addon) ? addon.Label
		: key;

	internal static AddonField? Addon(string key) => Addons.GetValueOrDefault(key);

	/// <returns>false for a key that isn't "addon.name" (lowercase letters, digits, _)</returns>
	internal static bool Register(AddonField field)
	{
		if (!AddonKey.IsMatch(field.Key))
			return false;
		Addons[field.Key] = field;
		return true;
	}

	internal static void Unregister(string key) => Addons.Remove(key);

	internal static void ClearAddons() => Addons.Clear();
}

/// <summary>
/// The shape of one HUD's layout: rows of field keys. Stored as rows split by '|', fields by ','.
/// </summary>
/// <param name="Rows">Rows the HUD has</param>
/// <param name="Capacity">Width a row holds</param>
/// <param name="KeepRows">Rows have fixed places (the center's lines): empty ones are kept, not moved up</param>
/// <param name="RowWord">"row" / "line" in the menus</param>
internal sealed record HudLayoutSpec(int Rows, int Capacity, bool KeepRows, string RowWord, Func<string, int> Width)
{
	/// <summary>Bottom HUD: 2 rows of 4 slots - the timer takes 2 (fixed by the HUD addon: CustomHud.FieldRows / FieldsPerRow)</summary>
	internal static readonly HudLayoutSpec Bottom = new(CustomHud.FieldRows, CustomHud.FieldsPerRow, false, "row",
		key => HudFieldCatalog.KindOf(key) == HudFieldKind.Timer ? 2 : 1);

	/// <summary>Center HUD: a line above the crosshair, one at it, one below - up to 3 fields each, in one text</summary>
	internal static readonly HudLayoutSpec Center = new(CustomHud.MidSlots, 3, true, "line", _ => 1);

	/// <summary>
	/// Parses a stored layout: every key valid and used once, no row wider than the capacity, at most Rows rows. Empty
	/// rows are dropped (KeepRows: kept, padded to Rows). An empty layout is allowed.
	/// </summary>
	internal bool TryParse(string text, out IReadOnlyList<IReadOnlyList<string>> rows)
	{
		rows = [];
		var parsed = new List<IReadOnlyList<string>>();
		var seen = new HashSet<string>();
		var rowTexts = text.Split('|', StringSplitOptions.TrimEntries);
		if (rowTexts.Length > Rows && rowTexts.Skip(Rows).Any(r => r.Length > 0))
			return false;

		foreach (var rowText in rowTexts.Take(Rows))
		{
			var row = new List<string>();
			foreach (var raw in rowText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
			{
				string key = raw.ToLowerInvariant();
				if (!HudFieldCatalog.IsValidKey(key) || !seen.Add(key))
					return false;
				row.Add(key);
			}
			if (row.Sum(Width) > Capacity)
				return false;
			if (row.Count > 0 || KeepRows)
				parsed.Add(row);
		}

		while (KeepRows && parsed.Count < Rows)
			parsed.Add([]);
		if (parsed.Count > Rows)
			return false;
		rows = parsed;
		return true;
	}

	internal string Format(IEnumerable<IEnumerable<string>> rows)
	{
		var texts = rows.Select(r => string.Join(",", r)).ToList();
		return KeepRows ? string.Join("|", texts) : string.Join("|", texts.Where(r => r.Length > 0));
	}

	/// <summary>Where a field is: (row, position) or null when it isn't shown</summary>
	internal static (int Row, int Index)? Find(IReadOnlyList<IReadOnlyList<string>> rows, string key)
	{
		for (int r = 0; r < rows.Count; r++)
		{
			int i = rows[r].ToList().IndexOf(key);
			if (i >= 0)
				return (r, i);
		}
		return null;
	}
}
