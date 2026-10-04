namespace SurfTimer;

/// <summary>Fields of the bottom HUD block the player can arrange (!options - HUD)</summary>
internal enum HudFieldKind
{
	Timer,
	Speed,
	Prespeed,
	Keys,
	Sync,
}

/// <summary>What the HUD splits panel compares the run against (!options - HUD)</summary>
internal enum SplitTarget
{
	Off,
	Pb,
	Wr,
	Top10,
	G1,
	G2,
	G3,
	G4,
	G5,
}

/// <summary>
/// A player's client options (!options), stored in player_settings. Values are cached here - several
/// are read every tick (transmit, HUD) - and saved in the background when changed.
/// </summary>
internal sealed class PlayerOptions
{
	internal const string KeyHideLegs = "hide_legs";
	internal const string KeyHidePlayers = "hide_players";
	internal const string KeyHideBots = "hide_bots";
	internal const string KeyChatSplits = "chat_splits";
	internal const string KeyChatOthersPb = "chat_others_pb";
	internal const string KeyChatOthersRecords = "chat_others_records";
	internal const string KeyChatConnects = "chat_connects";
	internal const string KeyHudTop = "hud_top";
	internal const string KeyHudSplits = "hud_splits"; // Old on / off switch - only read to migrate to KeyHudSplitTarget
	internal const string KeyHudSplitTarget = "hud_splits_target";
	internal const string KeyHudSplitsKeep = "hud_splits_keep";
	internal const string KeyHudSpectators = "hud_spectators";
	internal const string KeyHudFields = "hud_fields";
	internal const string KeyTrailMine = "trail_mine";
	internal const string KeyTrailsOthers = "trails_others";
	internal const string KeyTrailsOwn = "trails_own";
	internal const string KeyTrailsSpectate = "trails_spectate";
	internal const string KeyTrailsBots = "trails_bots";
	internal const string KeyTrailColor = "trail_color";

	/// <summary>Bottom HUD: rows split by '|', fields by ','</summary>
	internal const string DefaultHudFields = "timer,speed|prespeed,keys,sync";

	/// <summary>Named bottom HUD layouts offered in !options</summary>
	internal static readonly IReadOnlyList<(string Name, string Fields)> HudPresets =
	[
		("Default", DefaultHudFields),
		("Compact", "timer,speed,keys"),
		("Minimal", "timer,speed"),
		("Keys first", "keys,timer,speed|prespeed,sync"),
	];

	// Layout limits - fixed by the published HUD addon (CustomHud.FieldRows / FieldsPerRow)
	internal static int RowCapacity => CustomHud.FieldsPerRow;
	internal static int MaxRows => CustomHud.FieldRows;

	/// <summary>Width of a field in the bottom HUD - the timer spans two</summary>
	internal static int Width(HudFieldKind field) => field == HudFieldKind.Timer ? 2 : 1;

	private readonly PlayerProfile _profile;

	private bool _hideLegs, _hidePlayers, _hideBots;
	private bool _chatSplits, _chatOthersPb, _chatOthersRecords, _chatConnects;
	private bool _hudTop, _hudSpectators, _hudSplitsKeep;
	private SplitTarget _hudSplitTarget = SplitTarget.Pb;
	private string _hudFields = DefaultHudFields;
	private bool _trailMine, _trailsOthers, _trailsOwn, _trailsSpectate, _trailsBots;
	private string _trailColor = "";

	internal PlayerOptions(PlayerProfile profile)
	{
		_profile = profile;
		Load();
	}

