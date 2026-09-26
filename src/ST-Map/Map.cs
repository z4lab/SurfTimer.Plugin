using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurfTimer.Data;
using SurfTimer.Shared.DTO;
using SurfTimer.Shared.Entities;
using SurfTimer.Shared.Types;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SurfTimer;

public class Map : MapEntity
{
	public int TotalCheckpoints { get; set; } = 0;
	/// <summary>
	/// Map Completion Count - Refer to as MapCompletions[style]
	/// </summary>
	public Dictionary<int, int> MapCompletions { get; set; } = new Dictionary<int, int>();
	/// <summary>
	/// Bonus Completion Count - Refer to as BonusCompletions[bonus#][style]
	/// </summary>
	public Dictionary<int, int>[] BonusCompletions { get; set; } = Array.Empty<Dictionary<int, int>>();
	/// <summary>
	/// Stage Completion Count - Refer to as StageCompletions[stage#][style]
	/// </summary>
	public Dictionary<int, int>[] StageCompletions { get; set; } = Array.Empty<Dictionary<int, int>>();
	/// <summary>
	/// Map World Record - Refer to as WR[style]
	/// </summary>
	public Dictionary<int, PersonalBest> WR { get; set; } = new Dictionary<int, PersonalBest>();
	/// <summary>
	/// Bonus World Record - Refer to as BonusWR[bonus#][style]
	/// </summary>
	public Dictionary<int, PersonalBest>[] BonusWR { get; set; } = Array.Empty<Dictionary<int, PersonalBest>>();
	/// <summary>
	/// Stage World Record - Refer to as StageWR[stage#][style]
	/// </summary>
	public Dictionary<int, PersonalBest>[] StageWR { get; set; } = Array.Empty<Dictionary<int, PersonalBest>>();
	/// <summary>
	/// Checkpoint segment World Record (non-staged maps only) - Refer to as CheckpointWR[checkpoint#][style]
	/// </summary>
	public Dictionary<int, PersonalBest>[] CheckpointWR { get; set; } = Array.Empty<Dictionary<int, PersonalBest>>();
	/// <summary>
	/// Checkpoint segment Completion Count (non-staged maps only) - Refer to as CheckpointCompletions[checkpoint#][style]
	/// </summary>
	public Dictionary<int, int>[] CheckpointCompletions { get; set; } = Array.Empty<Dictionary<int, int>>();

	/// <summary>
	/// Not sure what this is for.
	/// Guessing it's to do with Replays and the ability to play your PB replay.
	/// 
	/// - T
	/// </summary>
	public List<int> ConnectedMapTimes { get; set; } = new List<int>();

	// Zone registry for teleport targets and counts - a role+number (e.g. BonusEnd 1) can have several
	// triggers. Not used to dispatch touches: round restarts re-create trigger entities with new indexes.
	internal Dictionary<(ZoneType Type, short Number), List<ZoneInfo>> Zones { get; } = new();

	public ReplayManager ReplayManager { get; set; } = null!;

	private readonly ILogger<Map> _logger;
	private readonly IDataAccessService _dataService;

	// Constructor
	internal Map(string name)
	{
		// Resolve the logger instance from the DI container
		_logger = SurfTimer.ServiceProvider.GetRequiredService<ILogger<Map>>();
		_dataService = SurfTimer.ServiceProvider.GetRequiredService<IDataAccessService>();

		// Set map name
		this.Name = name;

		// Load zones
		MapLoadZones();
		_logger.LogInformation("[{ClassName}] -> Zones have been loaded. | Bonuses: {Bonuses} | Stages: {Stages} | Checkpoints: {Checkpoints}",
			nameof(Map), this.Bonuses, this.Stages, this.TotalCheckpoints
		);
	}

