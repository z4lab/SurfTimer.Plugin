using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using SurfTimer.Api;

namespace SurfTimer;

/// <summary>
/// SurfTimer.Api for addons (registered as the "surftimer:api" capability in OnAllPluginsLoaded). What addons set
/// (top HUD values, admin pages, forced cvars) lives in static state here, read by the HUD, the admin panel and
/// ApplyServerSettings. Main thread only.
/// </summary>
internal sealed class SurfTimerApiImpl(SurfTimer plugin, ILogger logger) : ISurfTimerApi
{
	private static SurfTimerApiImpl? _instance;

	// Top HUD values by key - global, and per player (SteamID64) on top
	private static readonly Dictionary<string, HudValue> TopGlobal = new();
	private static readonly Dictionary<ulong, Dictionary<string, HudValue>> TopPerPlayer = new();

	private sealed record AdminPage(string Name, string Flag, string Sub, Func<ApiPanelPage> Root);
	private static readonly List<AdminPage> AdminPages = [];

	private static readonly Dictionary<string, string> ForcedCvars = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Regex CvarName = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

	// The popup menu last shown through the API, per player slot - updates / closes only touch that one
	private readonly Dictionary<int, HudMenu> _shownMenus = new();

	internal static void Register(SurfTimerApiImpl api) => _instance = api;

	/// <summary>Plugin unload - addons' state goes with it</summary>
	internal static void Clear()
	{
		_instance = null;
		TopGlobal.Clear();
		TopPerPlayer.Clear();
		AdminPages.Clear();
		ForcedCvars.Clear();
	}

	// ---- Map ----

	public SurfMapInfo? CurrentMap
	{
		get
		{
			var map = SurfTimer.CurrentMap;
			return map?.Name == null ? null : new SurfMapInfo(map.ID, map.Name, map.Tier, map.Ranked, map.WorkshopId);
		}
	}

	// ---- Top HUD ----

	public void SetTopHud(string key, HudValue? value)
	{
		if (value == null)
			TopGlobal.Remove(key);
		else
			TopGlobal[key] = value;
	}

	public void SetTopHudFor(CCSPlayerController player, string key, HudValue? value)
	{
		if (!player.IsValid)
			return;

		ulong steamId = player.SteamID;
		if (value == null)
		{
			if (TopPerPlayer.TryGetValue(steamId, out var values) && values.Remove(key) && values.Count == 0)
				TopPerPlayer.Remove(steamId);
			return;
		}

		if (!TopPerPlayer.TryGetValue(steamId, out var own))
			TopPerPlayer[steamId] = own = new Dictionary<string, HudValue>();
		own[key] = value;
	}

	/// <summary>The addon values the viewer sees in the top HUD's first row (PlayerHud.TopRows)</summary>
	internal static IEnumerable<PlayerHud.HudElement> TopHudFor(CCSPlayerController viewer)
	{
		if (TopGlobal.Count == 0 && TopPerPlayer.Count == 0)
			return [];

		TopPerPlayer.TryGetValue(viewer.SteamID, out var own);
		var keys = own == null ? TopGlobal.Keys : TopGlobal.Keys.Union(own.Keys);
		var elements = new List<PlayerHud.HudElement>();
		foreach (var key in keys)
		{
			HudValue? value = own != null && own.TryGetValue(key, out var mine) ? mine : TopGlobal.GetValueOrDefault(key);
			if (value == null || value.Value.Length == 0)
				continue;
			elements.Add(new PlayerHud.HudElement(value.Label, value.Value, HexOf(value.Color)));
		}
		return elements;
	}

	/// <summary>The HUD's hex colours (CustomHud.ColorClass knows them)</summary>
	private static string HexOf(HudColor color) => color switch
	{
		HudColor.Blue => "#4FC3F7",
		HudColor.Purple => "#BA68C8",
		HudColor.Green => "#43A047",
		HudColor.Indigo => "#7986CB",
		HudColor.Gold => "#FFD700",
		HudColor.Grey => "#9E9E9E",
		HudColor.Red => "#E53935",
		_ => "",
	};

	// ---- Menus ----

	public bool UsesPopupMenus => MenuPresenter.UsesPopup;