	private void Load()
	{
		_hideLegs = Bool(KeyHideLegs, true);
		_hidePlayers = Bool(KeyHidePlayers, false);
		_hideBots = Bool(KeyHideBots, false);
		_chatSplits = Bool(KeyChatSplits, true);
		_chatOthersPb = Bool(KeyChatOthersPb, true);
		_chatOthersRecords = Bool(KeyChatOthersRecords, true);
		_chatConnects = Bool(KeyChatConnects, true);
		_hudTop = Bool(KeyHudTop, true);
		_hudSplitTarget = _profile.Settings.TryGetValue(KeyHudSplitTarget, out var target)
			&& Enum.TryParse(target, ignoreCase: true, out SplitTarget parsed) && Enum.IsDefined(parsed)
				? parsed
				: Bool(KeyHudSplits, true) ? SplitTarget.Pb : SplitTarget.Off;
		_hudSpectators = Bool(KeyHudSpectators, true);
		_hudSplitsKeep = Bool(KeyHudSplitsKeep, true);
		_trailMine = Bool(KeyTrailMine, true);
		_trailsOthers = Bool(KeyTrailsOthers, true);
		_trailsOwn = Bool(KeyTrailsOwn, true);
		_trailsSpectate = Bool(KeyTrailsSpectate, true);
		_trailsBots = Bool(KeyTrailsBots, true);
		_trailColor = _profile.Settings.TryGetValue(KeyTrailColor, out var trailColor) && TrailColors.IsValidCustom(trailColor) ? trailColor : "";

		// A stored layout the HUD can't show falls back to the default
		string fields = _profile.Settings.TryGetValue(KeyHudFields, out var value) ? value : DefaultHudFields;
		if (!TryParseHud(fields, out var rows))
		{
			fields = DefaultHudFields;
			TryParseHud(fields, out rows);
		}
		_hudFields = fields;
		HudRows = rows;

		bool Bool(string key, bool fallback) =>
			_profile.Settings.TryGetValue(key, out var stored) ? stored is "1" or "true" : fallback;
	}

	/// <summary>
	/// Back to the defaults (the stored settings were removed, e.g. by an admin).
	/// </summary>
	internal void ResetToDefaults()
	{
		_profile.Settings.Clear();
		Load();
	}

	private void Save(string key, bool value) => _profile.SetSetting(key, value ? "1" : "0");

	// ---- Visibility ----

	/// <summary>Own first-person legs hidden (render alpha 254 - others still see the model)</summary>
	internal bool HideLegs { get => _hideLegs; set { _hideLegs = value; Save(KeyHideLegs, value); } }

	/// <summary>Other players aren't transmitted to this player</summary>
	internal bool HidePlayers { get => _hidePlayers; set { _hidePlayers = value; Save(KeyHidePlayers, value); } }

	/// <summary>Replay bots aren't transmitted to this player</summary>
	internal bool HideBots { get => _hideBots; set { _hideBots = value; Save(KeyHideBots, value); } }

	// ---- Chat ----

	/// <summary>Own checkpoint / stage / segment comparison messages</summary>
	internal bool ChatSplits { get => _chatSplits; set { _chatSplits = value; Save(KeyChatSplits, value); } }

	/// <summary>Other players' PB announcements</summary>
	internal bool ChatOthersPb { get => _chatOthersPb; set { _chatOthersPb = value; Save(KeyChatOthersPb, value); } }

	/// <summary>Other players' record (WR) announcements</summary>
	internal bool ChatOthersRecords { get => _chatOthersRecords; set { _chatOthersRecords = value; Save(KeyChatOthersRecords, value); } }

	/// <summary>"X connected" messages</summary>
	internal bool ChatConnects { get => _chatConnects; set { _chatConnects = value; Save(KeyChatConnects, value); } }

	// ---- HUD ----

	internal bool HudTop { get => _hudTop; set { _hudTop = value; Save(KeyHudTop, value); } }

	/// <summary>The splits panel is shown (anything but Off)</summary>
	internal bool HudSplits => _hudSplitTarget != SplitTarget.Off;

	internal SplitTarget HudSplitTarget
	{
		get => _hudSplitTarget;
		set
		{
			_hudSplitTarget = value;
			_profile.SetSetting(KeyHudSplitTarget, value.ToString().ToLowerInvariant());
		}
	}

