using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// One page of a panel (!admin, !options): a title (breadcrumb part) and its rows, rebuilt on every
/// refresh. State holds what the page loaded from the database (see PanelContext.Load).
/// </summary>
internal sealed class PanelPage(string title, Func<PanelContext, List<HudMenuItem>> build)
{
	internal string Title { get; } = title;
	internal Func<PanelContext, List<HudMenuItem>> Build { get; } = build;
	internal Dictionary<string, object?> State { get; } = new();
}

/// <summary>A tab of a panel - Flag is the permission it needs (null = everyone)</summary>
internal sealed record PanelSection(string Name, string? Flag, Func<PanelPage> Root);

/// <summary>
/// A player's open panel: a page stack per tab, the active tab and the last status line.
/// Main thread only.
/// </summary>
internal sealed class PanelSession
{
	internal PanelSession(string title, Player player, List<PanelSection> sections)
	{
		Title = title;
		Player = player;
		Sections = sections;
		Stacks = sections.Select(s => new List<PanelPage> { s.Root() }).ToList();
	}

	/// <summary>First part of the popup title, e.g. "Admin"</summary>
	internal string Title { get; }
	internal Player Player { get; }
	internal List<PanelSection> Sections { get; }
	internal List<List<PanelPage>> Stacks { get; }
	internal int ActiveTab { get; set; }
	internal string Status { get; set; } = "";

	/// <summary>The menu last shown - updates only go to the popup while it still shows this one</summary>
	internal HudMenu? Shown { get; set; }

	internal List<PanelPage> Stack => Stacks[ActiveTab];
	internal PanelPage Top => Stack[^1];
	internal PanelSection Section => Sections[ActiveTab];
}

public partial class SurfTimer
{
	// ---- Rendering / navigation of panels ----

	private HudMenu PanelBuildMenu(PanelSession session)
	{
		var tabs = new List<HudMenuTab>();
		for (int i = 0; i < session.Sections.Count; i++)
		{
			List<HudMenuItem> items;
			if (i == session.ActiveTab)
			{
				var ctx = new PanelContext(this, session, session.Top);
				try
				{
					items = session.Top.Build(ctx);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "[Panel] Building page '{Page}' of {Panel} failed", session.Top.Title, session.Title);
					items = [HudMenuItem.Info("This page failed to load", "", ex.Message)];
				}
				if (items.Count == 0)
					items.Add(HudMenuItem.Info("Nothing here", ""));
			}
			else
			{
				items = [HudMenuItem.Info("…", "")]; // Built when the tab is opened
			}
			tabs.Add(new HudMenuTab(session.Sections[i].Name, items));
		}

		var crumbs = session.Stack.Skip(1).Select(p => p.Title).ToList();
		if (crumbs.Count > 2)
			crumbs = ["…", .. crumbs.TakeLast(2)];
		string title = crumbs.Count == 0 ? session.Title : $"{session.Title} › " + string.Join(" › ", crumbs);

		return new HudMenu(title, tabs)
		{
			ActiveTab = session.ActiveTab,
			Status = session.Status,
			OnBack = session.Stack.Count > 1 ? _ => PanelBack(session) : null,
			OnTabChanged = (_, tab) => PanelSwitchTab(session, tab),
		};
	}

	/// <summary>
	/// Rebuilds the panel after a change - in place when the popup still shows it.
	/// </summary>
	internal void PanelRefresh(PanelSession session, bool resetPage = false)
	{
		if (!session.Player.Controller.IsValid)
			return;

		var previous = session.Shown;
		var menu = PanelBuildMenu(session);
		MenuPresenter.Update(session.Player, menu, resetPage, open => open != null && ReferenceEquals(open, previous));
		if (!MenuPresenter.UsesPopup || ReferenceEquals(session.Player.HUD.Menu, menu))
			session.Shown = menu;
	}

	/// <summary>
	/// Shows the panel (again - e.g. after a chat prompt closed it).
	/// </summary>
	internal void PanelReopen(PanelSession session)
	{
		if (!session.Player.Controller.IsValid)
			return;

		var menu = PanelBuildMenu(session);
		MenuPresenter.Show(session.Player, menu);
		session.Shown = menu;
	}

	internal void PanelPush(PanelSession session, PanelPage page)
	{
		session.Stack.Add(page);
		session.Status = "";
		PanelRefresh(session, resetPage: true);
	}

	internal void PanelBack(PanelSession session, string status = "")
	{
		if (session.Stack.Count > 1)
			session.Stack.RemoveAt(session.Stack.Count - 1);
		session.Status = status;
		PanelRefresh(session, resetPage: true);
	}

	/// <summary>
	/// Leaves a page after its background work finished - only if it's still the one shown (the player
	/// may have moved on meanwhile).
	/// </summary>
	internal void PanelBackFrom(PanelSession session, PanelPage page, string status)
	{
		if (ReferenceEquals(session.Top, page))
		{
			PanelBack(session, status);
			return;
		}
		session.Status = status;
		PanelRefresh(session);
	}

	private void PanelSwitchTab(PanelSession session, int tab)
	{
		if (tab < 0 || tab >= session.Sections.Count)
			return;

		// A tab starts at its root page with fresh data
		session.ActiveTab = tab;
		session.Stacks[tab] = [session.Sections[tab].Root()];
		session.Status = "";
		PanelRefresh(session, resetPage: true);
	}

	/// <summary>
	/// Asks for a value in chat; the panel comes back afterwards (also on !cancel).
	/// </summary>
	/// <param name="apply">Gets the text - returns an error to ask again, or null when done</param>
	internal void PanelAsk(PanelSession session, string prompt, Func<string, string?> apply)
	{
		ChatPrompt.Ask(session.Player, prompt, (_, text) =>
		{
			string? error = apply(text);
			if (error == null)
				PanelReopen(session);
			return error;
		}, _ => PanelReopen(session));
	}
}
