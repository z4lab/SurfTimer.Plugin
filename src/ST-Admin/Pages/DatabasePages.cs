namespace SurfTimer;

/// <summary>
/// Admin panel - Database tab: health, totals, table sizes and maintenance; Audit tab: the admin log.
/// </summary>
public partial class SurfTimer
{
	private sealed class TablesData(List<AdminRepository.TableRow> tables)
	{
		internal List<AdminRepository.TableRow> Tables { get; } = tables;
		internal long Bytes => Tables.Sum(t => t.Bytes);
	}

	private AdminPage AdminDatabaseRoot() => new("Database", ctx =>
	{
		var health = ctx.Load("health", AdminRepository.GetHealthAsync);
		var tables = ctx.Load("tables", async () => new TablesData(await AdminRepository.GetTablesAsync(exactCounts: false)));
		if (health == null || tables == null)
			return [AdminContext.LoadingRow()];

		var db = DB;
		return
		[
			AdminContext.Info("Engine", health.Version, $"ping {health.PingMs:0.0} ms"),
			AdminContext.Info("Schema", $"{health.SchemaVersion:0000} {health.SchemaName}", $"{health.Migrations} migrations applied"),
			AdminContext.Info("Database", db.DatabaseName, $"prefix {(db.TablePrefix.Length > 0 ? db.TablePrefix : "none")} · pool {db.MaxPoolSize}"),
			AdminContext.Info("Size", AdminFormat.Bytes(tables.Bytes), $"{tables.Tables.Count} tables"),
			ctx.Nav("Totals", "", "players, times, replays…", AdminTotalsPage),
			ctx.Nav("Tables", tables.Tables.Count.ToString(), "rows and size per table", AdminTablesPage),
			ctx.Nav("Maintenance", "", "points, sessions, cleanup", AdminMaintenancePage),
			ctx.Act("Refresh", "", "", () => ctx.Reload("health", "tables")),
		];
	});

	private AdminPage AdminTotalsPage() => new("Totals", ctx =>
	{
		var t = ctx.Load("totals", AdminRepository.GetTotalsAsync);
		if (t == null)
			return [AdminContext.LoadingRow()];

		return
		[
			AdminContext.Info("Players", AdminFormat.Number(t.Players), $"{playerList.Count} online · {t.ActiveBans} banned"),
			AdminContext.Info("Maps", AdminFormat.Number(t.Maps), $"{t.RankedMaps} ranked"),
			AdminContext.Info("Courses", AdminFormat.Number(t.Courses), "maps, stages, bonuses, checkpoints"),
			AdminContext.Info("Times", AdminFormat.Number(t.Times), t.HiddenTimes > 0 ? $"{t.HiddenTimes} hidden" : ""),
			AdminContext.Info("Replays", AdminFormat.Number(t.Replays), AdminFormat.Bytes(t.ReplayBytes)),
			AdminContext.Info("Run history", AdminFormat.Number(t.History), "finished runs"),
			AdminContext.Info("Open sessions", AdminFormat.Number(t.OpenSessions)),
			AdminContext.Info("Total playtime", AdminFormat.Duration(t.PlayTime)),
		];
	});

	private AdminPage AdminTablesPage() => new("Tables", ctx =>
	{
		bool exact = ctx.Page.State.ContainsKey("exact");
		var tables = ctx.Load("list", async () => new TablesData(await AdminRepository.GetTablesAsync(exact)));
		if (tables == null)
			return [AdminContext.LoadingRow()];

		var rows = tables.Tables.Select(t =>
			AdminContext.Info(t.Name, AdminFormat.Bytes(t.Bytes), t.Exact ? $"{AdminFormat.Number(t.Rows)} rows" : $"~{AdminFormat.Number(t.Rows)} rows")).ToList();
		if (!exact)
		{
			rows.Add(ctx.Act("Exact row counts", "", "InnoDB's counts are estimates", () =>
			{
				ctx.Page.State["exact"] = true;
				ctx.Reload("list");
			}));
		}
		return rows;
	});

	private sealed class Count(long value)
	{
		internal long Value { get; } = value;
	}

