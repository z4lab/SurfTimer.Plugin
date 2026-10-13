namespace SurfTimer;

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

/// <summary>How speeds are coloured in the HUD (!options - HUD - Speed color)</summary>
internal enum SpeedColorMode
{
	/// <summary>Nord gradient: slow cool blue to fast red</summary>
	Gradient,
	/// <summary>White, green while gaining speed, red while losing it</summary>
	GainLoss,
	/// <summary>One Nord colour of the player's choice</summary>
	Static,
}

/// <summary>What the running timer turns yellow at once it's slower (!options - HUD)</summary>
internal enum TimerWarning
{
	Pb,
	Wr,
}

/// <summary>The run the ghost path / orb follow (!options - HUD - Ghost &amp; path)</summary>
internal enum GhostTarget
{
	/// <summary>The splits panel's target (PB if it's off)</summary>
	Splits,
	Pb,
	Wr,
	/// <summary>The time one rank above yours (the last time without a PB)</summary>
	NextRank,
	Top10,
	G1,
	G2,
	G3,
	G4,
	G5,
}

/// <summary>
/// The run messages in chat a player can turn on / off - for their own runs (and whoever they spectate) and for other
/// players' (!options - Chat). Stored by lowercase name: chat_own_&lt;name&gt;, chat_others_&lt;name&gt;.
/// </summary>
internal enum ChatEvent
{
	/// <summary>In a map run, at every checkpoint / stage: the time there vs the map PB / WR's time there</summary>
	SplitMap,
	/// <summary>A stage / checkpoint segment's time vs its own segment PB / WR</summary>
	SplitSegment,
	MapFinish,
	MapPb,
	MapWr,
	StagePb,
	StageWr,
	CpPb,
	CpWr,
	BonusFinish,
	BonusPb,
	BonusWr,
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
	// Before the per-message toggles (chat_own_* / chat_others_*) - only read for their defaults
	internal const string KeyChatSplits = "chat_splits";
	internal const string KeyChatOthersPb = "chat_others_pb";
	internal const string KeyChatOthersRecords = "chat_others_records";
	internal const string KeyChatOwnPrefix = "chat_own_";
	internal const string KeyChatOthersPrefix = "chat_others_";
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
	/// <summary>The center HUD (custom HUD middle slots - fields around the crosshair) on / off</summary>
	internal const string KeyCenterHud = "center_hud";
	/// <summary>Center HUD layout: three lines (above, at, below the crosshair) split by '|', fields by ','</summary>
	internal const string KeyCenterFields = "center_fields";
	/// <summary>Center fields with their labels ("Speed 1234") instead of only the values</summary>
	internal const string KeyCenterLabels = "center_labels";
	/// <summary>Lines 1 and 2: center_size_N / center_offset_N - line 0 keeps the old center_speed / center_speed_offset</summary>
	internal const string KeyCenterSizePrefix = "center_size_";
	internal const string KeyCenterOffsetPrefix = "center_offset_";
	/// <summary>Live pace (projected finish) under the splits, and the energy / speed difference to the reference there</summary>
	internal const string KeyHudPace = "hud_pace";
	internal const string KeyHudPaceEnergy = "hud_pace_energy";
	/// <summary>Size of the center HUD's first line (the speed before the lines had fields), 1-5 (0 meant off before center_hud existed)</summary>
	internal const string KeyCenterSpeed = "center_speed";
	/// <summary>The center speed's colour: follow (the HUD speed colour) or a SpeedColorMode of its own</summary>
	internal const string KeyCenterSpeedColor = "center_speed_color";
	internal const string KeyCenterSpeedColorStatic = "center_speed_color_static";
	/// <summary>The first line's height: steps up (-) / down (+) from its place, 10px each</summary>
	internal const string KeyCenterSpeedOffset = "center_speed_offset";
	internal const string KeyCenterSpeedMono = "center_speed_mono";
	internal const string KeySpeedColor = "speed_color";
	internal const string KeySpeedColorStatic = "speed_color_static";
	internal const string KeyTimerWarning = "timer_warning";
	/// <summary>Ghost &amp; path (GhostPath.cs): the target's path ahead, its jump / crouch markers, the racing orb, which run, how far</summary>
	internal const string KeyGhostPath = "ghost_path";
	internal const string KeyGhostMarkers = "ghost_markers";
	internal const string KeyGhostOrb = "ghost_orb";
	internal const string KeyGhostTarget = "ghost_target";
	internal const string KeyGhostSeconds = "ghost_seconds";

