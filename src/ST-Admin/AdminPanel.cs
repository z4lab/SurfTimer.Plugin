using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	internal static readonly DateTime LoadedAt = DateTime.UtcNow;

	[ConsoleCommand("css_surfadmin", "Open the SurfTimer admin panel.")]
	[ConsoleCommand("css_timeradmin", "Open the SurfTimer admin panel.")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void AdminCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var admin))
			return;

		var session = AdminSessionOf(admin);
		if (session == null)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["admin_no_access"]}");
			return;
		}

		session.Status = "";
		PanelReopen(session);
	}

	/// <summary>
	/// The player's admin panel session with the sections they may open - the previous one (same tab / page)
	/// unless their rights changed. Null without any section.
	/// </summary>
	private PanelSession? AdminSessionOf(Player admin)
	{
		var sections = AdminSections().Where(s => AdminPermissions.Has(admin.Controller, s.Flag)).ToList();
		if (sections.Count == 0)
			return null;

		var session = admin.Admin;
		if (session == null || !session.Sections.Select(s => s.Flag).SequenceEqual(sections.Select(s => s.Flag)))
			admin.Admin = session = new PanelSession("SurfTimer Admin", admin, sections);
		return session;
	}

	/// <summary>Opens the admin panel on Server - Change map (!changemap without a map name)</summary>
	private void OpenAdminChangeMap(Player admin)
	{
		var session = AdminSessionOf(admin);
		int tab = session?.Sections.FindIndex(s => s.Flag == AdminPermissions.Server) ?? -1;
		if (session == null || tab < 0)
		{
			admin.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["admin_no_access"]}");
			return;
		}

		session.ActiveTab = tab;
		session.Stacks[tab] = [session.Sections[tab].Root(), AdminChangeMapPage()];
		session.Status = "";
		PanelReopen(session);
	}

	/// <summary>All sections in tab order - the pages live in ST-Admin/Pages.</summary>
	private List<PanelSection> AdminSections() =>
	[
		new("Map", AdminPermissions.Map, AdminMapRoot),
		new("Records", AdminPermissions.Records, AdminRecordsRoot),
		new("Players", AdminPermissions.Players, AdminPlayersRoot),
		new("Server", AdminPermissions.Server, AdminServerRoot),
		new("Database", AdminPermissions.Database, AdminDatabaseRoot),
		new("Audit", AdminPermissions.Audit, AdminAuditRoot),
	];

	/// <summary>
	/// Writes an admin_actions row (in the background).
	/// </summary>
	internal void AdminAudit(PanelSession session, string action, string targetType, long? targetId, string details)
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
