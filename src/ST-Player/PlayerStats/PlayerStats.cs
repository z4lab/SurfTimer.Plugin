using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace SurfTimer;

public class PlayerStats
{
	/// <summary>
	/// Map Personal Best - Refer to as PB[style]
	/// </summary>
	public Dictionary<int, PersonalBest> PB { get; set; } = new Dictionary<int, PersonalBest>();
	/// <summary>
	/// Bonus Personal Best - Refer to as BonusPB[bonus#][style]
	/// </summary>
	public Dictionary<int, PersonalBest>[] BonusPB { get; set; }
	/// <summary>
	/// Stage Personal Best - Refer to as StagePB[stage#][style]
	/// </summary>
	public Dictionary<int, PersonalBest>[] StagePB { get; set; }
	/// <summary>
	/// Checkpoint segment Personal Best (non-staged maps only) - Refer to as CheckpointPB[checkpoint#][style]
	/// </summary>
	public Dictionary<int, PersonalBest>[] CheckpointPB { get; set; }
	/// <summary>
	/// This object tracks data for the Player's current run.
	/// </summary>
	public CurrentRun ThisRun { get; set; } = new CurrentRun();

	private readonly ILogger<PlayerStats> _logger;


	internal PlayerStats([CallerMemberName] string methodName = "")
	{
		// Resolve the logger instance from the DI container
		_logger = SurfTimer.ServiceProvider.GetRequiredService<ILogger<PlayerStats>>();

		// Initialize PB variables
		this.StagePB = new Dictionary<int, PersonalBest>[SurfTimer.CurrentMap.Stages + 1];
		this.BonusPB = new Dictionary<int, PersonalBest>[SurfTimer.CurrentMap.Bonuses + 1];
		bool checkpointed = SurfTimer.CurrentMap.Stages == 0 && SurfTimer.CurrentMap.TotalCheckpoints > 0;
		this.CheckpointPB = checkpointed
			? new Dictionary<int, PersonalBest>[SurfTimer.CurrentMap.CheckpointSegments + 1]
			: Array.Empty<Dictionary<int, PersonalBest>>();
		int initStage = 0;
		int initBonus = 0;
		int initCheckpoint = 0;

		foreach (int style in Config.Styles)
		{
			PB[style] = new PersonalBest { Type = 0 };

			for (int i = 1; i <= SurfTimer.CurrentMap.Stages; i++)
			{
				this.StagePB[i] = new Dictionary<int, PersonalBest>();
				this.StagePB[i][style] = new PersonalBest { Type = 2 };
				initStage++;
			}

			for (int i = 1; i <= SurfTimer.CurrentMap.Bonuses; i++)
			{
				this.BonusPB[i] = new Dictionary<int, PersonalBest>();
				this.BonusPB[i][style] = new PersonalBest { Type = 1 };
				initBonus++;
			}

			for (int i = 1; checkpointed && i <= SurfTimer.CurrentMap.CheckpointSegments; i++)
			{
				this.CheckpointPB[i] ??= new Dictionary<int, PersonalBest>();
				this.CheckpointPB[i][style] = new PersonalBest { Type = 3 };
				initCheckpoint++;
			}
		}


		_logger.LogTrace("[{ClassName}] {MethodName} -> PlayerStats -> Initialized {StagesInitialized} Stages, {BonusesInitialized} Bonuses and {CheckpointsInitialized} Checkpoint segments",
			nameof(PlayerStats), methodName, initStage, initBonus, initCheckpoint
		);
	}

	/// <summary>
	/// The PB object for a run type (0 map, 1 bonus, 2 stage, 3 checkpoint segment), number and style -
	/// null for a course / style this map doesn't have.
	/// </summary>
	internal PersonalBest? PbFor(short type, short number, int style)
	{
		Dictionary<int, PersonalBest>? byStyle = type switch
		{
			0 => PB,
			1 => number > 0 && number < BonusPB.Length ? BonusPB[number] : null,
			2 => number > 0 && number < StagePB.Length ? StagePB[number] : null,
			3 => number > 0 && number < CheckpointPB.Length ? CheckpointPB[number] : null,
			_ => null,
		};
		return byStyle != null && byStyle.TryGetValue(style, out var pb) ? pb : null;
	}

	/// <summary>
	/// Loads the player's PBs on the current map with their ranks (and the map PB's splits).
	/// </summary>
	internal async Task LoadPlayerMapTimesData(Player player, [CallerMemberName] string methodName = "")
	{
		var times = await TimeRepository.GetPlayerMapTimesAsync(player.Profile.ID, SurfTimer.CurrentMap.ID);

		foreach (var time in times)
		{
			var pb = PbFor(CourseKinds.ToRunType(time.Kind), time.Kind == CourseKind.Map ? (short)0 : time.Number, time.Style);
			if (pb == null)
				continue; // A course the map's zones don't have anymore

			time.Fill(pb);
			if (time.Kind == CourseKind.Map)
				await pb.LoadCheckpoints();

#if DEBUG
			_logger.LogDebug("[{ClassName}] {MethodName} -> Loaded {Kind} {Number} PB {RunID} (rank {Rank}) for '{PlayerName}'",
				nameof(PlayerStats), methodName, time.Kind, time.Number, time.Id, time.Rank, player.Profile.Name);
#endif
		}
	}
}
