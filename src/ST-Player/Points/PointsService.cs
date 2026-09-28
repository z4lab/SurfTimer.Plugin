using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurfTimer.Shared.Sql;

namespace SurfTimer;

/// <summary>
/// Keeps PlayerMapPoints (points per player/map/style) and PlayerStats.points (their sum) up to date.
/// A new PB can move everyone on that map a rank down, so the whole map is recalculated - not just
/// the player who finished (the CS:GO SurfTimer only did the latter). Unranked maps give no points.
/// </summary>
internal static class PointsService
{
	private sealed class MapInfoRow
	{
		public int Tier { get; set; }
		public bool Ranked { get; set; }
	}

	private sealed class RankRow
	{
		public int PlayerId { get; set; }
		public short Type { get; set; }
		public short Stage { get; set; }
		public long Rank { get; set; }
		public long Total { get; set; }
	}

	// One recalculation at a time - a map run and its last stage are saved at the same moment
	private static readonly SemaphoreSlim _lock = new(1, 1);

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PointsService");

	/// <summary>
	/// Recalculates everyone's points on one map for one style. Never throws - points are logged and
	/// skipped on failure so a run save isn't affected.
	/// </summary>
	internal static async Task RecalculateMapAsync(int mapId, int style)
	{
		await _lock.WaitAsync();
		try
		{
			var map = await SurfTimer.DB.QueryFirstOrDefaultAsync<MapInfoRow>(Queries.DB_QUERY_POINTS_GET_MAP, new { MapId = mapId });
			var ranks = (await SurfTimer.DB.QueryAsync<RankRow>(Queries.DB_QUERY_POINTS_GET_MAP_RANKS, new { MapId = mapId, Style = style })).ToList();
			var previousPlayers = await SurfTimer.DB.QueryAsync<int>(Queries.DB_QUERY_POINTS_GET_MAP_PLAYERS, new { MapId = mapId, Style = style });

			var points = new Dictionary<int, PointsCalculator.MapPoints>();
			if (map != null && map.Ranked)
			{
				foreach (var row in ranks)
				{
					points.TryGetValue(row.PlayerId, out var p);
					int rank = (int)row.Rank;

					switch (row.Type)
					{
						case 0: // Map
							var (wr, top10, group) = PointsCalculator.RankPoints(map.Tier, rank, (int)row.Total);
							p = p with
							{
								Map = p.Map + PointsCalculator.CompletionPoints(map.Tier),
								Wr = p.Wr + wr,
								Top10 = p.Top10 + top10,
								Group = p.Group + group,
							};
							break;
						case 1: // Bonus
							p = rank == 1
								? p with { BonusWr = p.BonusWr + PointsCalculator.BonusPoints(1) }
								: p with { Bonus = p.Bonus + PointsCalculator.BonusPoints(rank) };
							break;
						case 2: // Stage
						case 3: // Checkpoint segment
							if (rank == 1)
								p = p with { SegmentWr = p.SegmentWr + Config.PointsSegmentWr };
							break;
					}

					points[row.PlayerId] = p;
				}
			}

			var rows = points.Where(e => e.Value.Total > 0).Select(e => new
			{
				PlayerId = e.Key,
				MapId = mapId,
				Style = style,
				Points = e.Value.Total,
				MapPoints = e.Value.Map,
				WrPoints = e.Value.Wr,
				Top10Points = e.Value.Top10,
				GroupPoints = e.Value.Group,
				BonusPoints = e.Value.Bonus,
				BonusWrPoints = e.Value.BonusWr,
				SegmentWrPoints = e.Value.SegmentWr,
			}).ToList();

			// Everyone whose total can change: players with times here, and anyone who had points here before
			var affected = ranks.Select(r => r.PlayerId).Concat(previousPlayers).Distinct().ToList();

			await SurfTimer.DB.TransactionAsync(async (conn, tx) =>
			{
				await conn.ExecuteAsync(Queries.DB_QUERY_POINTS_DELETE_MAP, new { MapId = mapId, Style = style }, tx);
				if (rows.Count > 0)
					await conn.ExecuteAsync(Queries.DB_QUERY_POINTS_INSERT_MAP_PLAYER, rows, tx);
				if (affected.Count > 0)
				{
					await conn.ExecuteAsync(Queries.DB_QUERY_POINTS_RESET_TOTALS, new { Style = style, PlayerIds = affected }, tx);
					await conn.ExecuteAsync(Queries.DB_QUERY_POINTS_UPDATE_TOTALS, new { Style = style, PlayerIds = affected }, tx);
				}
			});

#if DEBUG
			Logger.LogDebug("[PointsService] Map {MapId} style {Style}: {Players} players with points, {Affected} totals updated",
				mapId, style, rows.Count, affected.Count);
#endif
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "[PointsService] Recalculating points for map {MapId} (style {Style}) failed", mapId, style);
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>
	/// Recalculates every map for every style (!recalcpoints) - returns how many maps were done.
	/// </summary>
	internal static async Task<int> RecalculateAllAsync()
	{
		var mapIds = (await SurfTimer.DB.QueryAsync<int>(Queries.DB_QUERY_POINTS_GET_ALL_MAP_IDS)).ToList();
		foreach (int mapId in mapIds)
		{
			foreach (int style in Config.Styles)
				await RecalculateMapAsync(mapId, style);
		}
		return mapIds.Count;
	}
}
