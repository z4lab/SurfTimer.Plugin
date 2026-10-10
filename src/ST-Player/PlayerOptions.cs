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

/// <summary>Which velocity axes speeds are shown with (!options - HUD)</summary>
internal enum SpeedAxes
{
	/// <summary>Horizontal</summary>
	XY,
	/// <summary>3D</summary>
	XYZ,
	/// <summary>Vertical, signed (negative = falling)</summary>
	Z,
}

/// <summary>Which zone outlines a player sees (!options - Visibility)</summary>
internal enum ZoneDisplay
{
	Off,
	/// <summary>Map / stage / bonus starts and ends</summary>
	StartEnd,
	/// <summary>Every zone, checkpoints and special zones too</summary>
	All,
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
	internal const string KeyChatSaveloc = "chat_saveloc";
	internal const string KeyConfirmReset = "confirm_reset";
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
	internal const string KeyZonesShow = "zones_show";
	internal const string KeySpeedAxes = "speed_axes";
	internal const string KeySoundVolume = "sound_volume";
	/// <summary>Main switch for every sound (!quake)</summary>
	internal const string KeySounds = "sounds";
	/// <summary>Per kind of sound: sound_&lt;key&gt; (Sounds.Categories)</summary>
	internal const string KeySoundPrefix = "sound_";

	/// <summary>Sound volume steps in !options (percent) - the default is subtle</summary>
	internal static readonly int[] SoundVolumeSteps = [0, 10, 20, 30, 50, 75, 100];
	internal const int DefaultSoundVolume = 20;
	/// <summary>Per HUD part: hud_pos_top, hud_pos_center, hud_pos_left, hud_pos_right - "" = the server's default</summary>
	internal const string KeyHudPosPrefix = "hud_pos_";

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
	private bool _chatSplits, _chatOthersPb, _chatOthersRecords, _chatConnects, _chatSaveloc;
	private bool _confirmReset;
	private bool _hudTop, _hudSpectators, _hudSplitsKeep;
	private SplitTarget _hudSplitTarget = SplitTarget.Pb;
	private string _hudFields = DefaultHudFields;
	private bool _trailMine, _trailsOthers, _trailsOwn, _trailsSpectate, _trailsBots;
	private string _trailColor = "";
	private ZoneDisplay _zonesShow = ZoneDisplay.Off;
	private SpeedAxes _speedAxes = SpeedAxes.XY;
	private int _soundVolume = DefaultSoundVolume;
	private bool _sounds = true;
	private readonly Dictionary<string, int> _hudPositions = new();

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
		_chatSaveloc = Bool(KeyChatSaveloc, true);
		_confirmReset = Bool(KeyConfirmReset, true);
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
		_speedAxes = _profile.Settings.TryGetValue(KeySpeedAxes, out var axes)
			&& Enum.TryParse(axes, ignoreCase: true, out SpeedAxes parsedAxes) && Enum.IsDefined(parsedAxes) ? parsedAxes : SpeedAxes.XY;
		_zonesShow = _profile.Settings.TryGetValue(KeyZonesShow, out var zones)
			&& Enum.TryParse(zones, ignoreCase: true, out ZoneDisplay display) && Enum.IsDefined(display) ? display : ZoneDisplay.Off;
		_trailColor = _profile.Settings.TryGetValue(KeyTrailColor, out var trailColor) && TrailColors.IsValidCustom(trailColor) ? trailColor : "";
		_soundVolume = _profile.Settings.TryGetValue(KeySoundVolume, out var volume) && int.TryParse(volume, out int percent)
			&& percent is >= 0 and <= 100 ? percent : DefaultSoundVolume;
		_sounds = Bool(KeySounds, true);
		_hudPositions.Clear();
		foreach (string slot in CustomHud.SlotShift.Keys)
		{
			if (_profile.Settings.TryGetValue(KeyHudPosPrefix + slot, out var pos) && int.TryParse(pos, out int shift)
				&& shift >= 0 && shift <= CustomHud.MaxShift)
				_hudPositions[slot] = shift;
		}

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

	/// <summary>"Teleported to #N" after loading a saveloc (at most one per burst of loads)</summary>
	internal bool ChatSaveloc { get => _chatSaveloc; set { _chatSaveloc = value; Save(KeyChatSaveloc, value); } }

	// ---- Gameplay ----

	/// <summary>During a run !r has to be typed twice within a few seconds - the first one only warns</summary>
	internal bool ConfirmReset { get => _confirmReset; set { _confirmReset = value; Save(KeyConfirmReset, value); } }

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

	/// <summary>Where a HUD part sits (shift-0..10 of the layout) - the player's own, else the server's default</summary>
	internal int HudPosition(string slot) =>
		_hudPositions.TryGetValue(slot, out int shift) ? shift : CustomHud.SlotShift[slot];

	/// <summary>The player's own position of a HUD part - null = the server's default</summary>
	internal int? OwnHudPosition(string slot) => _hudPositions.TryGetValue(slot, out int shift) ? shift : null;

	internal void SetHudPosition(string slot, int? shift)
	{
		if (shift is int value)
			_hudPositions[slot] = Math.Clamp(value, 0, CustomHud.MaxShift);
		else
			_hudPositions.Remove(slot);
		_profile.SetSetting(KeyHudPosPrefix + slot, shift?.ToString() ?? "");
	}

	/// <summary>Main switch for every timer / addon sound (!quake)</summary>
	internal bool SoundsEnabled { get => _sounds; set { _sounds = value; Save(KeySounds, value); } }

	/// <summary>Whether a kind of sound is on - its default until the player changes it</summary>
	internal bool SoundOn(SoundCategory category) =>
		_profile.Settings.TryGetValue(KeySoundPrefix + category.Key, out var stored) ? stored is "1" or "true" : category.DefaultOn;

	internal void SetSoundOn(SoundCategory category, bool on) => Save(KeySoundPrefix + category.Key, on);

	/// <summary>Volume of plugin / addon sounds (e.g. the map chooser's vote beeps), 0-100 percent</summary>
	internal int SoundVolume
	{
		get => _soundVolume;
		set
		{
			_soundVolume = Math.Clamp(value, 0, 100);
			_profile.SetSetting(KeySoundVolume, _soundVolume.ToString());
		}
	}

	/// <summary>Axes the HUD, prespeed and split speeds are shown with</summary>
	internal SpeedAxes SpeedAxes
	{
		get => _speedAxes;
		set
		{
			_speedAxes = value;
			_profile.SetSetting(KeySpeedAxes, value.ToString().ToLowerInvariant());
		}
	}

	/// <summary>Zone outlines this player sees (drawn with beams - ZoneDrawing.cs)</summary>
	internal ZoneDisplay ZonesShow
	{
		get => _zonesShow;
		set
		{
			_zonesShow = value;
			_profile.SetSetting(KeyZonesShow, value.ToString().ToLowerInvariant());
		}
	}

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