	internal async Task InitializeAsync([CallerMemberName] string methodName = "")
	{
		bool checkpointed = this.Stages == 0 && this.TotalCheckpoints > 0;

		// Initialize ReplayManager with placeholder values
		this.ReplayManager = new ReplayManager(-1, this.Stages > 0, this.Bonuses > 0, checkpointed, null!);

		// Initialize WR variables
		this.StageWR = new Dictionary<int, PersonalBest>[this.Stages + 1]; // We do + 1 cause stages and bonuses start from 1, not from 0
		this.StageCompletions = new Dictionary<int, int>[this.Stages + 1];
		this.BonusWR = new Dictionary<int, PersonalBest>[this.Bonuses + 1];
		this.BonusCompletions = new Dictionary<int, int>[this.Bonuses + 1];
		this.CheckpointWR = checkpointed
			? new Dictionary<int, PersonalBest>[this.TotalCheckpoints + 1]
			: Array.Empty<Dictionary<int, PersonalBest>>();
		this.CheckpointCompletions = checkpointed
			? new Dictionary<int, int>[this.TotalCheckpoints + 1]
			: Array.Empty<Dictionary<int, int>>();
		int initStages = 0;
		int initBonuses = 0;
		int initCheckpoints = 0;

		foreach (int style in Config.Styles)
		{
			this.WR[style] = new PersonalBest { Type = 0 };
			this.MapCompletions[style] = 0;

			for (int i = 1; i <= this.Stages; i++)
			{
				this.StageWR[i] = new Dictionary<int, PersonalBest>();
				this.StageWR[i][style] = new PersonalBest { Type = 2 };
				this.StageCompletions[i] = new Dictionary<int, int>();
				this.StageCompletions[i][style] = 0;
				initStages++;
			}

			for (int i = 1; i <= this.Bonuses; i++)
			{
				this.BonusWR[i] = new Dictionary<int, PersonalBest>();
				this.BonusWR[i][style] = new PersonalBest { Type = 1 };
				this.BonusCompletions[i] = new Dictionary<int, int>();
				this.BonusCompletions[i][style] = 0;
				initBonuses++;
			}

			for (int i = 1; checkpointed && i <= this.TotalCheckpoints; i++)
			{
				this.CheckpointWR[i] ??= new Dictionary<int, PersonalBest>();
				this.CheckpointWR[i][style] = new PersonalBest { Type = 3 };
				this.CheckpointCompletions[i] ??= new Dictionary<int, int>();
				this.CheckpointCompletions[i][style] = 0;
				initCheckpoints++;
			}
		}

		_logger.LogInformation("[{ClassName}] {MethodName} -> Initialized WR variables. | Bonuses: {Bonuses} | Stages: {Stages} | Checkpoints: {Checkpoints}",
			nameof(Map), methodName, initBonuses, initStages, initCheckpoints
		);

		await LoadMapInfo();
	}

	/// <summary>
	/// Registers every zone trigger in the map. A role+number can have several triggers (e.g. two
	/// bonus1_end, one per side of a course) - each keeps its own teleport target. Counts are the
	/// highest number found, not the number of triggers, so duplicates don't inflate them.
	/// </summary>
	internal void MapLoadZones([CallerMemberName] string methodName = "")
	{
		var triggers = Utilities.FindAllEntitiesByDesignerName<CBaseTrigger>("trigger_multiple");
		var destinations = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_teleport_destination").ToList();

		foreach (CBaseTrigger trigger in triggers)
		{
			string? name = trigger.Entity?.Name;
			if (!ZoneName.TryParse(name, out ZoneType type, out short number))
				continue;

			var (teleport, angles) = FindTeleportTarget(trigger, type, number, destinations);
			var zone = new ZoneInfo(trigger.Index, name!, type, number, teleport, angles);

			if (!this.Zones.TryGetValue((type, number), out var list))
				this.Zones[(type, number)] = list = new List<ZoneInfo>();
			list.Add(zone);
		}

		short HighestNumber(ZoneType type) =>
			this.Zones.Keys.Where(k => k.Type == type).Select(k => k.Number).DefaultIfEmpty((short)0).Max();

		this.Stages = HighestNumber(ZoneType.StageStart); // Map start is stage 1, so the highest sN_start is the stage count
		this.Bonuses = HighestNumber(ZoneType.BonusStart);
		this.TotalCheckpoints = this.Stages > 0
			? this.Stages - 1 // Stages are counted as Checkpoints on Staged maps during MAP runs
			: HighestNumber(ZoneType.Checkpoint);

		_logger.LogInformation("[{ClassName}] {MethodName} -> Registered {Triggers} zone triggers in {Roles} zones",
			nameof(Map), methodName, this.Zones.Values.Sum(z => z.Count), this.Zones.Count
		);

		KillServerCommandEnts();
	}