	private AdminPage AdminMaintenancePage() => new("Maintenance", ctx =>
	[
		ctx.Act("Recalculate all points", "", "every map and style", () =>
		{
			ctx.Audit("recalc points", "server", null, "all maps");
			ctx.Run("Recalculating all points…", async () =>
			{
				int maps = await PointsService.RecalculateAllAsync();
				return $"Points of {maps} maps recalculated";
			});
		}),
		ctx.Act("Close stale sessions", "", "left open by a crash", () =>
			ctx.Run("Closing stale sessions…", async () => $"Closed {await PlayerRepository.CloseStaleSessionsAsync()} session(s)")),
		ctx.Danger("Delete orphaned replays", "not used by any time", () => AdminContext.Confirm("Orphaned replays", c =>
		{
			var count = c.Load("count", async () => new Count(await AdminRepository.CountOrphanedReplaysAsync()));
			return count == null ? [AdminContext.LoadingRow()] : [AdminContext.Info("Replays to delete", AdminFormat.Number(count.Value))];
		}, "Delete", c =>
		{
			c.Audit("delete orphaned replays", "database", null, "");
			c.Run("Deleting orphaned replays…", async () => $"Deleted {await AdminRepository.DeleteOrphanedReplaysAsync()} replay(s)");
		})),
		ctx.Nav("Purge run history", "", "finished runs older than…", AdminPurgeHistoryPage),
		ctx.Danger("Optimize tables", "rebuilds tables, locks them briefly", () => AdminContext.Confirm("Optimize tables",
			_ => [AdminContext.Info("OPTIMIZE TABLE", "", "every SurfTimer table - saves wait meanwhile")], "Optimize", c =>
			{
				c.Audit("optimize tables", "database", null, "");
				c.Run("Optimizing tables…", async () => $"Optimized {await AdminRepository.OptimizeTablesAsync()} tables");
			})),
	]);

	private AdminPage AdminPurgeHistoryPage() => new("Purge run history", ctx =>
		new[] { 30, 90, 365 }.Select(days => ctx.Danger($"Older than {days} days", "PBs are kept", () => AdminContext.Confirm($"Purge > {days} days", c =>
		{
			var count = c.Load("count", async () => new Count(await AdminRepository.CountHistoryOlderThanAsync(days)));
			return count == null
				? [AdminContext.LoadingRow()]
				: [AdminContext.Info("Runs to delete", AdminFormat.Number(count.Value), "profiles' \"runs finished\" drops")];
		}, "Purge", c =>
		{
			c.Audit("purge history", "database", days, $"older than {days} days");
			c.Run("Purging run history…", async () => $"Deleted {AdminFormat.Number(await AdminRepository.PurgeHistoryOlderThanAsync(days))} run(s)");
		}))).ToList());

	// ---- Audit ----

	private sealed class AuditData(List<AdminRepository.AuditRow> rows)
	{
		internal List<AdminRepository.AuditRow> Rows { get; } = rows;
	}

	private AdminPage AdminAuditRoot() => new("Audit", ctx =>
	{
		bool thisMap = ctx.Page.State.ContainsKey("thisMap");
		int? mapId = thisMap && CurrentMap?.ID > 0 ? CurrentMap.ID : null;
		var audit = ctx.Load("audit", async () => new AuditData(await AdminRepository.GetAuditAsync(mapId, 100)));

		var rows = new List<HudMenuItem>
		{
			ctx.Toggle("This map only", thisMap, CurrentMap?.Name ?? "", on =>
			{
				if (on)
					ctx.Page.State["thisMap"] = true;
				else
					ctx.Page.State.Remove("thisMap");
				ctx.Reload("audit");
			}),
		};

		if (audit == null)
		{
			rows.Add(AdminContext.LoadingRow());
			return rows;
		}
		if (audit.Rows.Count == 0)
			rows.Add(AdminContext.Info("No admin actions yet"));

		rows.AddRange(audit.Rows.Select(a =>
		{
			string details = !thisMap && a.MapName != null && !a.Details.Contains(a.MapName) ? $"{a.MapName} · {a.Details}" : a.Details;
			return AdminContext.Info($"{AdminFormat.Ago(a.CreatedAt)} · {a.AdminName ?? "console"}", a.Action, details);
		}));
		return rows;
	});
}
