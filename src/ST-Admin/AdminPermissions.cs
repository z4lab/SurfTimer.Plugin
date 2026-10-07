using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;

namespace SurfTimer;

/// <summary>
/// Access rights of the !surfadmin panel: one CounterStrikeSharp permission per section (granted through
/// admins.json / admin groups). @css/root has every section.
/// </summary>
internal static class AdminPermissions
{
	internal const string Root = "@css/root";
	internal const string Map = "@surftimer/map";
	internal const string Records = "@surftimer/records";
	internal const string Players = "@surftimer/players";
	internal const string Server = "@surftimer/server";
	internal const string Database = "@surftimer/database";
	internal const string Audit = "@surftimer/audit";

	/// <summary>Changing the map (!changemap, Server - Change map) - the generic CounterStrikeSharp flag</summary>
	internal const string ChangeMap = "@css/changemap";

	internal static readonly IReadOnlyList<string> Sections = [Map, Records, Players, Server, Database, Audit];

	/// <param name="flag">null = no permission needed</param>
	internal static bool Has(CCSPlayerController? controller, string? flag)
	{
		if (controller == null || !controller.IsValid)
			return false;
		if (flag == null)
			return true;
		return AdminManager.PlayerHasPermissions(controller, Root) || AdminManager.PlayerHasPermissions(controller, flag);
	}

	/// <summary>
	/// !surfadmin opens for anyone with at least one section.
	/// </summary>
	internal static bool CanOpen(CCSPlayerController? controller) => Sections.Any(flag => Has(controller, flag));
}