	/// <summary>
	/// Where to put a player teleporting into this trigger: an info_teleport_destination inside it,
	/// else the matching named spawn (spawn_map_start, spawn_s2_start, ...) closest to it, else its origin.
	/// </summary>
	private static (VectorT Position, QAngleT? Angles) FindTeleportTarget(CBaseTrigger trigger, ZoneType type, short number, List<CBaseEntity> destinations)
	{
		VectorT origin = trigger.AbsOrigin!.ToVector_t();

		var inside = destinations.FirstOrDefault(d => d.AbsOrigin != null && IsInsideTrigger(trigger, d.AbsOrigin.ToVector_t()));
		var chosen = inside ?? destinations
			.Where(d => d.AbsOrigin != null
				&& ZoneName.TryParseSpawn(d.Entity?.Name, out var spawnType, out var spawnNumber)
				&& spawnType == type && spawnNumber == number)
			.OrderBy(d => (d.AbsOrigin!.ToVector_t() - origin).Length())
			.FirstOrDefault();

		if (chosen == null)
			return (origin, null);

		return (chosen.AbsOrigin!.ToVector_t(),
			new QAngleT(chosen.AbsRotation!.X, chosen.AbsRotation!.Y, chosen.AbsRotation!.Z));
	}

	internal static bool IsInsideTrigger(CBaseTrigger trigger, VectorT point)
	{
		var origin = trigger.AbsOrigin!;
		var mins = trigger.Collision.Mins;
		var maxs = trigger.Collision.Maxs;
		return point.X >= origin.X + mins.X && point.X <= origin.X + maxs.X
			&& point.Y >= origin.Y + mins.Y && point.Y <= origin.Y + maxs.Y
			&& point.Z >= origin.Z + mins.Z && point.Z <= origin.Z + maxs.Z;
	}

	internal bool HasZone(ZoneType type, short number) => this.Zones.ContainsKey((type, number));

	/// <summary>
	/// The trigger of a role+number closest to `from` (by teleport target). Without a position
	/// (dead/spectating) the lowest trigger index is used so the choice is still deterministic.
	/// </summary>
	internal ZoneInfo? FindNearestZone(ZoneType type, short number, VectorT? from)
	{
		if (!this.Zones.TryGetValue((type, number), out var list) || list.Count == 0)
			return null;

		if (from == null)
			return list.MinBy(z => z.TriggerIndex);

		VectorT position = from.Value;
		return list.MinBy(z => (z.Teleport - position).Length());
	}

	/// <summary>
	/// Inserts a new map entry in the database.
	/// </summary>
	internal async Task InsertMapInfo([CallerMemberName] string methodName = "")
	{
		var mapInfo = new MapDto
		{
			Name = this.Name!,
			Author = "Unknown", // Or set appropriately
			Tier = this.Tier,
			Stages = this.Stages,
			Bonuses = this.Bonuses,
			Ranked = false
		};

		try
		{
			this.ID = await _dataService.InsertMapInfoAsync(mapInfo);

			_logger.LogInformation("[{ClassName}] {MethodName} -> Map '{Map}' inserted successfully with ID {ID}.",
				nameof(Map), methodName, this.Name, this.ID
			);
		}
		catch (Exception ex)
		{
			_logger.LogCritical(ex, "[{ClassName}] {MethodName} -> Failed to insert map '{Map}'. Exception: {ExceptionMessage}",
				nameof(Map), methodName, this.Name, ex.Message
			);
			throw new InvalidOperationException($"Failed to insert map '{Name}'. See inner exception for details.", ex);
		}
	}

