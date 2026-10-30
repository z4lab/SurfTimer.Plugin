using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// Every visible PB time of the map's courses (map, stages, bonuses, checkpoint segments) and the splits of every map
/// PB, in memory - so finish and split messages show "[rank/finishers]" right away, before the run is saved. Loaded
/// with the map's records (map start and after every saved time) and updated the moment a PB is announced.
/// Ranks are like the leaderboard's: 1 + the other players' distinct faster times (ties share a rank: 1, 1, 2).
/// Main thread only.
/// </summary>
internal sealed class CourseRanks
{
	/// <summary>One leaderboard: each player's time and all times sorted</summary>
	private sealed class Board
	{
		private readonly Dictionary<int, int> _byPlayer = new();
		private readonly List<int> _sorted = new();

		internal void Set(int playerId, int ticks)
		{
			if (_byPlayer.TryGetValue(playerId, out int old))
				_sorted.RemoveAt(IndexOf(old));
			_byPlayer[playerId] = ticks;
			_sorted.Insert(LowerBound(ticks), ticks);
		}

		/// <summary>The rank a time of this player has (their own time left out) and the finishers with it counted</summary>
		internal (int Rank, int Total) RankOf(int playerId, int ticks)
		{
			bool own = _byPlayer.TryGetValue(playerId, out int ownTicks);
			bool skipOwn = own;
			int rank = 1, last = -1;
			for (int i = 0, faster = LowerBound(ticks); i < faster; i++)
			{
				int t = _sorted[i];
				if (skipOwn && t == ownTicks)
					skipOwn = false;
				else if (t != last)
				{
					rank++;
					last = t;
				}
			}
			return (rank, _sorted.Count + (own ? 0 : 1));
		}

		/// <summary>How many times are faster than ticks (ties counted each)</summary>
		private int LowerBound(int ticks)
		{
			int low = 0, high = _sorted.Count;
			while (low < high)
			{
				int mid = (low + high) / 2;
				if (_sorted[mid] < ticks)
					low = mid + 1;
				else
					high = mid;
			}
			return low;
		}

		private int IndexOf(int ticks) => LowerBound(ticks);
	}

	private readonly Dictionary<(short Type, short Number, int Style), Board> _courses = new();
	private readonly Dictionary<(int Style, int Split), Board> _splits = new();

	private Board CourseBoard(short type, short number, int style)
	{
		var key = (type, number, style);
		if (!_courses.TryGetValue(key, out var board))
			_courses[key] = board = new Board();
		return board;
	}

	private Board SplitBoard(int style, int split)
	{
		var key = (style, split);
		if (!_splits.TryGetValue(key, out var board))
			_splits[key] = board = new Board();
		return board;
	}

	/// <summary>Rank / finishers of a time on a course (type: 0 map, 1 bonus, 2 stage, 3 checkpoint segment)</summary>
	internal (int Rank, int Total) RankOf(short type, short number, int style, int playerId, int ticks) =>
		CourseBoard(type, type == 0 ? (short)0 : number, style).RankOf(playerId, ticks);

	/// <summary>Rank / map PBs with that split of a map run's time at a checkpoint (split number)</summary>
	internal (int Rank, int Total) SplitRankOf(int style, int split, int playerId, int ticks) =>
		SplitBoard(style, split).RankOf(playerId, ticks);

	/// <summary>A new PB - counted from now on (the saved one comes with the next load)</summary>
	internal void SetTime(short type, short number, int style, int playerId, int ticks) =>
		CourseBoard(type, type == 0 ? (short)0 : number, style).Set(playerId, ticks);

	/// <summary>A new map PB's splits (split number - time at it)</summary>
	internal void SetSplits(int style, int playerId, IEnumerable<(int Split, int Ticks)> splits)
	{
		foreach (var (split, ticks) in splits)
			SplitBoard(style, split).Set(playerId, ticks);
	}

	internal static async Task<CourseRanks> LoadAsync(int mapId)
	{
		var ranks = new CourseRanks();
		foreach (var row in await TimeRepository.GetMapBoardTimesAsync(mapId))
		{
			short type = CourseKinds.ToRunType(row.Kind);
			ranks.CourseBoard(type, type == 0 ? (short)0 : row.Number, row.StyleId).Set(row.PlayerId, row.RunTime);
		}
		foreach (var row in await TimeRepository.GetMapPbSplitsAsync(mapId))
			ranks.SplitBoard(row.StyleId, row.SplitNumber).Set(row.PlayerId, row.EnterTicks);
		return ranks;
	}

	// ---- Chat ----

	/// <summary>" [3/120]" in grey - "" when the map's ranks aren't there</summary>
	internal static string Tag((int Rank, int Total)? rank) =>
		rank is (int n, int x) && x > 0 ? $" {ChatColors.Grey}[{n}/{x}]{ChatColors.Default}" : "";

	/// <summary>A run's time with its rank for chat: "1:23.456 [3/120]"</summary>
	internal static string Time(Player player, short type, short number, int ticks) =>
		PlayerHud.FormatTime(ticks) + Tag(SurfTimer.CurrentMap?.Ranks?.RankOf(type, number, player.Timer.Style, player.Profile.ID, ticks));

	/// <summary>A map run's time at a checkpoint with its rank among the map PBs' splits</summary>
	internal static string SplitTime(Player player, int split, int ticks) =>
		PlayerHud.FormatTime(ticks) + Tag(SurfTimer.CurrentMap?.Ranks?.SplitRankOf(player.Timer.Style, split, player.Profile.ID, ticks));

	/// <summary>A PB was announced - counted for the next messages right away (not for timer-banned players: not saved)</summary>
	internal static void Record(Player player, short type, short number, int ticks)
	{
		if (!player.Profile.IsBanned)
			SurfTimer.CurrentMap?.Ranks?.SetTime(type, number, player.Timer.Style, player.Profile.ID, ticks);
	}

	/// <summary>A map PB was announced - its splits count right away too</summary>
	internal static void RecordSplits(Player player)
	{
		if (!player.Profile.IsBanned)
			SurfTimer.CurrentMap?.Ranks?.SetSplits(player.Timer.Style, player.Profile.ID,
				player.Stats.ThisRun.Checkpoints.Select(c => (c.Key, c.Value.RunTime)));
	}
}