	internal static readonly int[] GhostSecondSteps = [2, 4, 6, 8];
	internal const int DefaultGhostSeconds = 4;

	internal const int CenterSizes = 5;
	internal const int DefaultCenterSize = 3;
	internal const int CenterMaxOffset = 10;

	/// <summary>Center HUD: the speed above the crosshair, nothing at / below it</summary>
	internal const string DefaultCenterFields = "speed||";

	/// <summary>Static speed colours (Nord) - the layout's nc-&lt;key&gt; classes</summary>
	internal static readonly (string Key, string Label)[] SpeedStaticColors =
	[
		("snow", "Snow"), ("frost", "Frost"), ("teal", "Teal"), ("blue", "Blue"), ("deep", "Deep blue"),
		("red", "Red"), ("orange", "Orange"), ("yellow", "Yellow"), ("green", "Green"), ("purple", "Purple"),
	];
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

	private readonly PlayerProfile _profile;

	private bool _hideLegs, _hidePlayers, _hideBots;
	private bool _chatConnects, _chatSaveloc;
	private readonly Dictionary<ChatEvent, bool> _chatOwn = new(), _chatOthers = new();
	private bool _confirmReset;
	private bool _hudTop, _hudSpectators, _hudSplitsKeep;
	private SplitTarget _hudSplitTarget = SplitTarget.Pb;
	private string _hudFields = DefaultHudFields;
	private bool _trailMine, _trailsOthers, _trailsOwn, _trailsSpectate, _trailsBots;
	private string _trailColor = "";
	private ZoneDisplay _zonesShow = ZoneDisplay.Off;
	private SpeedAxes _speedAxes = SpeedAxes.XY;
	private int _soundVolume = DefaultSoundVolume;
	private string _centerFields = DefaultCenterFields;
	private readonly int[] _centerSizes = new int[CustomHud.MidSlots];
	private readonly int[] _centerOffsets = new int[CustomHud.MidSlots];
	private bool _centerHud, _centerLabels;
	private bool _hudPace, _hudPaceEnergy;
	private SpeedColorMode? _centerSpeedColor;
	private string _centerSpeedColorStatic = "snow";
	private bool _centerSpeedMono = true;
	private SpeedColorMode _speedColor = SpeedColorMode.Gradient;
	private string _speedColorStatic = "snow";
	private TimerWarning _timerWarning = TimerWarning.Pb;
	private bool _sounds = true;
	private bool _ghostPath, _ghostMarkers, _ghostOrb;
	private GhostTarget _ghostTarget = GhostTarget.Splits;
	private int _ghostSeconds = DefaultGhostSeconds;
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
		// Own: all on (splits as the old split switch). Others: PBs / records as the old switches, finishes and splits off
		bool oldSplits = Bool(KeyChatSplits, true), oldPb = Bool(KeyChatOthersPb, true), oldRecords = Bool(KeyChatOthersRecords, true);
		foreach (var ev in Enum.GetValues<ChatEvent>())
		{
			string name = ev.ToString().ToLowerInvariant();
			bool split = ev is ChatEvent.SplitMap or ChatEvent.SplitSegment;
			_chatOwn[ev] = Bool(KeyChatOwnPrefix + name, !split || oldSplits);
			_chatOthers[ev] = Bool(KeyChatOthersPrefix + name, ev switch
			{
				ChatEvent.MapPb or ChatEvent.StagePb or ChatEvent.CpPb or ChatEvent.BonusPb => oldPb,
				ChatEvent.MapWr or ChatEvent.StageWr or ChatEvent.CpWr or ChatEvent.BonusWr => oldRecords,
				_ => false,
			});
		}
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
		int storedSize = _profile.Settings.TryGetValue(KeyCenterSpeed, out var centerSize) && int.TryParse(centerSize, out int size)
			&& size is >= 0 and <= CenterSizes ? size : 0;
		// Before center_hud existed, size 0 meant off
		_centerHud = Bool(KeyCenterHud, storedSize > 0);
		_centerLabels = Bool(KeyCenterLabels, false);
		for (int line = 0; line < CustomHud.MidSlots; line++)
		{
			_centerSizes[line] = _profile.Settings.TryGetValue(CenterSizeKey(line), out var lineSize) && int.TryParse(lineSize, out int parsedSize)
				&& parsedSize is >= 1 and <= CenterSizes ? parsedSize : DefaultCenterSize;
			_centerOffsets[line] = _profile.Settings.TryGetValue(CenterOffsetKey(line), out var lineOffset) && int.TryParse(lineOffset, out int parsedOffset)
				&& Math.Abs(parsedOffset) <= CenterMaxOffset ? parsedOffset : 0;
		}
		_hudPace = Bool(KeyHudPace, false);
		_hudPaceEnergy = Bool(KeyHudPaceEnergy, false);
		_centerSpeedColor = _profile.Settings.TryGetValue(KeyCenterSpeedColor, out var centerColor)
			&& Enum.TryParse(centerColor, ignoreCase: true, out SpeedColorMode parsedCenterColor) && Enum.IsDefined(parsedCenterColor)
				? parsedCenterColor : null;
		_centerSpeedColorStatic = _profile.Settings.TryGetValue(KeyCenterSpeedColorStatic, out var centerStatic)
			&& SpeedStaticColors.Any(c => c.Key == centerStatic) ? centerStatic : "snow";
		_centerSpeedMono = Bool(KeyCenterSpeedMono, true);
		_speedColor = _profile.Settings.TryGetValue(KeySpeedColor, out var speedColor)
			&& Enum.TryParse(speedColor, ignoreCase: true, out SpeedColorMode parsedColor) && Enum.IsDefined(parsedColor) ? parsedColor : SpeedColorMode.Gradient;
		_speedColorStatic = _profile.Settings.TryGetValue(KeySpeedColorStatic, out var staticColor)
			&& SpeedStaticColors.Any(c => c.Key == staticColor) ? staticColor : "snow";
		_timerWarning = _profile.Settings.TryGetValue(KeyTimerWarning, out var warning)
			&& Enum.TryParse(warning, ignoreCase: true, out TimerWarning parsedWarning) && Enum.IsDefined(parsedWarning) ? parsedWarning : TimerWarning.Pb;
		_sounds = Bool(KeySounds, true);
		_ghostPath = Bool(KeyGhostPath, false);
		_ghostMarkers = Bool(KeyGhostMarkers, true);
		_ghostOrb = Bool(KeyGhostOrb, false);
		_ghostTarget = _profile.Settings.TryGetValue(KeyGhostTarget, out var ghostTarget)
			&& Enum.TryParse(ghostTarget, ignoreCase: true, out GhostTarget parsedGhost) && Enum.IsDefined(parsedGhost) ? parsedGhost : GhostTarget.Splits;
		_ghostSeconds = _profile.Settings.TryGetValue(KeyGhostSeconds, out var ghostSeconds) && int.TryParse(ghostSeconds, out int seconds)
			&& GhostSecondSteps.Contains(seconds) ? seconds : DefaultGhostSeconds;
		_hudPositions.Clear();
		foreach (string slot in CustomHud.SlotShift.Keys)
		{
			if (_profile.Settings.TryGetValue(KeyHudPosPrefix + slot, out var pos) && int.TryParse(pos, out int shift)
				&& shift >= 0 && shift <= CustomHud.MaxShift)
				_hudPositions[slot] = shift;
		}