	/// <summary>
	/// Updates last played, stages, bonuses for the map in the database.
	/// </summary>
	internal async Task UpdateMapInfo([CallerMemberName] string methodName = "")
	{
		var mapInfo = new MapDto
		{
			Name = this.Name!,
			Author = this.Author!,
			Tier = this.Tier,
			Stages = this.Stages,
			Bonuses = this.Bonuses,
			Ranked = this.Ranked,
			LastPlayed = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
		};

		try
		{
			await _dataService.UpdateMapInfoAsync(mapInfo, this.ID);

#if DEBUG
			_logger.LogDebug("[{ClassName}] {MethodName} -> Updated map '{Map}' (ID: {ID}).",
				nameof(Map), methodName, this.Name, this.ID
			);
#endif
		}
		catch (Exception ex)
		{
			_logger.LogCritical(ex, "[{ClassName}] {MethodName} -> Failed to update map '{Map}'. Exception Message: {ExceptionMessage}",
				nameof(Map), methodName, this.Name, ex.Message
			);
			throw new InvalidOperationException($"Failed to update map '{Name}'. See inner exception for details.", ex);
		}
	}

	/// <summary>
	/// Load/update/create Map table entry.
	/// Loads the record runs for the map as well.
	/// </summary>
	/// <param name="updateData">Should we run UPDATE query for the map</param>
	internal async Task LoadMapInfo(bool updateData = true, [CallerMemberName] string methodName = "")
	{
		bool newMap = false;

		var mapInfo = await _dataService.GetMapInfoAsync(this.Name!);

		if (mapInfo != null)
		{
			ID = mapInfo.ID;
			Author = mapInfo.Author;
			Tier = mapInfo.Tier;
			Ranked = mapInfo.Ranked;
			StagedLinear = mapInfo.StagedLinear;
			DateAdded = mapInfo.DateAdded;
			LastPlayed = mapInfo.LastPlayed;
		}
		else
		{
			newMap = true;
		}

		if (newMap)
		{
			await InsertMapInfo();
			return;
		}

		if (updateData)
			await UpdateMapInfo();

		var stopwatch = Stopwatch.StartNew();
		await LoadMapRecordRuns();
		stopwatch.Stop();

#if DEBUG
		_logger.LogDebug("[{ClassName}] {MethodName} -> Finished LoadMapRecordRuns in {Elapsed}ms",
			nameof(Map), methodName, stopwatch.ElapsedMilliseconds);
#endif
	}