	public void ShowMenu(CCSPlayerController player, ApiMenu menu)
	{
		if (plugin.PlayerOf(player) is not { } target)
			return;

		var hud = ToHud(menu);
		MenuPresenter.Show(target, hud);
		_shownMenus[player.Slot] = hud;
	}

	public void UpdateMenu(CCSPlayerController player, ApiMenu menu)
	{
		if (plugin.PlayerOf(player) is not { } target)
			return;

		_shownMenus.TryGetValue(player.Slot, out var previous);
		var hud = ToHud(menu);
		MenuPresenter.Update(target, hud, resetPage: false, open => open != null && ReferenceEquals(open, previous));
		if (!MenuPresenter.UsesPopup || ReferenceEquals(target.HUD.Menu, hud))
			_shownMenus[player.Slot] = hud;
	}

	public void CloseMenu(CCSPlayerController player)
	{
		if (plugin.PlayerOf(player) is not { } target || !_shownMenus.Remove(player.Slot, out var shown))
			return;

		if (ReferenceEquals(target.HUD.Menu, shown))
			target.HUD.CloseMenu();
	}

	private static HudMenu ToHud(ApiMenu menu) =>
		new(menu.Title, menu.Tabs.Select(t => new HudMenuTab(t.Name, t.Items.Select(ToHud).ToList())))
		{
			Status = menu.Status,
			ActiveTab = menu.ActiveTab,
			OnBack = menu.OnBack,
			OnTabChanged = menu.OnTabChanged,
		};

	private static HudMenuItem ToHud(ApiMenuItem item) =>
		new(item.Text, item.OnSelect, item.Sub, item.Right)
		{
			KeepOpen = item.KeepOpen,
			Opens = item.Opens,
			Style = item.Style switch
			{
				ApiMenuItemStyle.Danger => HudMenuItemStyle.Danger,
				ApiMenuItemStyle.On => HudMenuItemStyle.On,
				ApiMenuItemStyle.Off => HudMenuItemStyle.Off,
				_ => HudMenuItemStyle.Normal,
			},
		};

	private static ApiMenuItem ToApi(HudMenuItem item) =>
		new(item.Text, item.OnSelect, item.Sub, item.Right)
		{
			KeepOpen = item.KeepOpen,
			Opens = item.Opens,
			Style = item.Style switch
			{
				HudMenuItemStyle.Danger => ApiMenuItemStyle.Danger,
				HudMenuItemStyle.On => ApiMenuItemStyle.On,
				HudMenuItemStyle.Off => ApiMenuItemStyle.Off,
				_ => ApiMenuItemStyle.Normal,
			},
		};

	// ---- Admin panel ----

	public void RegisterAdminPage(string name, string flag, string sub, Func<ApiPanelPage> root)
	{
		AdminPages.RemoveAll(p => p.Name == name);
		AdminPages.Add(new AdminPage(name, flag, sub, root));
	}

	public void UnregisterAdminPage(string name) => AdminPages.RemoveAll(p => p.Name == name);

	/// <summary>Rows of the Server tab for the addon pages the admin may open</summary>
	internal static IEnumerable<HudMenuItem> AdminPageRows(PanelContext ctx)
	{
		foreach (var page in AdminPages)
		{
			if (!AdminPermissions.Has(ctx.Player.Controller, page.Flag))
				continue;
			var current = page;
			yield return ctx.Nav(page.Name, "", page.Sub, () => ToPanelPage(current.Root(), current.Flag));
		}
	}

	/// <summary>An addon page as a panel page - rows built with the addon flag checked again on every action</summary>
	private static PanelPage ToPanelPage(ApiPanelPage page, string flag) =>
		new(page.Title, ctx =>
		{
			if (!AdminPermissions.Has(ctx.Player.Controller, flag))
				return [PanelContext.Info(LocalizationService.LocalizerNonNull["admin_no_access"])];
			return page.Build(new PanelContextAdapter(ctx, flag)).Select(ToHud).ToList();
		});

	/// <summary>IApiPanelContext over SurfTimer's own PanelContext</summary>
	private sealed class PanelContextAdapter(PanelContext ctx, string flag) : IApiPanelContext
	{
		public CCSPlayerController Player => ctx.Player.Controller;

		public string Status
		{
			get => ctx.Session.Status;
			set => ctx.Session.Status = value;
		}