		// A stored layout the HUD can't show falls back to the default
		(_hudFields, HudRows) = LoadLayout(HudLayoutSpec.Bottom, KeyHudFields, DefaultHudFields);
		(_centerFields, CenterLines) = LoadLayout(HudLayoutSpec.Center, KeyCenterFields, DefaultCenterFields);

		bool Bool(string key, bool fallback) =>
			_profile.Settings.TryGetValue(key, out var stored) ? stored is "1" or "true" : fallback;
	}

	private (string Text, IReadOnlyList<IReadOnlyList<string>> Rows) LoadLayout(HudLayoutSpec spec, string key, string fallback)
	{
		string text = _profile.Settings.TryGetValue(key, out var value) ? value : fallback;
		if (!spec.TryParse(text, out var rows))
		{
			text = fallback;
			spec.TryParse(text, out rows);
		}
		return (spec.Format(rows), rows);
	}

	private static string CenterSizeKey(int line) => line == 0 ? KeyCenterSpeed : KeyCenterSizePrefix + line;
	private static string CenterOffsetKey(int line) => line == 0 ? KeyCenterSpeedOffset : KeyCenterOffsetPrefix + line;

	/// <summary>
	/// Every setting of !options - HUD - what a HUD code (HudPresets) carries. Only these are taken from a code.
	/// </summary>
	internal static readonly IReadOnlyList<string> HudKeys =
	[
		KeyHudFields, KeyHudTop, KeyHudSplitTarget, KeyHudSplitsKeep, KeyHudSpectators, KeyHudPace, KeyHudPaceEnergy,
		KeySpeedAxes, KeySpeedColor, KeySpeedColorStatic, KeyTimerWarning,
		KeyCenterHud, KeyCenterFields, KeyCenterLabels, KeyCenterSpeed, KeyCenterSpeedOffset, KeyCenterSpeedMono,
		KeyCenterSpeedColor, KeyCenterSpeedColorStatic,
		.. Enumerable.Range(1, CustomHud.MidSlots - 1).SelectMany(line => new[] { KeyCenterSizePrefix + line, KeyCenterOffsetPrefix + line }),
		KeyGhostPath, KeyGhostMarkers, KeyGhostOrb, KeyGhostTarget, KeyGhostSeconds,
		.. CustomHud.SlotShift.Keys.Select(slot => KeyHudPosPrefix + slot),
	];

	/// <summary>
	/// Takes a HUD code's settings: each HUD option becomes the code's value, options it doesn't have go back to their
	/// defaults. Values the HUD can't use fall back like any stored setting does.
	/// </summary>
	internal void ApplyHud(IReadOnlyDictionary<string, string> settings)
	{
		foreach (string key in HudKeys)
		{
			if (settings.TryGetValue(key, out var value) && value.Length > 0 && value.Length <= 200)
				_profile.SetSetting(key, value);
			else if (_profile.Settings.ContainsKey(key))
				_profile.RemoveSetting(key);
		}
		Load();
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

	/// <summary>A run message about the player's own runs - also used for whoever they spectate (players and replay bots)</summary>
	internal bool ChatOwn(ChatEvent ev) => _chatOwn[ev];

	internal void SetChatOwn(ChatEvent ev, bool on)
	{
		_chatOwn[ev] = on;
		Save(KeyChatOwnPrefix + ev.ToString().ToLowerInvariant(), on);
	}

	/// <summary>A run message about other players' runs</summary>
	internal bool ChatOthers(ChatEvent ev) => _chatOthers[ev];

	internal void SetChatOthers(ChatEvent ev, bool on)
	{
		_chatOthers[ev] = on;
		Save(KeyChatOthersPrefix + ev.ToString().ToLowerInvariant(), on);
	}

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

	// ---- Ghost & path (off by default) ----

	internal bool GhostPath { get => _ghostPath; set { _ghostPath = value; Save(KeyGhostPath, value); } }

	/// <summary>Jump squares and crouch triangles on the path (on, once the path is)</summary>
	internal bool GhostMarkers { get => _ghostMarkers; set { _ghostMarkers = value; Save(KeyGhostMarkers, value); } }

	internal bool GhostOrb { get => _ghostOrb; set { _ghostOrb = value; Save(KeyGhostOrb, value); } }

	internal GhostTarget GhostTarget
	{
		get => _ghostTarget;
		set
		{
			_ghostTarget = value;
			_profile.SetSetting(KeyGhostTarget, value.ToString().ToLowerInvariant());
		}
	}

	/// <summary>How much of the path ahead is drawn, in seconds of the target's run</summary>
	internal int GhostSeconds
	{
		get => _ghostSeconds;
		set
		{
			_ghostSeconds = GhostSecondSteps.Contains(value) ? value : DefaultGhostSeconds;
			_profile.SetSetting(KeyGhostSeconds, _ghostSeconds.ToString());
		}
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

	// ---- Pace, center HUD ----

	/// <summary>Live pace under the splits (off by default) - against the splits panel's target</summary>
	internal bool HudPace { get => _hudPace; set { _hudPace = value; Save(KeyHudPace, value); } }

	/// <summary>With the pace: energy and speed difference to the reference at the same spot (off by default)</summary>
	internal bool HudPaceEnergy { get => _hudPaceEnergy; set { _hudPaceEnergy = value; Save(KeyHudPaceEnergy, value); } }

	/// <summary>The center HUD on / off (off by default)</summary>
	internal bool CenterHud { get => _centerHud; set { _centerHud = value; Save(KeyCenterHud, value); } }

	/// <summary>Center fields with their labels</summary>
	internal bool CenterLabels { get => _centerLabels; set { _centerLabels = value; Save(KeyCenterLabels, value); } }

	/// <summary>Size of a center line, 1-5</summary>
	internal int CenterSize(int line) => _centerSizes[line];

	internal void SetCenterSize(int line, int size)
	{
		_centerSizes[line] = Math.Clamp(size, 1, CenterSizes);
		_profile.SetSetting(CenterSizeKey(line), _centerSizes[line].ToString());
	}

	/// <summary>Steps up (-) / down (+) from a center line's place, 10px each</summary>
	internal int CenterOffset(int line) => _centerOffsets[line];

	internal void SetCenterOffset(int line, int offset)
	{
		_centerOffsets[line] = Math.Clamp(offset, -CenterMaxOffset, CenterMaxOffset);
		_profile.SetSetting(CenterOffsetKey(line), _centerOffsets[line].ToString());
	}

	/// <summary>The center HUD layout as stored (see DefaultCenterFields)</summary>
	internal string CenterFields => _centerFields;

	/// <summary>The center HUD's three lines (above, at, below the crosshair) - read by the HUD every update</summary>
	internal IReadOnlyList<IReadOnlyList<string>> CenterLines { get; private set; } = [];

	/// <summary>Sets the center HUD layout - false (nothing changed) when it doesn't fit</summary>
	internal bool TrySetCenterFields(string fields)
	{
		if (!HudLayoutSpec.Center.TryParse(fields, out var lines))
			return false;

		_centerFields = HudLayoutSpec.Center.Format(lines);
		CenterLines = lines;
		_profile.SetSetting(KeyCenterFields, _centerFields);
		return true;
	}

	/// <summary>The center speed's own colour mode - null: the same as the HUD speed (default)</summary>
	internal SpeedColorMode? CenterSpeedColor
	{
		get => _centerSpeedColor;
		set
		{
			_centerSpeedColor = value;
			_profile.SetSetting(KeyCenterSpeedColor, value?.ToString().ToLowerInvariant() ?? "follow");
		}
	}

	/// <summary>Key of SpeedStaticColors for the center speed's own static colour</summary>
	internal string CenterSpeedColorStatic
	{
		get => _centerSpeedColorStatic;
		set
		{
			if (!SpeedStaticColors.Any(c => c.Key == value))
				return;
			_centerSpeedColorStatic = value;
			_profile.SetSetting(KeyCenterSpeedColorStatic, value);
		}
	}

	/// <summary>The center HUD in the monospace font (digits don't shift)</summary>
	internal bool CenterSpeedMono { get => _centerSpeedMono; set { _centerSpeedMono = value; Save(KeyCenterSpeedMono, value); } }

	internal SpeedColorMode SpeedColor
	{
		get => _speedColor;
		set
		{
			_speedColor = value;
			_profile.SetSetting(KeySpeedColor, value.ToString().ToLowerInvariant());
		}
	}

	/// <summary>Key of SpeedStaticColors used with SpeedColorMode.Static</summary>
	internal string SpeedColorStatic
	{
		get => _speedColorStatic;
		set
		{
			if (!SpeedStaticColors.Any(c => c.Key == value))
				return;
			_speedColorStatic = value;
			_profile.SetSetting(KeySpeedColorStatic, value);
		}
	}

	internal TimerWarning TimerWarning
	{
		get => _timerWarning;
		set
		{
			_timerWarning = value;
			_profile.SetSetting(KeyTimerWarning, value.ToString().ToLowerInvariant());
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

	/// <summary>The bottom HUD layout as stored (see DefaultHudFields)</summary>
	internal string HudFields => _hudFields;

	/// <summary>The bottom HUD layout, parsed - read by the HUD every update</summary>
	internal IReadOnlyList<IReadOnlyList<string>> HudRows { get; private set; } = [];

	/// <summary>
	/// Sets the bottom HUD layout - false (nothing changed) when it doesn't fit the HUD.
	/// </summary>
	internal bool TrySetHudFields(string fields)
	{
		if (!HudLayoutSpec.Bottom.TryParse(fields, out var rows))
			return false;

		_hudFields = HudLayoutSpec.Bottom.Format(rows);
		HudRows = rows;
		_profile.SetSetting(KeyHudFields, _hudFields);
		return true;
	}
}