	/// <summary>
	/// Extracts Map, Bonus, Stage record runs and the total completions for each style. 
	/// (NOT TESTED WITH MORE THAN 1 STYLE)
	/// For the Map WR it also gets the Checkpoints data.
	/// </summary>
	internal async Task LoadMapRecordRuns([CallerMemberName] string methodName = "")
	{
		//this.ConnectedMapTimes.Clear(); // This is for Custom Replays (PB replays?) - T

		var runs = await _dataService.GetMapRecordRunsAsync(this.ID);

		_logger.LogInformation("[{ClassName}] {MethodName} -> Received {Length} runs from `GetMapRecordRunsAsync`",
			nameof(Map), methodName, runs.Count
		);

		foreach (var run in runs)
		{
			switch (run.Type)
			{
				case 0: // Map WR data and total completions
					WR[run.Style].ID = run.ID;
					WR[run.Style].RunTime = run.RunTime;
					WR[run.Style].StartVelX = run.StartVelX;
					WR[run.Style].StartVelY = run.StartVelY;
					WR[run.Style].StartVelZ = run.StartVelZ;
					WR[run.Style].EndVelX = run.EndVelX;
					WR[run.Style].EndVelY = run.EndVelY;
					WR[run.Style].EndVelZ = run.EndVelZ;
					WR[run.Style].RunDate = run.RunDate;
					WR[run.Style].Name = run.Name;
					/// ConnectedMapTimes.Add(run.ID);
					MapCompletions[run.Style] = run.TotalCount;

					SetReplayData(run.Type, run.Style, run.Stage, run.ReplayFrames!);
					break;

				case 1: // Bonus WR data and total completions
					BonusWR[run.Stage][run.Style].ID = run.ID;
					BonusWR[run.Stage][run.Style].RunTime = run.RunTime;
					BonusWR[run.Stage][run.Style].StartVelX = run.StartVelX;
					BonusWR[run.Stage][run.Style].StartVelY = run.StartVelY;
					BonusWR[run.Stage][run.Style].StartVelZ = run.StartVelZ;
					BonusWR[run.Stage][run.Style].EndVelX = run.EndVelX;
					BonusWR[run.Stage][run.Style].EndVelY = run.EndVelY;
					BonusWR[run.Stage][run.Style].EndVelZ = run.EndVelZ;
					BonusWR[run.Stage][run.Style].RunDate = run.RunDate;
					BonusWR[run.Stage][run.Style].Name = run.Name;
					BonusCompletions[run.Stage][run.Style] = run.TotalCount;

					SetReplayData(run.Type, run.Style, run.Stage, run.ReplayFrames!);
					break;

				case 2: // Stage WR data and total completions
					StageWR[run.Stage][run.Style].ID = run.ID;
					StageWR[run.Stage][run.Style].RunTime = run.RunTime;
					StageWR[run.Stage][run.Style].StartVelX = run.StartVelX;
					StageWR[run.Stage][run.Style].StartVelY = run.StartVelY;
					StageWR[run.Stage][run.Style].StartVelZ = run.StartVelZ;
					StageWR[run.Stage][run.Style].EndVelX = run.EndVelX;
					StageWR[run.Stage][run.Style].EndVelY = run.EndVelY;
					StageWR[run.Stage][run.Style].EndVelZ = run.EndVelZ;
					StageWR[run.Stage][run.Style].RunDate = run.RunDate;
					StageWR[run.Stage][run.Style].Name = run.Name;
					StageCompletions[run.Stage][run.Style] = run.TotalCount;

					SetReplayData(run.Type, run.Style, run.Stage, run.ReplayFrames!);
					break;

				case 3: // Checkpoint segment WR data and total completions (non-staged maps only)
					CheckpointWR[run.Stage][run.Style].ID = run.ID;
					CheckpointWR[run.Stage][run.Style].RunTime = run.RunTime;
					CheckpointWR[run.Stage][run.Style].StartVelX = run.StartVelX;
					CheckpointWR[run.Stage][run.Style].StartVelY = run.StartVelY;
					CheckpointWR[run.Stage][run.Style].StartVelZ = run.StartVelZ;
					CheckpointWR[run.Stage][run.Style].EndVelX = run.EndVelX;
					CheckpointWR[run.Stage][run.Style].EndVelY = run.EndVelY;
					CheckpointWR[run.Stage][run.Style].EndVelZ = run.EndVelZ;
					CheckpointWR[run.Stage][run.Style].RunDate = run.RunDate;
					CheckpointWR[run.Stage][run.Style].Name = run.Name;
					CheckpointCompletions[run.Stage][run.Style] = run.TotalCount;

					SetReplayData(run.Type, run.Style, run.Stage, run.ReplayFrames!);
					break;
			}
		}

		foreach (int style in Config.Styles)
		{
			if (MapCompletions[style] > 0 && WR[style].ID != -1)
			{
#if DEBUG
				_logger.LogDebug("[{ClassName}] {MethodName} -> LoadMapRecordRuns : Map -> Loaded {MapCompletions} runs (MapID {MapID} | Style {Style}). WR by {PlayerName} - {Time}",
					nameof(Map), methodName, this.MapCompletions[style], this.ID, style, this.WR[style].Name, PlayerHud.FormatTime(this.WR[style].RunTime)
				);
#endif

				var stopwatch = Stopwatch.StartNew();
				await this.WR[style].LoadCheckpoints(); // Load the checkpoints for the WR and Style combo
				stopwatch.Stop();

				_logger.LogInformation("[{ClassName}] {MethodName} -> Finished WR.[{Style}].LoadCheckpoints() in {ElapsedMilliseconds}ms",
					nameof(Map), methodName, style, stopwatch.ElapsedMilliseconds
				);
			}
		}
	}

