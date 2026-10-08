using CounterStrikeSharp.API.Core;

namespace SurfTimer.Api;

/// <summary>
/// One page of an addon's admin panel tab: a title (breadcrumb part) and its rows, rebuilt on every refresh. Build the
/// rows with the context's factories - they check the tab's permission again before an action runs.
/// </summary>
public sealed class ApiPanelPage(string title, Func<IApiPanelContext, List<ApiMenuItem>> build)
{
	public string Title { get; } = title;
	public Func<IApiPanelContext, List<ApiMenuItem>> Build { get; } = build;
}

/// <summary>
/// What a page builder gets - the same row factories and helpers SurfTimer's own admin pages use. Main thread.
/// </summary>
public interface IApiPanelContext
{
	CCSPlayerController Player { get; }

	/// <summary>The status line under the title</summary>
	string Status { get; set; }

	// ---- Rows ----

	/// <summary>Opens a sub-page</summary>
	ApiMenuItem Nav(string text, string value, string sub, Func<ApiPanelPage> open);

	/// <summary>An on / off switch - set gets the new state</summary>
	ApiMenuItem Toggle(string text, bool on, string sub, Action<bool> set);

	/// <summary>Runs an action, then refreshes the panel</summary>
	ApiMenuItem Act(string text, string value, string sub, Action act, bool closes = false);

	/// <summary>A dangerous action behind a confirmation page (summary row, then confirmText / Cancel)</summary>
	ApiMenuItem Danger(string text, string sub, string summary, string confirmText, Action<IApiPanelContext> onConfirm);

	/// <summary>Asks for a value in chat - apply returns an error to ask again, or null</summary>
	ApiMenuItem Ask(string text, string value, string sub, string prompt, Func<string, string?> apply);

	/// <summary>Back to the previous page</summary>
	ApiMenuItem Back(string text = "Cancel");

	// ---- Data / navigation ----

	/// <summary>Data of this page, loaded once in the background: null while loading, then the result</summary>
	T? Load<T>(string key, Func<Task<T>> load) where T : class?;

	/// <summary>Drops loaded data so the next refresh loads it again</summary>
	void Reload(params string[] keys);

	/// <summary>
	/// Runs work in the background ("working…" meanwhile), then shows its result as the status. then runs afterwards on
	/// the main thread, before the refresh (e.g. Reload of what changed).
	/// </summary>
	void Run(string working, Func<Task<string>> work, Action<string>? then = null);

	/// <summary>Shows a status line and refreshes</summary>
	void Done(string status);

	void Refresh();

	/// <summary>Writes an admin_actions row (Audit tab)</summary>
	void Audit(string action, string targetType, long? targetId, string details);
}
