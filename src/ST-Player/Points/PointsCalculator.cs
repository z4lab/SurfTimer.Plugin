namespace SurfTimer;

/// <summary>
/// Points per run, as in the CS:GO SurfTimer plugin (surftimer/SurfTimer, dev,
/// addons/sourcemod/scripting/surftimer/sql.sp - sql_CountFinishedMapsCallback / Bonus callback).
/// Ranks are "count of strictly faster times + 1", N is the number of completions of that map/bonus.
/// Pure functions - PointsService applies them to the database.
/// </summary>
internal static class PointsCalculator
{
	/// <summary>
	/// Points one player gets on one map, split into the buckets shown in !profile.
	/// </summary>
	internal record struct MapPoints(int Map, int Wr, int Top10, int Group, int Bonus, int BonusWr, int SegmentWr)
	{
		internal readonly int Total => Map + Wr + Top10 + Group + Bonus + BonusWr + SegmentWr;
	}

	/// <summary>
	/// Map completion points by tier (flat, independent of rank).
	/// </summary>
	internal static int CompletionPoints(int tier) => tier switch
	{
		1 => 25,
		2 => 50,
		3 => 100,
		4 => 200,
		5 => 400,
		6 => 600,
		7 => 800,
		8 => 1000,
		_ => 13,
	};

	/// <summary>
	/// Points for the map WR - they grow with the number of completions and set the scale for the
	/// top 10 and group points.
	/// </summary>
	internal static int WrPoints(int tier, int completions)
	{
		double n = completions;
		double points = tier switch
		{
			1 => Math.Max(n * 1.75 / 6.0 + 58.5, 250),
			2 => Math.Max(n * 2.8 / 5.0 + 82.15, 500),
			3 => ScaledOrMinimum(n * 3.5 / 4.0, 750, 117),
			4 => ScaledOrMinimum(n * 5.74 / 4.0, 1000, 164.25),
			5 => ScaledOrMinimum(n * 7.0 / 4.0, 1250, 234),
			6 => ScaledOrMinimum(n * 14.0 / 4.0, 1500, 328),
			7 => ScaledOrMinimum(n * 21.0 / 4.0, 1750, 420),
			8 => ScaledOrMinimum(n * 30.0 / 4.0, 2000, 560),
			_ => 25,
		};
		return (int)Math.Ceiling(points);
	}

	// Tiers 3-8: below the minimum it's the minimum, above it the constant is added
	private static double ScaledOrMinimum(double scaled, double minimum, double bonus) =>
		scaled < minimum ? minimum : scaled + bonus;

	// Share of the WR points for ranks 2-10
	private static readonly double[] Top10Factors = [0.80, 0.75, 0.70, 0.65, 0.60, 0.55, 0.50, 0.45, 0.40];

	// Groups G1-G5 by share of completions, and how far each is above the previous one (1.5x)
	private static readonly double[] GroupShares = [0.03125, 0.0625, 0.125, 0.25, 0.5];

	/// <summary>
	/// WR, top 10 or group points for a map rank (completion points not included).
	/// Returns the amount and which bucket it belongs to.
	/// </summary>
	internal static (int Wr, int Top10, int Group) RankPoints(int tier, int rank, int completions)
	{
		int wrPoints = WrPoints(tier, completions);

		if (rank == 1)
			return (wrPoints, 0, 0);
		if (rank <= 10)
			return (0, (int)Math.Ceiling(Top10Factors[rank - 2] * wrPoints), 0);

		// Groups start after the top 10; every group spans at least 5 ranks
		int bottom = 11;
		double groupPoints = wrPoints * 0.25;
		foreach (double share in GroupShares)
		{
			int top = (int)Math.Ceiling(completions * share + 11.0);
			if (top - bottom < 4)
				top = bottom + 4;

			if (rank <= top)
				return (0, 0, (int)Math.Round(groupPoints, MidpointRounding.AwayFromZero));

			bottom = top + 1;
			groupPoints /= 1.5;
		}

		return (0, 0, 0);
	}

	/// <summary>
	/// Bonus points by rank - the same for every bonus, tier and completion count.
	/// </summary>
	internal static int BonusPoints(int rank) => rank switch
	{
		1 => 250,
		2 => 235,
		3 => 220,
		4 => 205,
		5 => 190,
		6 => 175,
		7 => 160,
		8 => 145,
		9 => 130,
		10 => 100,
		11 => 95,
		12 => 90,
		13 => 80,
		14 => 70,
		15 => 60,
		16 => 50,
		17 => 40,
		18 => 30,
		19 => 20,
		20 => 10,
		_ => 5,
	};
}