	/// <summary>
	/// Populates the content-template replay data (MapWR / AllStageWR / AllBonusWR / AllCheckpointWR)
	/// for a record run retrieved from MapTimes data. These are pure content templates - loading data
	/// here does not start or spawn any bot; replays only ever start via ReplayManager.RequestReplay.
	/// </summary>
	/// <param name="type">Type - 0 = Map, 1 = Bonus, 2 = Stage, 3 = Checkpoint segment</param>
	/// <param name="style">Style to add</param>
	/// <param name="stage">Stage to add</param>
	/// <param name="replayFramesBase64">Base64 encoded string for the replay_frames</param>
	internal void SetReplayData(int type, int style, int stage, ReplayFramesString replayFramesBase64, [CallerMemberName] string methodName = "")
	{
		List<ReplayFrame> frames = ReplayFrame.Deserialize(replayFramesBase64);

		switch (type)
		{
			case 0: // Map Replays
				_logger.LogTrace("[{ClassName}] {MethodName} -> SetReplayData -> [MapWR] Setting run {RunID} {RunTime} (Ticks = {RunTicks}; Frames = {TotalFrames})",
					nameof(Map), methodName, this.WR[style].ID, PlayerHud.FormatTime(this.WR[style].RunTime), this.WR[style].RunTime, frames.Count
				);
				if (this.ReplayManager.MapWR.IsPlaying)
					this.ReplayManager.MapWR.Stop();

				this.ReplayManager.MapWR.RecordPlayerName = this.WR[style].Name!;
				this.ReplayManager.MapWR.RecordRunTime = this.WR[style].RunTime;
				this.ReplayManager.MapWR.Frames = frames;
				this.ReplayManager.MapWR.MapTimeID = this.WR[style].ID;
				this.ReplayManager.MapWR.MapID = this.ID;
				this.ReplayManager.MapWR.Type = 0;
				for (int i = 0; i < frames.Count; i++) // Load the situations for the replay
				{
					ReplayFrame f = frames[i];
					switch (f.Situation)
					{
						case ReplayFrameSituation.START_ZONE_ENTER or ReplayFrameSituation.START_ZONE_EXIT:
							this.ReplayManager.MapWR.MapSituations.Add(i);
							/// Console.WriteLine($"START_ZONE_ENTER: {i} | Situation {f.Situation}");
							break;
						case ReplayFrameSituation.STAGE_ZONE_ENTER or ReplayFrameSituation.STAGE_ZONE_EXIT:
							this.ReplayManager.MapWR.StageEnterSituations.Add(i);
							/// Console.WriteLine($"STAGE_ZONE_ENTER: {i} | Situation {f.Situation}");
							break;
						case ReplayFrameSituation.CHECKPOINT_ZONE_ENTER or ReplayFrameSituation.CHECKPOINT_ZONE_EXIT:
							this.ReplayManager.MapWR.CheckpointEnterSituations.Add(i);
							/// Console.WriteLine($"CHECKPOINT_ZONE_ENTER: {i} | Situation {f.Situation}");
							break;
						case ReplayFrameSituation.END_ZONE_ENTER or ReplayFrameSituation.END_ZONE_EXIT:
							/// Console.WriteLine($"END_ZONE_ENTER: {i} | Situation {f.Situation}");
							break;
					}
				}
				break;
			case 1: // Bonus Replays
					// Skip if the same bonus run already exists
				if (this.ReplayManager.AllBonusWR[stage][style].RecordRunTime == this.BonusWR[stage][style].RunTime)
					break;
#if DEBUG
				_logger.LogDebug("[{ClassName}] {MethodName} -> SetReplayData -> [BonusWR] Adding run {ID} {Time} (Ticks = {Ticks}; Frames = {Frames}) to `ReplayManager.AllBonusWR`",
					nameof(Map), methodName, this.BonusWR[stage][style].ID, PlayerHud.FormatTime(this.BonusWR[stage][style].RunTime), this.BonusWR[stage][style].RunTime, frames.Count
				);
#endif

				// Add all stages found to a dictionary with their data
				this.ReplayManager.AllBonusWR[stage][style].MapID = this.ID;
				this.ReplayManager.AllBonusWR[stage][style].Frames = frames;
				this.ReplayManager.AllBonusWR[stage][style].RecordRunTime = this.BonusWR[stage][style].RunTime;
				this.ReplayManager.AllBonusWR[stage][style].RecordPlayerName = this.BonusWR[stage][style].Name!;
				this.ReplayManager.AllBonusWR[stage][style].MapTimeID = this.BonusWR[stage][style].ID;
				this.ReplayManager.AllBonusWR[stage][style].Stage = stage;
				this.ReplayManager.AllBonusWR[stage][style].Type = 1;
				this.ReplayManager.AllBonusWR[stage][style].RecordRank = 1;
				this.ReplayManager.AllBonusWR[stage][style].IsPlayable = true; // We set this to `true` else we overwrite it and need to call SetController method again
				for (int i = 0; i < frames.Count; i++)
				{
					ReplayFrame f = frames[i];
					switch (f.Situation)
					{
						case ReplayFrameSituation.START_ZONE_ENTER or ReplayFrameSituation.END_ZONE_EXIT:
							this.ReplayManager.AllBonusWR[stage][style].BonusSituations.Add(i);
							break;
					}
				}
				break;
			case 2: // Stage Replays
					// Skip if the same stage run already exists
				if (this.ReplayManager.AllStageWR[stage][style].RecordRunTime == this.StageWR[stage][style].RunTime)
					break;
#if DEBUG
				_logger.LogDebug("[{ClassName}] {MethodName} -> SetReplayData -> [StageWR] Adding run {ID} {Time} (Ticks = {Ticks}; Frames = {Frames}) to `ReplayManager.AllStageWR`",
					nameof(Map), methodName, this.StageWR[stage][style].ID, PlayerHud.FormatTime(this.StageWR[stage][style].RunTime), this.StageWR[stage][style].RunTime, frames.Count
				);
#endif

				// Add all stages found to a dictionary with their data
				this.ReplayManager.AllStageWR[stage][style].MapID = this.ID;
				this.ReplayManager.AllStageWR[stage][style].Frames = frames;
				this.ReplayManager.AllStageWR[stage][style].RecordRunTime = this.StageWR[stage][style].RunTime;
				this.ReplayManager.AllStageWR[stage][style].RecordPlayerName = this.StageWR[stage][style].Name!;
				this.ReplayManager.AllStageWR[stage][style].MapTimeID = this.StageWR[stage][style].ID;
				this.ReplayManager.AllStageWR[stage][style].Stage = stage;
				this.ReplayManager.AllStageWR[stage][style].Type = 2;
				this.ReplayManager.AllStageWR[stage][style].RecordRank = 1;
				this.ReplayManager.AllStageWR[stage][style].IsPlayable = true; // We set this to `true` else we overwrite it and need to call SetController method again
				for (int i = 0; i < frames.Count; i++)
				{
					ReplayFrame f = frames[i];
					switch (f.Situation)
					{
						case ReplayFrameSituation.STAGE_ZONE_ENTER:
							this.ReplayManager.AllStageWR[stage][style].StageEnterSituations.Add(i);
							break;
						case ReplayFrameSituation.STAGE_ZONE_EXIT:
							this.ReplayManager.AllStageWR[stage][style].StageExitSituations.Add(i);
							break;
					}
				}
				break;
			case 3: // Checkpoint segment Replays (non-staged maps only)
					// Skip if the same checkpoint run already exists
				if (this.ReplayManager.AllCheckpointWR[stage][style].RecordRunTime == this.CheckpointWR[stage][style].RunTime)
					break;
#if DEBUG
				_logger.LogDebug("[{ClassName}] {MethodName} -> SetReplayData -> [CheckpointWR] Adding run {ID} {Time} (Ticks = {Ticks}; Frames = {Frames}) to `ReplayManager.AllCheckpointWR`",
					nameof(Map), methodName, this.CheckpointWR[stage][style].ID, PlayerHud.FormatTime(this.CheckpointWR[stage][style].RunTime), this.CheckpointWR[stage][style].RunTime, frames.Count
				);
#endif

				// Add all checkpoints found to a dictionary with their data
				this.ReplayManager.AllCheckpointWR[stage][style].MapID = this.ID;
				this.ReplayManager.AllCheckpointWR[stage][style].Frames = frames;
				this.ReplayManager.AllCheckpointWR[stage][style].RecordRunTime = this.CheckpointWR[stage][style].RunTime;
				this.ReplayManager.AllCheckpointWR[stage][style].RecordPlayerName = this.CheckpointWR[stage][style].Name!;
				this.ReplayManager.AllCheckpointWR[stage][style].MapTimeID = this.CheckpointWR[stage][style].ID;
				this.ReplayManager.AllCheckpointWR[stage][style].Stage = stage;
				this.ReplayManager.AllCheckpointWR[stage][style].Type = 3;
				this.ReplayManager.AllCheckpointWR[stage][style].RecordRank = 1;
				this.ReplayManager.AllCheckpointWR[stage][style].IsPlayable = true; // We set this to `true` else we overwrite it and need to call SetController method again
				for (int i = 0; i < frames.Count; i++)
				{
					ReplayFrame f = frames[i];
					switch (f.Situation)
					{
						case ReplayFrameSituation.CHECKPOINT_ZONE_ENTER:
							this.ReplayManager.AllCheckpointWR[stage][style].CheckpointEnterSituations.Add(i);
							break;
						case ReplayFrameSituation.CHECKPOINT_ZONE_EXIT:
							this.ReplayManager.AllCheckpointWR[stage][style].CheckpointExitSituations.Add(i);
							break;
					}
				}
				break;
		}
	}

	public void KickReplayBot(int index)
	{
		if (this.ReplayManager.Pool[index].Controller == null)
			return;

		int? id_to_kick = this.ReplayManager.Pool[index].Controller!.UserId;
		if (id_to_kick == null)
			return;

		this.ReplayManager.Pool.RemoveAt(index);
		Server.ExecuteCommand($"kickid {id_to_kick}; bot_quota {this.ReplayManager.Pool.Count}");
	}

	private void KillServerCommandEnts([CallerMemberName] string methodName = "")
	{
		var pointServerCommands = Utilities.FindAllEntitiesByDesignerName<CPointServerCommand>("point_servercommand");

		foreach (var servercmd in pointServerCommands)
		{
			if (servercmd == null) continue;
			_logger.LogTrace("[{ClassName}] {MethodName} -> Killed point_servercommand ent: {ServerCMD}", nameof(Map), methodName, servercmd.Handle);
			servercmd.Remove();
		}
	}
}
