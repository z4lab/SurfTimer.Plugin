using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// One page of the admin panel: a title (breadcrumb part) and its rows, rebuilt on every refresh.
/// State holds what the page loaded from the database (see AdminContext.Load).
/// </summary>
internal sealed class AdminPage(string title, Func<AdminContext, List<HudMenuItem>> build)
{
	internal string Title { get; } = title;
	internal Func<AdminContext, List<HudMenuItem>> Build { get; } = build;
	internal Dictionary<string, object?> State { get; } = new();
}

/// <summary>A tab of the panel - shown to admins with its permission</summary>
internal sealed record AdminSection(string Name, string Flag, Func<AdminPage> Root);

/// <summary>
/// A player's open admin panel: a page stack per tab, the active tab and the last status line.
/// Main thread only.
/// </summary>
internal sealed class AdminSession
{
	internal AdminSession(Player player, List<AdminSection> sections)
	{
		Player = player;
		Sections = sections;
		Stacks = sections.Select(s => new List<AdminPage> { s.Root() }).ToList();
	}

	internal Player Player { get; }
	internal List<AdminSection> Sections { get; }
	internal List<List<AdminPage>> Stacks { get; }
	internal int ActiveTab { get; set; }
	internal string Status { get; set; } = "";

	/// <summary>The menu last shown - updates only go to the popup while it still shows this one</summary>
	internal HudMenu? Shown { get; set; }

	internal List<AdminPage> Stack => Stacks[ActiveTab];
	internal AdminPage Top => Stack[^1];
	internal AdminSection Section => Sections[ActiveTab];
}

public partial class SurfTimer
{
	internal static readonly DateTime LoadedAt = DateTime.UtcNow;

