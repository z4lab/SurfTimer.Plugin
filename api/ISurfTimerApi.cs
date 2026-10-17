using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;

namespace SurfTimer.Api;

/// <summary>
/// The capability addons get SurfTimer through. Get it in OnAllPluginsLoaded:
/// <code>var timer = SurfTimerApi.Capability.Get();</code> - null when SurfTimer.Plugin isn't loaded.
/// </summary>
public static class SurfTimerApi
{
	public static PluginCapability<ISurfTimerApi> Capability { get; } = new("surftimer:api");
}

/// <summary>
/// SurfTimer.Plugin for addons (e.g. SurfTimer.MapChooser): the top HUD, menus, admin panel tabs, map changes and
/// run events. Main thread only - events are raised on the main thread too.
/// </summary>
public interface ISurfTimerApi
{
	/// <summary>The loaded map, null between maps / before its data is loaded</summary>
	SurfMapInfo? CurrentMap { get; }

	// ---- Top HUD (custom HUD only) ----

	/// <summary>
	/// Shows a value in the top HUD for everyone, e.g. ("Time left", "12:34"), on one of its rows (HudValue.Row). key
	/// keeps values of several addons apart; null clears it.
	/// </summary>
	void SetTopHud(string key, HudValue? value);

	/// <summary>A value for one player only - wins over the global value of the same key. null clears it.</summary>
	void SetTopHudFor(CCSPlayerController player, string key, HudValue? value);

	// ---- HUD fields and center messages (custom HUD only) ----

	/// <summary>
	/// Adds a live value players can place in their bottom HUD and center HUD (!options - HUD), like the timer's own
	/// fields. key: "addon.name" (lowercase letters, digits, _), stored in players' layouts - keep it stable. value is
	/// asked for every HUD update (16x a second, main thread) with the viewer and returns what they see - null or an empty
	/// Value shows nothing. HudValue.Label and Row are ignored (label is the field's name). Registering a key again
	/// replaces it.
	/// </summary>
	void RegisterHudField(string key, string label, string description, Func<CCSPlayerController, HudValue?> value);

	void UnregisterHudField(string key);

	/// <summary>
	/// Shows a short text in a line of the center HUD (around the crosshair) for some seconds (0.5 - 30) - for one player,
	/// or everyone when player is null. The line shows the text instead of its fields, then goes back to them. Auto takes
	/// a line without fields (at, below, above the crosshair), else the one at the crosshair. On a line with a message
	/// already, the newest shows; an older one comes back if it still has time left. size: 1 - 5. Players without the
	/// custom HUD get CS2's center print.
	/// </summary>
	CenterMessage ShowCenterMessage(CCSPlayerController? player, string text, float seconds, CenterLine line = CenterLine.Auto,
		HudColor color = HudColor.Default, int size = 3);

	/// <summary>Takes a center message down before its time is up</summary>
	void ClearCenterMessage(CenterMessage message);

	// ---- Menus (the custom HUD popup, or the chat menu without it) ----

	void ShowMenu(CCSPlayerController player, ApiMenu menu);

	/// <summary>
	/// Replaces a menu shown with ShowMenu in place (live vote results). Nothing happens when the player closed it
	/// or opened another one meanwhile. Without the popup the chat menu is shown again.
	/// </summary>
	void UpdateMenu(CCSPlayerController player, ApiMenu menu);

	/// <summary>Closes the popup if it still shows a menu of this API</summary>
	void CloseMenu(CCSPlayerController player);

	/// <summary>Whether menus show as the clickable popup (false = chat menus, !1 !2 ...)</summary>
	bool UsesPopupMenus { get; }

	// ---- Admin panel (!surfadmin) ----

	/// <summary>
	/// Adds a page to the admin panel: a row in its Server tab (the popup has no room for more tabs), shown to
	/// admins with flag. sub is the row's dim text. Registering a name again replaces the page.
	/// </summary>
	void RegisterAdminPage(string name, string flag, string sub, Func<ApiPanelPage> root);

	void UnregisterAdminPage(string name);

	// ---- Players and runs ----

	/// <summary>Whether the player is in a timed run that counts (not practice mode)</summary>
	bool IsRunning(CCSPlayerController player);

	/// <summary>
	/// Plays a sound to this player only, at their own volume - nothing when they turned sounds off (!quake) or this
	/// category off (!options - Sound). A CS2 sound event name gets the volume and pitch; a file path
	/// ("sounds/....vsnd") is played with the client's "play" at full volume.
	/// </summary>
	/// <param name="category">A key from RegisterSoundCategory - null: only the main switch and the volume apply</param>
	void PlaySound(CCSPlayerController player, string sound, float pitch = 1f, string? category = null);

	/// <summary>
	/// Adds a kind of sound players can turn on / off in !options - Sound, after the timer's own. defaultOn: sounds about
	/// the player themselves should be on, about others off. Registering a key again replaces it.
	/// </summary>
	void RegisterSoundCategory(string key, string label, string sub, bool defaultOn = true);

	/// <summary>Whether the player is in a start zone (map, stage or bonus start)</summary>
	bool IsInStartZone(CCSPlayerController player);

	/// <summary>
	/// A map run was saved (not stages / bonuses). FirstCompletion: the player had no time on this map / style
	/// before.
	/// </summary>
	event Action<MapFinish>? MapFinished;

