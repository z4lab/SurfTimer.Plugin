using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SurfTimer;

public class Map : MapEntity
{
	public int TotalCheckpoints { get; set; } = 0;
	/// <summary>
	/// Checkpoint segment records on linear maps: segment 1 is map start -> cp1, segment N is
	/// cp(N-1) -> cpN, and the last one (TotalCheckpoints + 1) is the last cp -> map end.
	/// 0 on staged maps and maps without checkpoints.
	/// </summary>
	public int CheckpointSegments => this.Stages == 0 && this.TotalCheckpoints > 0 ? this.TotalCheckpoints + 1 : 0;
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

	// Constructor
	/// <param name="stagesAsCheckpoints">The map's stages_as_checkpoints setting - read before the zones load</param>
	internal Map(string name, bool stagesAsCheckpoints = false)
	{
		// Resolve the logger instance from the DI container
		_logger = SurfTimer.ServiceProvider.GetRequiredService<ILogger<Map>>();

		// Set map name
		this.Name = name;

		// Stages as checkpoints decides how the zones are read - fixed until the map loads again
		this.StagesAsCheckpoints = stagesAsCheckpoints;
		ZoneName.StagesAsCheckpoints = stagesAsCheckpoints;

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
			? new Dictionary<int, PersonalBest>[this.CheckpointSegments + 1]
			: Array.Empty<Dictionary<int, PersonalBest>>();
		this.CheckpointCompletions = checkpointed
			? new Dictionary<int, int>[this.CheckpointSegments + 1]
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

			for (int i = 1; checkpointed && i <= this.CheckpointSegments; i++)
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

			if (type == ZoneType.StageStart)
				this.StageZoneCount = Math.Max(this.StageZoneCount, number);

			// Teleport targets by the map's own names (spawn_s2_start), then the zone mode
			var (teleport, angles) = FindTeleportTarget(trigger, type, number, destinations);
			if (!ZoneName.Remap(ref type, ref number))
				continue;
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

	// ---- Database ----

	// The map's courses by (kind, number) - map course = (Map, 0)
	private Dictionary<(CourseKind Kind, short Number), MapRepository.CourseRow> _courses = new();

	/// <summary>map_settings of this map</summary>
	internal Dictionary<string, string> Settings { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

	internal const string SettingStagedLinear = "staged_linear";
	internal const string SettingStagesAsCheckpoints = "stages_as_checkpoints";

	/// <summary>
	/// The map was loaded with its stages as checkpoints (linear). Fixed per load - changing the setting
	/// needs a map restart.
	/// </summary>
	internal bool StagesAsCheckpoints { get; }

	/// <summary>Stage zones the map itself has (sN_start), whatever the zone mode</summary>
	internal short StageZoneCount { get; private set; }
	internal const string SettingStartSpeedCap = "start_speed_cap";
	internal const string SettingReplays = "replays_enabled";
	internal const string SettingExitLimit = "exit_speed_limit";
	internal const string SettingExitLimitValue = "exit_speed_limit_value";

	/// <summary>The map turns the hard limit on leaving run starts on (exit_speed_limit)</summary>
	internal bool ExitLimitEnabled { get; private set; }

	/// <summary>
	/// Hard horizontal speed limit when leaving a run start (u/s) - null when the map doesn't use one.
	/// Follows a live change of the timer_settings.json default.
	/// </summary>
	internal float? ExitSpeedLimit => ExitLimitEnabled ? ExitSpeedLimitValue ?? Config.StartExitSpeedLimit : null;

	/// <summary>The map's own exit limit value - null follows Config.StartExitSpeedLimit</summary>
	internal float? ExitSpeedLimitValue { get; private set; }

	/// <summary>Start zone bhop cap of this map (u/s, 0 = off) - null uses Config.StartSpeedCap</summary>
	internal float? StartSpeedCap { get; private set; }

	/// <summary>False when the map's replays_enabled setting turns replay recording off</summary>
	internal bool RecordReplays { get; private set; } = true;

	internal ulong? WorkshopId { get; set; }

	/// <summary>All courses of the map (from the database, incl. ones whose zones were removed)</summary>
	internal IEnumerable<MapRepository.CourseRow> Courses => _courses.Values;

	/// <summary>
	/// The course id of a run type (0 map, 1 bonus, 2 stage, 3 checkpoint segment) and number, 0 if unknown.
	/// </summary>
	internal int CourseId(int type, short number)
	{
		var kind = CourseKinds.FromRunType(type);
		return _courses.TryGetValue((kind, kind == CourseKind.Map ? (short)0 : number), out var course) ? course.Id : 0;
	}

	internal MapRepository.CourseRow? Course(CourseKind kind, short number) =>
		_courses.TryGetValue((kind, kind == CourseKind.Map ? (short)0 : number), out var course) ? course : null;

	/// <summary>
	/// Every course the map's zones define.
	/// </summary>
	private IEnumerable<(CourseKind, short)> ZoneCourses()
	{
		yield return (CourseKind.Map, 0);
		for (short stage = 1; stage <= this.Stages; stage++)
			yield return (CourseKind.Stage, stage);
		for (short bonus = 1; bonus <= this.Bonuses; bonus++)
			yield return (CourseKind.Bonus, bonus);
		for (short cp = 1; cp <= this.CheckpointSegments; cp++)
			yield return (CourseKind.Checkpoint, cp);
	}

	/// <summary>
	/// Loads the map's row (created on first load), courses, authors and settings, then its records.
	/// </summary>
	internal async Task LoadMapInfo([CallerMemberName] string methodName = "")
	{
		var row = await MapRepository.GetOrCreateAsync(this.Name!);
		this.ID = row.Id;
		this.Ranked = row.Ranked;
		this.DateAdded = PlayerRepository.ToUnix(row.CreatedAt);
		this.LastPlayed = PlayerRepository.ToUnix(row.LastPlayedAt);
		this.WorkshopId = row.WorkshopId;

		var courses = await MapRepository.EnsureCoursesAsync(this.ID, ZoneCourses());
		_courses = courses.ToDictionary(c => (c.Kind, c.Number));
		this.Tier = (short)(Course(CourseKind.Map, 0)?.Tier ?? 0);

		var authors = await MapRepository.GetAuthorsAsync(this.ID);
		this.Author = authors.Count > 0 ? string.Join(", ", authors) : null;

		this.Settings = await MapRepository.GetSettingsAsync(this.ID);
		ApplySettings();

		_logger.LogInformation("[{ClassName}] {MethodName} -> Map '{Map}' (ID {ID}) with {Courses} courses, tier {Tier}, ranked {Ranked}",
			nameof(Map), methodName, this.Name, this.ID, _courses.Count, this.Tier, this.Ranked);

		var stopwatch = Stopwatch.StartNew();
		await LoadMapRecordRuns();
		stopwatch.Stop();

#if DEBUG
		_logger.LogDebug("[{ClassName}] {MethodName} -> Finished LoadMapRecordRuns in {Elapsed}ms",
			nameof(Map), methodName, stopwatch.ElapsedMilliseconds);
#endif
	}

	/// <summary>
	/// Map options stored in map_settings. Cvar overrides are applied on the next frame (natives are main
	/// thread only - this also runs after the map load's awaits).
	/// </summary>
	internal void ApplySettings()
	{
		this.StagedLinear = IsTrue(SettingStagedLinear, false);
		this.RecordReplays = IsTrue(SettingReplays, true);
		this.StartSpeedCap = Settings.TryGetValue(SettingStartSpeedCap, out var cap)
			&& float.TryParse(cap, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
			? Math.Max(0, parsed)
			: null;

		this.ExitSpeedLimitValue = Settings.TryGetValue(SettingExitLimitValue, out var limit)
			&& float.TryParse(limit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedLimit)
			&& parsedLimit > 0
			? parsedLimit
			: null;
		this.ExitLimitEnabled = IsTrue(SettingExitLimit, false);

		var snapshot = new Dictionary<string, string>(Settings, StringComparer.OrdinalIgnoreCase);
		Server.NextFrame(() => MapCvars.Apply(snapshot));

		bool IsTrue(string key, bool fallback) =>
			Settings.TryGetValue(key, out var value) ? value is "1" or "true" : fallback;
	}

	internal void SetCourseTier(CourseKind kind, short number, byte? tier)
	{
		var course = Course(kind, number);
		if (course == null)
			return;

		course.Tier = tier;
		if (kind == CourseKind.Map)
			this.Tier = (short)(tier ?? 0);
	}

	/// <summary>
	/// Loads every course's WR and completions (from course_stats - no replay data) and, for WRs whose
	/// replay changed since the last load, their replay. Called on map load and after every saved time.
	/// </summary>
	internal async Task LoadMapRecordRuns([CallerMemberName] string methodName = "")
	{
		var records = await TimeRepository.GetMapRecordsAsync(this.ID);
		var wrIds = records.Where(r => r.WrTimeId != null).Select(r => r.WrTimeId!.Value).Distinct().ToList();
		var wrTimes = (await TimeRepository.GetTimesAsync(wrIds)).ToDictionary(t => t.Id);

		_logger.LogInformation("[{ClassName}] {MethodName} -> {Records} course records, {Wrs} WRs",
			nameof(Map), methodName, records.Count, wrTimes.Count);

		foreach (var record in records)
		{
			short type = CourseKinds.ToRunType(record.Kind);
			short number = record.Kind == CourseKind.Map ? (short)0 : record.Number;
			int style = record.StyleId;

			SetCompletions(type, number, style, (int)record.Completions);

			var wr = WrFor(type, number, style);
			if (wr == null)
				continue; // A course the map's zones don't have anymore, or an unknown style

			if (record.WrTimeId is not int wrId || !wrTimes.TryGetValue(wrId, out var time))
			{
				wr.Clear();
				continue;
			}

			time.Fill(wr);
			if (record.Kind == CourseKind.Map)
				await wr.LoadCheckpoints();

			// Replays are only downloaded when the WR's replay changed
			var template = ReplayTemplateFor(type, number, style);
			if (template != null && template.ReplayId != time.ReplayId)
			{
				var data = time.ReplayId is int replayId ? await TimeRepository.GetReplayDataAsync(replayId) : null;
				var frames = data != null ? ReplayCodec.Decode(data) : new List<ReplayFrame>();
				SetReplayData(type, style, number, frames, time.ReplayId);
			}
		}
	}

	/// <summary>
	/// The WR object of a run type, number and style - null when the map / style doesn't have it.
	/// </summary>
	internal PersonalBest? WrFor(short type, short number, int style)
	{
		Dictionary<int, PersonalBest>? byStyle = type switch
		{
			0 => WR,
			1 => number > 0 && number < BonusWR.Length ? BonusWR[number] : null,
			2 => number > 0 && number < StageWR.Length ? StageWR[number] : null,
			3 => number > 0 && number < CheckpointWR.Length ? CheckpointWR[number] : null,
			_ => null,
		};
		return byStyle != null && byStyle.TryGetValue(style, out var wr) ? wr : null;
	}

	private void SetCompletions(short type, short number, int style, int completions)
	{
		Dictionary<int, int>? byStyle = type switch
		{
			0 => MapCompletions,
			1 => number > 0 && number < BonusCompletions.Length ? BonusCompletions[number] : null,
			2 => number > 0 && number < StageCompletions.Length ? StageCompletions[number] : null,
			3 => number > 0 && number < CheckpointCompletions.Length ? CheckpointCompletions[number] : null,
			_ => null,
		};
		if (byStyle != null && byStyle.ContainsKey(style))
			byStyle[style] = completions;
	}

	private ReplayPlayer? ReplayTemplateFor(short type, short number, int style)
	{
		if (type == 0)
			return style == 0 ? this.ReplayManager.MapWR : null; // One map WR template (normal style)

		Dictionary<int, ReplayPlayer>[] byNumber = type switch
		{
			1 => this.ReplayManager.AllBonusWR,
			2 => this.ReplayManager.AllStageWR,
			_ => this.ReplayManager.AllCheckpointWR,
		};
		return number > 0 && number < byNumber.Length && byNumber[number] != null && byNumber[number].TryGetValue(style, out var template)
			? template
			: null;
	}

	/// <summary>
	/// Puts a WR's replay into its content template (MapWR / AllBonusWR / AllStageWR / AllCheckpointWR).
	/// Templates are only content - nothing is spawned here; replays start via ReplayManager.RequestReplay.
	/// </summary>
	/// <param name="type">0 = Map, 1 = Bonus, 2 = Stage, 3 = Checkpoint segment</param>
	/// <param name="frames">Decoded replay (empty when the WR has none)</param>
	/// <param name="replayId">replays.id the frames came from</param>
	internal void SetReplayData(short type, int style, short number, List<ReplayFrame> frames, int? replayId, [CallerMemberName] string methodName = "")
	{
		var template = ReplayTemplateFor(type, number, style);
		if (template == null)
			return;

		var wr = WrFor(type, number, style);
		if (template.IsPlaying)
			template.Stop();

		template.MapID = this.ID;
		template.Type = type;
		template.Stage = type == 0 ? 0 : number;
		template.MapTimeID = wr?.ID ?? -1;
		template.RecordRunTime = wr?.RunTime ?? -1;
		template.RecordPlayerName = wr?.Name ?? "N/A";
		template.RecordRank = 1;
		template.Frames = frames;
		template.ReplayId = replayId;
		template.IsPlayable = frames.Count > 0; // Set here, else it's only set when a bot is assigned

		// Zone situations of the replay - new lists, pool slots may still share the old ones
		var map = new List<int>();
		var bonus = new List<int>();
		var stageEnter = new List<int>();
		var stageExit = new List<int>();
		var checkpointEnter = new List<int>();
		var checkpointExit = new List<int>();
		for (int i = 0; i < frames.Count; i++)
		{
			switch (frames[i].Situation)
			{
				case ReplayFrameSituation.START_ZONE_ENTER or ReplayFrameSituation.START_ZONE_EXIT
					or ReplayFrameSituation.END_ZONE_ENTER or ReplayFrameSituation.END_ZONE_EXIT:
					(type == 1 ? bonus : map).Add(i);
					break;
				case ReplayFrameSituation.STAGE_ZONE_ENTER:
					stageEnter.Add(i);
					break;
				case ReplayFrameSituation.STAGE_ZONE_EXIT:
					stageExit.Add(i);
					break;
				case ReplayFrameSituation.CHECKPOINT_ZONE_ENTER:
					checkpointEnter.Add(i);
					break;
				case ReplayFrameSituation.CHECKPOINT_ZONE_EXIT:
					checkpointExit.Add(i);
					break;
			}
		}
		template.MapSituations = map;
		template.BonusSituations = bonus;
		template.StageEnterSituations = stageEnter;
		template.StageExitSituations = stageExit;
		template.CheckpointEnterSituations = checkpointEnter;
		template.CheckpointExitSituations = checkpointExit;

#if DEBUG
		_logger.LogDebug("[{ClassName}] {MethodName} -> WR replay of type {Type} {Number} (style {Style}): time {TimeId}, replay {ReplayId}, {Frames} frames",
			nameof(Map), methodName, type, number, style, template.MapTimeID, replayId, frames.Count);
#endif
	}

	public void KickReplayBot(int index)
	{
		if (this.ReplayManager.Pool[index].Controller == null)
			return;

		int? id_to_kick = this.ReplayManager.Pool[index].Controller!.UserId;
		if (id_to_kick == null)
			return;

		this.ReplayManager.Pool.RemoveAt(index);
		SurfTimer.AllowPluginKick(id_to_kick.Value); // Past the replay bot protection against map kicks
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