	[ConsoleCommand("css_admin", "Open the SurfTimer admin panel.")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void AdminCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var admin))
			return;

		var sections = AdminSections().Where(s => AdminPermissions.Has(player, s.Flag)).ToList();
		if (sections.Count == 0)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["admin_no_access"]}");
			return;
		}

		// Keep the previous session (same tab / page) unless the rights changed
		var session = admin.Admin;
		if (session == null || !session.Sections.Select(s => s.Flag).SequenceEqual(sections.Select(s => s.Flag)))
			admin.Admin = session = new AdminSession(admin, sections);

		session.Status = "";
		AdminReopen(session);
	}

	/// <summary>All sections in tab order - the pages live in ST-Admin/Pages.</summary>
	private List<AdminSection> AdminSections() =>
	[
		new("Map", AdminPermissions.Map, AdminMapRoot),
		new("Records", AdminPermissions.Records, AdminRecordsRoot),
		new("Players", AdminPermissions.Players, AdminPlayersRoot),
		new("Server", AdminPermissions.Server, AdminServerRoot),
		new("Database", AdminPermissions.Database, AdminDatabaseRoot),
		new("Audit", AdminPermissions.Audit, AdminAuditRoot),
	];

	// ---- Rendering / navigation ----

	private HudMenu AdminBuildMenu(AdminSession session)
	{
		var tabs = new List<HudMenuTab>();
		for (int i = 0; i < session.Sections.Count; i++)
		{
			List<HudMenuItem> items;
			if (i == session.ActiveTab)
			{
				var ctx = new AdminContext(this, session, session.Top);
				try
				{
					items = session.Top.Build(ctx);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "[Admin] Building page '{Page}' failed", session.Top.Title);
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
		string title = crumbs.Count == 0 ? "Admin" : "Admin › " + string.Join(" › ", crumbs);

		return new HudMenu(title, tabs)
		{
			ActiveTab = session.ActiveTab,
			Status = session.Status,
			OnBack = session.Stack.Count > 1 ? _ => AdminBack(session) : null,
			OnTabChanged = (_, tab) => AdminSwitchTab(session, tab),
		};
	}

	/// <summary>
	/// Rebuilds the panel after a change - in place when the popup still shows it.
	/// </summary>
	internal void AdminRefresh(AdminSession session, bool resetPage = false)
	{
		if (!session.Player.Controller.IsValid)
			return;

		var previous = session.Shown;
		var menu = AdminBuildMenu(session);
		MenuPresenter.Update(session.Player, menu, resetPage, open => open != null && ReferenceEquals(open, previous));
		if (!MenuPresenter.UsesPopup || ReferenceEquals(session.Player.HUD.Menu, menu))
			session.Shown = menu;
	}

	/// <summary>
	/// Shows the panel again (e.g. after a chat prompt closed it).
	/// </summary>
	internal void AdminReopen(AdminSession session)
	{
		if (!session.Player.Controller.IsValid)
			return;

		var menu = AdminBuildMenu(session);
		MenuPresenter.Show(session.Player, menu);
		session.Shown = menu;
	}

	internal void AdminPush(AdminSession session, AdminPage page)
	{
		session.Stack.Add(page);
		session.Status = "";
		AdminRefresh(session, resetPage: true);
	}

	internal void AdminBack(AdminSession session, string status = "")
	{
		if (session.Stack.Count > 1)
			session.Stack.RemoveAt(session.Stack.Count - 1);
		session.Status = status;
		AdminRefresh(session, resetPage: true);
	}

	/// <summary>
	/// Leaves a page after its background work finished - only if it's still the one shown (the admin
	/// may have moved on meanwhile).
	/// </summary>
	internal void AdminBackFrom(AdminSession session, AdminPage page, string status)
	{
		if (ReferenceEquals(session.Top, page))
		{
			AdminBack(session, status);
			return;
		}
		session.Status = status;
		AdminRefresh(session);
	}

	private void AdminSwitchTab(AdminSession session, int tab)
	{
		if (tab < 0 || tab >= session.Sections.Count)
			return;

		// A tab starts at its root page with fresh data
		session.ActiveTab = tab;
		session.Stacks[tab] = [session.Sections[tab].Root()];
		session.Status = "";
		AdminRefresh(session, resetPage: true);
	}

	/// <summary>
	/// Asks for a value in chat; the panel comes back afterwards (also on !cancel).
	/// </summary>
	/// <param name="apply">Gets the text - returns an error to ask again, or null when done</param>
	internal void AdminAsk(AdminSession session, string prompt, Func<string, string?> apply)
	{
		ChatPrompt.Ask(session.Player, prompt, (_, text) =>
		{
			string? error = apply(text);
			if (error == null)
				AdminReopen(session);
			return error;
		}, _ => AdminReopen(session));
	}

	/// <summary>
	/// Writes an admin_actions row (in the background).
	/// </summary>
	internal void AdminAudit(AdminSession session, string action, string targetType, long? targetId, string details)
	{
		int adminId = session.Player.Profile.ID;
		int? mapId = CurrentMap?.ID > 0 ? CurrentMap.ID : null;
		_ = Task.Run(async () =>
		{
			try
			{
				await AdminRepository.LogAsync(adminId, action, targetType, targetId, mapId, details);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[Admin] Writing the audit log failed ({Action})", action);
			}
		});
	}

	/// <summary>
	/// After times were deleted / hidden: points of the affected maps, then the current map's records and
	/// the online players' PBs (ranks move). Off the main thread.
	/// </summary>
	internal async Task AdminAfterTimesChangedAsync(TimeRepository.Affected affected)
	{
		foreach (int mapId in affected.MapIds)
		{
			foreach (int style in Config.Styles)
				await PointsService.RecalculateMapAsync(mapId, style);
		}

		var map = CurrentMap;
		if (map != null && affected.MapIds.Contains(map.ID))
		{
			await map.LoadMapRecordRuns();
			Server.NextFrame(() =>
			{
				foreach (var player in playerList.Values)
				{
					if (player.Controller.IsValid && !player.Controller.IsBot)
						_ = player.Stats.LoadPlayerMapTimesData(player);
				}
			});
		}
	}

	internal static class AdminFormat
	{
		internal static string Time(int ticks) => PlayerHud.FormatTime(ticks, PlayerTimer.TimeFormatStyle.Full);

		internal static string Bytes(long bytes) => bytes switch
		{
			< 1024 => $"{bytes} B",
			< 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
			< 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
			_ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
		};

		internal static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

		internal static string Date(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

		internal static string Ago(DateTime utc)
		{
			var span = DateTime.UtcNow - DateTime.SpecifyKind(utc, DateTimeKind.Utc);
			return span.TotalMinutes < 1 ? "now"
				: span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m"
				: span.TotalDays < 1 ? $"{(int)span.TotalHours}h"
				: span.TotalDays < 60 ? $"{(int)span.TotalDays}d"
				: $"{(int)(span.TotalDays / 30)}mo";
		}

		internal static string Duration(long seconds) => seconds switch
		{
			< 3600 => $"{seconds / 60}m",
			_ => $"{seconds / 3600}h {seconds % 3600 / 60}m",
		};

		internal static string Tier(byte? tier) => tier is > 0 ? $"T{tier}" : "none";
	}
}