		public ApiMenuItem Nav(string text, string value, string sub, Func<ApiPanelPage> open) =>
			ToApi(ctx.Nav(text, value, sub, () => ToPanelPage(open(), flag)));

		public ApiMenuItem Toggle(string text, bool on, string sub, Action<bool> set) =>
			ToApi(ctx.Toggle(text, on, sub, v => { if (Allowed()) set(v); }));

		public ApiMenuItem Act(string text, string value, string sub, Action act, bool closes = false) =>
			ToApi(ctx.Act(text, value, sub, () => { if (Allowed()) act(); }, closes));

		public ApiMenuItem Danger(string text, string sub, string summary, string confirmText, Action<IApiPanelContext> onConfirm) =>
			ToApi(ctx.Danger(text, sub, () => PanelContext.Confirm(text, _ => [PanelContext.Info(summary)], confirmText, c =>
			{
				if (AdminPermissions.Has(c.Player.Controller, flag))
					onConfirm(new PanelContextAdapter(c, flag));
			})));

		public ApiMenuItem Ask(string text, string value, string sub, string prompt, Func<string, string?> apply) =>
			ToApi(ctx.Ask(text, value, sub, prompt, input => Allowed() ? apply(input) : null));

		public ApiMenuItem Back(string text = "Cancel") => ToApi(ctx.Back(text));

		public T? Load<T>(string key, Func<Task<T>> load) where T : class? => ctx.Load(key, load);

		public void Reload(params string[] keys) => ctx.Reload(keys);

		public void Run(string working, Func<Task<string>> work, Action<string>? then = null) =>
			ctx.Run(working, work, then == null ? null : status =>
			{
				then(status);
				Refresh();
			});

		public void Done(string status) => ctx.Done(status);

		public void Refresh() => ctx.Plugin.PanelRefresh(ctx.Session);

		public void Audit(string action, string targetType, long? targetId, string details) =>
			ctx.Audit(action, targetType, targetId, details);

		// The Server tab's flag is checked by PanelContext - this is the addon page's own
		private bool Allowed() => AdminPermissions.Has(ctx.Player.Controller, flag);
	}

	// ---- Players and runs ----

	public bool IsRunning(CCSPlayerController player) =>
		plugin.PlayerOf(player) is { } target && target.Timer.IsRunning && !target.Timer.IsPracticeMode;

	public event Action<MapFinish>? MapFinished;

	/// <summary>A map run was saved (CurrentRun.SaveMapTime) - main thread</summary>
	internal static void RaiseMapFinished(CCSPlayerController controller, int mapId, int style, int runTicks, bool first, bool improved)
	{
		var api = _instance;
		if (api?.MapFinished == null || !controller.IsValid)
			return;

		var finish = new MapFinish(controller, controller.SteamID, mapId, style, runTicks, first, improved);
		foreach (var handler in api.MapFinished.GetInvocationList().Cast<Action<MapFinish>>())
		{
			try
			{
				handler(finish);
			}
			catch (Exception ex)
			{
				api.LogHandlerError(nameof(MapFinished), ex);
			}
		}
	}

	private void LogHandlerError(string eventName, Exception ex) =>
		logger.LogError(ex, "[{Prefix}] An addon's {Event} handler failed", Config.PluginName, eventName);

	// ---- Server ----

	public void ChangeLevel(string mapName) => plugin.ChangeLevelTo(mapName);

	public void ForceCvar(string name, string value)
	{
		// Both go into a server command - nothing like "x; quit" gets through
		if (!CvarName.IsMatch(name) || value.IndexOfAny([';', '"', '\n', '\r']) >= 0)
			throw new ArgumentException($"Invalid cvar '{name}' / value '{value}'");

		ForcedCvars[name] = value;
		Server.ExecuteCommand($"{name} \"{value}\"");
	}

	public void ReleaseCvar(string name) => ForcedCvars.Remove(name);

	/// <summary>After server_settings.cfg and the map's cvar overrides</summary>
	internal static void ApplyForcedCvars()
	{
		foreach (var (name, value) in ForcedCvars)
			Server.ExecuteCommand($"{name} \"{value}\"");
	}
}

public partial class SurfTimer
{
	internal Player? PlayerOf(CCSPlayerController controller) =>
		controller.IsValid && playerList.TryGetValue(controller.UserId ?? 0, out var player) ? player : null;
}