	/// <summary>The splits panel keeps the last run's splits (after a fail / reset / finish) until the next run starts</summary>
	internal bool HudSplitsKeep { get => _hudSplitsKeep; set { _hudSplitsKeep = value; Save(KeyHudSplitsKeep, value); } }

	internal bool HudSpectators { get => _hudSpectators; set { _hudSpectators = value; Save(KeyHudSpectators, value); } }

	// ---- Trails ----

	/// <summary>Draw my trail (off = nobody sees it)</summary>
	internal bool TrailMine { get => _trailMine; set { _trailMine = value; Save(KeyTrailMine, value); } }

	/// <summary>See other players' trails</summary>
	internal bool TrailsOthers { get => _trailsOthers; set { _trailsOthers = value; Save(KeyTrailsOthers, value); } }

	/// <summary>See my own trail</summary>
	internal bool TrailsOwn { get => _trailsOwn; set { _trailsOwn = value; Save(KeyTrailsOwn, value); } }

	/// <summary>See the trail of the player / bot I spectate</summary>
	internal bool TrailsSpectate { get => _trailsSpectate; set { _trailsSpectate = value; Save(KeyTrailsSpectate, value); } }

	/// <summary>See replay bots' trails</summary>
	internal bool TrailsBots { get => _trailsBots; set { _trailsBots = value; Save(KeyTrailsBots, value); } }

	/// <summary>Own trail color ("#RRGGBB" or "rainbow") - empty = the group's color. Only used while eligible.</summary>
	internal string TrailColor
	{
		get => _trailColor;
		set
		{
			_trailColor = value;
			_profile.SetSetting(KeyTrailColor, value);
		}
	}

	/// <summary>Any viewer toggle off - the transmit filter has work to do for this player</summary>
	internal bool FiltersTrails => !_trailsOthers || !_trailsOwn || !_trailsSpectate || !_trailsBots;

	/// <summary>The bottom HUD layout as stored (see DefaultHudFields)</summary>
	internal string HudFields => _hudFields;

	/// <summary>The bottom HUD layout, parsed - read by the HUD every tick</summary>
	internal IReadOnlyList<IReadOnlyList<HudFieldKind>> HudRows { get; private set; } = [];

	/// <summary>
	/// Sets the bottom HUD layout - false (nothing changed) when it doesn't fit the HUD.
	/// </summary>
	internal bool TrySetHudFields(string fields)
	{
		if (!TryParseHud(fields, out var rows))
			return false;

		_hudFields = Format(rows);
		HudRows = rows;
		_profile.SetSetting(KeyHudFields, _hudFields);
		return true;
	}

	/// <summary>
	/// Parses "timer,speed|prespeed,keys,sync": at most MaxRows rows of RowCapacity width, every field at
	/// most once. Empty rows are dropped; an empty layout (no bottom HUD) is allowed.
	/// </summary>
	internal static bool TryParseHud(string text, out IReadOnlyList<IReadOnlyList<HudFieldKind>> rows)
	{
		rows = [];
		var parsed = new List<IReadOnlyList<HudFieldKind>>();
		var seen = new HashSet<HudFieldKind>();

		foreach (var rowText in text.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			var row = new List<HudFieldKind>();
			foreach (var name in rowText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
			{
				if (!Enum.TryParse(name, ignoreCase: true, out HudFieldKind field) || !Enum.IsDefined(field) || !seen.Add(field))
					return false;
				row.Add(field);
			}
			if (row.Sum(Width) > RowCapacity)
				return false;
			if (row.Count > 0)
				parsed.Add(row);
		}

		if (parsed.Count > MaxRows)
			return false;
		rows = parsed;
		return true;
	}

	internal static string Format(IEnumerable<IEnumerable<HudFieldKind>> rows) =>
		string.Join("|", rows.Select(r => string.Join(",", r.Select(f => f.ToString().ToLowerInvariant()))).Where(r => r.Length > 0));
}
