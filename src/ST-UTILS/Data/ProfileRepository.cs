using SurfTimer.Shared.Entities;
using SurfTimer.Shared.Sql;

namespace SurfTimer.Data;

/// <summary>
/// Everything !profile shows about a player, for one style - loaded in one go off the main thread.
/// </summary>
internal sealed class ProfileData
{
	internal sealed class PointsRow
	{
		public long Points { get; set; }
		public long PlayTime { get; set; }
		public long ServerRank { get; set; }
		public long RankedPlayers { get; set; }
	}

	internal sealed class TypeCountsRow
	{
		public short Type { get; set; }
		public long Completions { get; set; }
		public long Records { get; set; }
		public long Top10 { get; set; }
	}

	internal sealed class RankedTotalsRow
	{
		public long Maps { get; set; }
		public long Stages { get; set; }
		public long Bonuses { get; set; }
	}

	internal sealed class ExtrasRow
	{
		public decimal? AverageSync { get; set; }
		public long RunsStarted { get; set; }
		public long RunsFinished { get; set; }
	}

	internal sealed class PointsBucketsRow
	{
		public long MapPoints { get; set; }
		public long WrPoints { get; set; }
		public long Top10Points { get; set; }
		public long GroupPoints { get; set; }
		public long BonusPoints { get; set; }
		public long BonusWrPoints { get; set; }
		public long SegmentWrPoints { get; set; }
	}

	internal sealed class RunRow
	{
		public int Id { get; set; }
		public int MapId { get; set; }
		public string MapName { get; set; } = "";
		public short Type { get; set; }
		public short Stage { get; set; }
		public int RunTime { get; set; }
		public decimal? Sync { get; set; }
		public int RunDate { get; set; }
		public long Rank { get; set; }
		public long Total { get; set; }
	}

	internal sealed class TierRow
	{
		public int Tier { get; set; }
		public long Total { get; set; }
		public long Completed { get; set; }
	}

	public PointsRow Points { get; set; } = new();
	public List<TypeCountsRow> TypeCounts { get; set; } = [];
	public RankedTotalsRow RankedTotals { get; set; } = new();
	public ExtrasRow Extras { get; set; } = new();
	public PointsBucketsRow PointsBuckets { get; set; } = new();
	public List<RunRow> Records { get; set; } = [];
	public List<RunRow> Recent { get; set; } = [];
	public List<TierRow> Tiers { get; set; } = [];
	/// <summary>The player's times on the current map, with rank</summary>
	public List<RunRow> CurrentMapRuns { get; set; } = [];

	internal TypeCountsRow CountsFor(short type) =>
		TypeCounts.FirstOrDefault(c => c.Type == type) ?? new TypeCountsRow { Type = type };
}

internal static class ProfileRepository
{
	/// <summary>
	/// Best match for an offline player by (partial) name - the most recently seen one.
	/// </summary>
	internal static Task<PlayerProfileEntity?> FindPlayerByNameAsync(string name)
	{
		// LIKE wildcards in the name are matched literally
		string escaped = name.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
		return SurfTimer.DB.QueryFirstOrDefaultAsync<PlayerProfileEntity>(Queries.DB_QUERY_PP_FIND_BY_NAME,
			new { Pattern = $"%{escaped}%" });
	}

	internal static async Task<ProfileData> LoadAsync(int playerId, int style, int currentMapId)
	{
		var args = new { PlayerId = playerId, Style = style };
		var db = SurfTimer.DB;

		var data = new ProfileData
		{
			Points = await db.QueryFirstOrDefaultAsync<ProfileData.PointsRow>(Queries.DB_QUERY_PROFILE_POINTS, args) ?? new(),
			TypeCounts = (await db.QueryAsync<ProfileData.TypeCountsRow>(Queries.DB_QUERY_PROFILE_TYPE_COUNTS, args)).ToList(),
			RankedTotals = await db.QueryFirstOrDefaultAsync<ProfileData.RankedTotalsRow>(Queries.DB_QUERY_PROFILE_RANKED_TOTALS) ?? new(),
			Extras = await db.QueryFirstOrDefaultAsync<ProfileData.ExtrasRow>(Queries.DB_QUERY_PROFILE_EXTRAS, args) ?? new(),
			PointsBuckets = await db.QueryFirstOrDefaultAsync<ProfileData.PointsBucketsRow>(Queries.DB_QUERY_PROFILE_POINTS_BUCKETS, args) ?? new(),
			Records = (await db.QueryAsync<ProfileData.RunRow>(Queries.DB_QUERY_PROFILE_RECORDS, args)).ToList(),
			Recent = (await db.QueryAsync<ProfileData.RunRow>(Queries.DB_QUERY_PROFILE_RECENT, args)).ToList(),
			Tiers = (await db.QueryAsync<ProfileData.TierRow>(Queries.DB_QUERY_PROFILE_TIERS, args)).ToList(),
		};

		if (currentMapId > 0)
		{
			data.CurrentMapRuns = (await db.QueryAsync<ProfileData.RunRow>(Queries.DB_QUERY_PROFILE_MAP_RUNS,
				new { PlayerId = playerId, Style = style, MapId = currentMapId })).ToList();
		}

		return data;
	}
}
