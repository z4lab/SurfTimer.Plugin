using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// What a panel page builder gets: the session and page, plus row factories. Every row action checks
/// the section's permission (if it has one) again before it runs - hidden rows aren't the only protection.
/// </summary>
internal sealed class PanelContext(SurfTimer plugin, PanelSession session, PanelPage page)
{
	private static readonly object Loading = new();
	private sealed record Failure(string Message);

	internal SurfTimer Plugin { get; } = plugin;
	internal PanelSession Session { get; } = session;
	internal PanelPage Page { get; } = page;
	internal Player Player => Session.Player;
	internal int Style => Player.Timer.Style;

	private bool Allowed()
	{
		if (AdminPermissions.Has(Player.Controller, Session.Section.Flag))
			return true;

		Session.Status = LocalizationService.LocalizerNonNull["admin_no_access"];
		Plugin.PanelRefresh(Session);
		return false;
	}

	// ---- Rows ----

	internal static HudMenuItem Info(string text, string value = "", string sub = "") => HudMenuItem.Info(text, value, sub);

	/// <summary>Opens a sub-page</summary>
	internal HudMenuItem Nav(string text, string value, string sub, Func<PanelPage> open) =>
		new(text, _ =>
		{
			if (Allowed())
				Plugin.PanelPush(Session, open());
		}, sub, () => value)
		{ KeepOpen = true, Opens = true };

	/// <summary>An on / off switch - set gets the new state</summary>
	internal HudMenuItem Toggle(string text, bool on, string sub, Action<bool> set) =>
		new(text, _ =>
		{
			if (!Allowed())
				return;
			set(!on);
			Plugin.PanelRefresh(Session);
		}, sub, () => on ? "ON" : "OFF")
		{ KeepOpen = true, Style = on ? HudMenuItemStyle.On : HudMenuItemStyle.Off };

	/// <summary>
	/// Runs an action and refreshes the panel. closes = the popup closes first (e.g. spectating).
	/// </summary>
	internal HudMenuItem Act(string text, string value, string sub, Action act, bool closes = false) =>
		new(text, _ =>
		{
			if (!Allowed())
				return;
			act();
			if (!closes)
				Plugin.PanelRefresh(Session);
		}, sub, () => value)
		{ KeepOpen = !closes };

	/// <summary>A dangerous action - opens its confirmation page</summary>
	internal HudMenuItem Danger(string text, string sub, Func<PanelPage> confirm) =>
		Nav(text, "", sub, confirm) with { Style = HudMenuItemStyle.Danger };

	/// <summary>Asks for a value in chat - apply returns an error to ask again, or null</summary>
	internal HudMenuItem Ask(string text, string value, string sub, string prompt, Func<string, string?> apply) =>
		new(text, _ =>
		{
			if (Allowed())
				Plugin.PanelAsk(Session, prompt, apply);
		}, sub, () => value)
		{ KeepOpen = true, Opens = true };

	/// <summary>Back to the previous page</summary>
	internal HudMenuItem Back(string text = "Cancel") =>
		new(text, _ => Plugin.PanelBack(Session), "", null) { KeepOpen = true };

	// ---- Data ----

	/// <summary>
	/// Data of this page, loaded once in the background: null while loading (the page shows a loading
	/// row), then the result. Failures are logged and shown as the status line.
	/// </summary>
	internal T? Load<T>(string key, Func<Task<T>> load) where T : class?
	{
		if (Page.State.TryGetValue(key, out var value))
			return value as T;

		Page.State[key] = Loading;
		var page = Page;
		var session = Session;
		var plugin = Plugin;
		_ = Task.Run(async () =>
		{
			object? result;
			try
			{
				result = await load();
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "[Panel] Loading '{Key}' of page '{Page}' failed", key, page.Title);
				result = new Failure(ex.Message);
			}

			Server.NextFrame(() =>
			{
				page.State[key] = result;
				if (result is Failure failure)
					session.Status = $"Loading failed: {failure.Message}";
				plugin.PanelRefresh(session);
			});
		});
		return null;
	}

	internal bool IsLoading(string key) => Page.State.TryGetValue(key, out var value) && ReferenceEquals(value, Loading);

	/// <summary>Drops loaded data so the next refresh loads it again</summary>
	internal void Reload(params string[] keys)
	{
		foreach (var key in keys)
			Page.State.Remove(key);
	}

	internal static HudMenuItem LoadingRow() => Info("Loading…");

	/// <summary>
	/// Runs work in the background (status "working…" meanwhile), then shows its result as the status.
	/// then runs afterwards on the main thread (e.g. going back a page).
	/// </summary>
	internal void Run(string working, Func<Task<string>> work, Action<string>? then = null)
	{
		var session = Session;
		var plugin = Plugin;
		session.Status = working;
		plugin.PanelRefresh(session);

		_ = Task.Run(async () =>
		{
			string status;
			try
			{
				status = await work();
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "[Panel] '{Working}' failed", working);
				status = $"Failed: {ex.Message}";
			}

			Server.NextFrame(() =>
			{
				session.Status = status;
				if (then != null)
					then(status);
				else
					plugin.PanelRefresh(session);
			});
		});
	}

	/// <summary>
	/// A confirmation page: what will happen (summary rows, may Load), then Confirm and Cancel. Confirm
	/// goes back to the previous page and runs the action.
	/// </summary>
	internal static PanelPage Confirm(string title, Func<PanelContext, List<HudMenuItem>> summary, string confirmText, Action<PanelContext> onConfirm) =>
		new(title, ctx =>
		{
			var rows = summary(ctx);
			if (rows.Any(r => r.Text == "Loading…"))
				return rows;

			rows.Add(new HudMenuItem(confirmText, _ =>
			{
				if (!ctx.Allowed())
					return;
				ctx.Plugin.PanelBack(ctx.Session);
				// The action runs with the page it came from (for reloads / going back)
				onConfirm(new PanelContext(ctx.Plugin, ctx.Session, ctx.Session.Top));
			}, "", null)
			{ KeepOpen = true, Style = HudMenuItemStyle.Danger });
			rows.Add(ctx.Back());
			return rows;
		});

	internal void Audit(string action, string targetType, long? targetId, string details) =>
		Plugin.AdminAudit(Session, action, targetType, targetId, details);

	/// <summary>Shows a status line and refreshes</summary>
	internal void Done(string status)
	{
		Session.Status = status;
		Plugin.PanelRefresh(Session);
	}

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Panel");

	internal static CCSPlayerController? Valid(CCSPlayerController? controller) =>
		controller != null && controller.IsValid ? controller : null;
}