	// ---- Help (!help) ----

	/// <summary>
	/// Lists a command in !help (the popup's tab of its group, and !help &lt;command&gt;). Registering a command again
	/// replaces it. Players only see it when they have entry.Flag (root has all) - admin entries without a flag need any
	/// admin permission.
	/// </summary>
	void RegisterHelp(HelpEntry entry);

	void UnregisterHelp(string command);

	// ---- Chat ----

	/// <summary>
	/// Lets admins change an addon's chat prefix in !surfadmin - Server - Chat (saved in chat_settings.json, {color}
	/// tags). defaultPrefix is the addon's own (with colors applied, e.g. from its language file).
	/// </summary>
	void RegisterChatPrefix(string key, string label, string defaultPrefix);

	/// <summary>The addon's chat prefix: the admin's (colors applied), else the default it registered</summary>
	string ChatPrefix(string key);

	/// <summary>
	/// A player's role tag in chat - the {role} placeholder, right before the name when the format has none. The provider
	/// returns the tag with {color} tags (e.g. "{purple}[Admin]"), or null / "" for none. One provider (the last set
	/// wins), null removes it. Players can hide their own tag (!options - Chat).
	/// </summary>
	void SetChatTagProvider(Func<CCSPlayerController, string?>? provider);

	// ---- Server ----

	/// <summary>
	/// Changes the map like !changemap: by its workshop id when known, else from the workshop collection.
	/// Announced in chat; a change that doesn't happen is reported.
	/// </summary>
	void ChangeLevel(string mapName);

	/// <summary>
	/// Keeps a cvar at a value: applied now and again every time SurfTimer runs server_settings.cfg and the map's
	/// cvar overrides (e.g. mp_timelimit 0 for a map chooser that runs its own timer).
	/// </summary>
	void ForceCvar(string name, string value);

	/// <summary>Stops forcing a cvar (the next server_settings.cfg run sets it back)</summary>
	void ReleaseCvar(string name);
}

/// <param name="Id">maps.id - 0 when the map isn't stored yet</param>
/// <param name="Tier">Tier of the map course, 0 = none</param>
public sealed record SurfMapInfo(int Id, string Name, int Tier, bool Ranked, ulong? WorkshopId);

/// <param name="Style">Style id (0 = normal)</param>
/// <param name="FirstCompletion">The player had no time on this map / style before</param>
/// <param name="Improved">A new personal best (always true for the first completion)</param>
public sealed record MapFinish(CCSPlayerController Player, ulong SteamId64, int MapId, int Style, int RunTimeTicks,
	bool FirstCompletion, bool Improved);

/// <summary>The colours the custom HUD layout has (Nord palette)</summary>
public enum HudColor
{
	Default,
	Blue,
	Purple,
	Green,
	Indigo,
	Gold,
	Grey,
	Red,
}

/// <summary>
/// The top HUD's rows: one big, three small. Empty rows collapse. The third and fourth need the HUD addon with four top
/// rows - players with an older one don't see them.
/// </summary>
public enum HudRow
{
	/// <summary>Map name, tier, stage - after the mode flags</summary>
	Main,

	/// <summary>PB, rank and WR (or the replay) - after those</summary>
	Second,

	/// <summary>Addons - the map chooser's time left, vote and next map</summary>
	Third,

	/// <summary>Addons</summary>
	Fourth,
}

/// <summary>The tabs of !help, from basic to advanced</summary>
public enum HelpGroup
{
	/// <summary>Getting around: reset, stages, bonuses, map votes</summary>
	Basics,
	/// <summary>Savelocs, start positions, repeat</summary>
	Practice,
	/// <summary>Records, ranks, profiles</summary>
	Records,
	/// <summary>Spectating and replays</summary>
	Spectate,
	/// <summary>Settings, shortcuts, weapons</summary>
	Options,
	/// <summary>Only shown to admins</summary>
	Admin,
}

/// <param name="Command">The chat command without "!" / "css_", e.g. "rtv"</param>
/// <param name="Text">One line: what it does</param>
/// <param name="Usage">Its arguments, e.g. "[map]" ([] optional, &lt;&gt; needed)</param>
/// <param name="Aliases">Other names, without "!"</param>
/// <param name="Detail">Longer text for !help &lt;command&gt;</param>
/// <param name="Flag">Only players with this permission see it</param>
/// <param name="Order">Place in its tab - lower first (the timer's own use 0 - 99)</param>
public sealed record HelpEntry(string Command, string Text, HelpGroup Group = HelpGroup.Basics, string Usage = "",
	string[]? Aliases = null, string Detail = "", string? Flag = null, int Order = 100);

/// <summary>The center HUD's lines</summary>
public enum CenterLine
{
	/// <summary>A line without fields, else the one at the crosshair</summary>
	Auto,
	Above,
	Crosshair,
	Below,
}

/// <summary>A message shown with ShowCenterMessage - ClearCenterMessage takes it down early</summary>
public sealed record CenterMessage(long Id);

/// <param name="Label">Small label before the value ("Time left"), empty for none</param>
/// <param name="Value">The value ("12:34")</param>
/// <param name="Monospace">The value in the monospace font - for numbers that keep changing (a countdown)</param>
public sealed record HudValue(string Label, string Value, HudColor Color = HudColor.Default, HudRow Row = HudRow.Main,
	bool Monospace = false);
