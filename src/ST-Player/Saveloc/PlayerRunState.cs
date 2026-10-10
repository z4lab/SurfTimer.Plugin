using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// A player's in-progress run as stored in player_run_states.state (JSON): the pawn and run state of a saveloc
/// (Saveloc / SavelocRun capture and apply it) plus what a saveloc doesn't keep - practice, pause, stage failures and
/// where the run's replay frames stand. Resuming it continues the run as it was, records included.
/// </summary>
internal sealed class PlayerRunState
{
	/// <summary>Bumped when the meaning of the stored state changes - other versions are discarded</summary>
	internal const int CurrentVersion = 1;

	private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	[JsonPropertyName("v")] public int Version { get; set; } = CurrentVersion;
	[JsonPropertyName("tick_rate")] public int TickRate { get; set; } = ReplayCodec.TickRate;
	[JsonPropertyName("map")] public string MapName { get; set; } = "";
	/// <summary>Map.ZonesHash when it was saved - a changed course isn't resumed</summary>
	[JsonPropertyName("zones")] public string ZonesHash { get; set; } = "";

	// ---- Pawn ----
	[JsonPropertyName("pos")] public float[] Position { get; set; } = [0, 0, 0];
	[JsonPropertyName("ang")] public float[] Angles { get; set; } = [0, 0];
	[JsonPropertyName("vel")] public float[] Velocity { get; set; } = [0, 0, 0];
	[JsonPropertyName("ducked")] public bool Ducked { get; set; }
	[JsonPropertyName("duck")] public float DuckAmount { get; set; }
	[JsonPropertyName("ladder")] public bool Ladder { get; set; }
	[JsonPropertyName("gravity")] public float Gravity { get; set; } = 1f;
	[JsonPropertyName("course")] public short CourseBonus { get; set; }

	// ---- Run ----
	[JsonPropertyName("ticks")] public int Ticks { get; set; }
	[JsonPropertyName("style")] public short Style { get; set; }
	[JsonPropertyName("practice")] public bool Practice { get; set; }
	[JsonPropertyName("paused")] public bool Paused { get; set; }
	[JsonPropertyName("stage_mode")] public bool StageMode { get; set; }
	[JsonPropertyName("bonus_mode")] public bool BonusMode { get; set; }
	[JsonPropertyName("stage")] public short Stage { get; set; }
	[JsonPropertyName("checkpoint")] public short Checkpoint { get; set; }
	[JsonPropertyName("bonus")] public short Bonus { get; set; }
	[JsonPropertyName("stage_failures")] public int StageFailures { get; set; }
	[JsonPropertyName("stage_entry_vel")] public float[] StageEntryVelocity { get; set; } = [0, 0, 0];
	[JsonPropertyName("cp_entry_vel")] public float[] CheckpointEntryVelocity { get; set; } = [0, 0, 0];
	/// <summary>Tick the current stage / checkpoint segment started (CurrentRun.RunTime during a run)</summary>
	[JsonPropertyName("segment_start")] public int RunTime { get; set; }
	[JsonPropertyName("start_vel")] public float[] StartVelocity { get; set; } = [0, 0, 0];
	[JsonPropertyName("splits")] public List<Split> Splits { get; set; } = new();
	[JsonPropertyName("sync")] public int[] Sync { get; set; } = [0, 0, 0, 0];
	[JsonPropertyName("prespeed")] public float[]? LastPrespeed { get; set; }

	// ---- Replay ----
	/// <summary>No replay for this run (practice, dropped while idle, or only a periodic save survived)</summary>
	[JsonPropertyName("replay_dropped")] public bool ReplayDropped { get; set; }
	/// <summary>Replay frames recorded up to this state - the stored replay is cut to it</summary>
	[JsonPropertyName("frames")] public int FrameCount { get; set; }
	[JsonPropertyName("stage_enter")] public List<int> StageEnterSituations { get; set; } = new();
	[JsonPropertyName("stage_exit")] public List<int> StageExitSituations { get; set; } = new();
	[JsonPropertyName("cp_enter")] public List<int> CheckpointEnterSituations { get; set; } = new();
	[JsonPropertyName("cp_exit")] public List<int> CheckpointExitSituations { get; set; } = new();
	[JsonPropertyName("map_situations")] public List<int> MapSituations { get; set; } = new();
	[JsonPropertyName("bonus_situations")] public List<int> BonusSituations { get; set; } = new();

	/// <summary>One split of the run so far (CheckpointEntity)</summary>
	internal sealed class Split
	{
		[JsonPropertyName("cp")] public short CP { get; set; }
		[JsonPropertyName("enter")] public int RunTime { get; set; }
		[JsonPropertyName("exit")] public int EndTouch { get; set; }
		[JsonPropertyName("attempts")] public int Attempts { get; set; }
		[JsonPropertyName("start_vel")] public float[] StartVelocity { get; set; } = [0, 0, 0];
		[JsonPropertyName("end_vel")] public float[] EndVelocity { get; set; } = [0, 0, 0];
	}

	/// <summary>The player's running run on their pawn - null when the timer doesn't run</summary>
	internal static PlayerRunState? Capture(Player player, CCSPlayerPawn pawn, Map map)
	{
		var saveloc = Saveloc.Capture(0, player, pawn, player, SavelocSource.Run, null, player.CourseBonus);
		var run = saveloc?.Run;
		if (saveloc == null || run == null)
			return null;

		var recorder = player.ReplayRecorder;
		return new PlayerRunState
		{
			MapName = map.Name ?? "",
			ZonesHash = map.ZonesHash,
			Position = [saveloc.Position.X, saveloc.Position.Y, saveloc.Position.Z],
			Angles = [saveloc.Angles.X, saveloc.Angles.Y],
			Velocity = [saveloc.Velocity.X, saveloc.Velocity.Y, saveloc.Velocity.Z],
			Ducked = saveloc.Ducked,
			DuckAmount = saveloc.DuckAmount,
			Ladder = saveloc.MoveType == MoveType_t.MOVETYPE_LADDER,
			Gravity = saveloc.GravityScale,
			CourseBonus = saveloc.CourseBonus,

			Ticks = run.Ticks,
			Style = run.Style,
			Practice = player.Timer.IsPracticeMode,
			Paused = player.Timer.IsPaused,
			StageMode = run.IsStageMode,
			BonusMode = run.IsBonusMode,
			Stage = run.Stage,
			Checkpoint = run.Checkpoint,
			Bonus = run.Bonus,
			StageFailures = player.Timer.StageFailures,
			StageEntryVelocity = Floats(run.StageEntryVelocity),
			CheckpointEntryVelocity = Floats(run.CheckpointEntryVelocity),
			RunTime = run.RunTime,
			StartVelocity = Floats(run.StartVelocity),
			Splits = run.Splits.Select(s => new Split
			{
				CP = s.CP,
				RunTime = s.RunTime,
				EndTouch = s.EndTouch,
				Attempts = s.Attempts,
				StartVelocity = [s.StartVelX, s.StartVelY, s.StartVelZ],
				EndVelocity = [s.EndVelX, s.EndVelY, s.EndVelZ],
			}).ToList(),
			Sync = [run.Sync.Good, run.Sync.Total, run.Sync.SegmentGood, run.Sync.SegmentTotal],
			LastPrespeed = run.LastPrespeed is VectorT prespeed ? Floats(prespeed) : null,

			ReplayDropped = player.Timer.IsPracticeMode || recorder.DroppedForRun,
			FrameCount = recorder.Frames.Count,
			StageEnterSituations = [.. recorder.StageEnterSituations],
			StageExitSituations = [.. recorder.StageExitSituations],
			CheckpointEnterSituations = [.. recorder.CheckpointEnterSituations],
			CheckpointExitSituations = [.. recorder.CheckpointExitSituations],
			MapSituations = [.. recorder.MapSituations],
			BonusSituations = [.. recorder.BonusSituations],
		};
	}

	/// <summary>The pawn part (and the run, for Describe) - ApplyPawn puts the player there</summary>
	internal Saveloc ToSaveloc() => new()
	{
		Source = SavelocSource.Run,
		Position = Vector(Position),
		Angles = new QAngleT(At(Angles, 0), At(Angles, 1), 0),
		Velocity = Vector(Velocity),
		Ducked = Ducked,
		DuckAmount = DuckAmount,
		MoveType = Ladder ? MoveType_t.MOVETYPE_LADDER : MoveType_t.MOVETYPE_WALK,
		GravityScale = Gravity,
		CourseBonus = CourseBonus,
		Run = ToRun(),
	};

	/// <summary>The run part - SavelocRun.Apply continues it</summary>
	internal SavelocRun ToRun() => new()
	{
		Ticks = Ticks,
		Style = Style,
		IsStageMode = StageMode,
		IsBonusMode = BonusMode,
		Stage = Stage,
		Checkpoint = Checkpoint,
		Bonus = Bonus,
		StageEntryVelocity = Vector(StageEntryVelocity),
		CheckpointEntryVelocity = Vector(CheckpointEntryVelocity),
		RunTime = RunTime,
		StartVelocity = Vector(StartVelocity),
		Splits = Splits.Select(s => new CheckpointEntity(s.CP, s.RunTime,
			At(s.StartVelocity, 0), At(s.StartVelocity, 1), At(s.StartVelocity, 2),
			At(s.EndVelocity, 0), At(s.EndVelocity, 1), At(s.EndVelocity, 2), s.EndTouch, s.Attempts)).ToList(),
		Sync = (At(Sync, 0), At(Sync, 1), At(Sync, 2), At(Sync, 3)),
		LastPrespeed = LastPrespeed != null ? Vector(LastPrespeed) : null,
	};

	/// <summary>"stage 3 · 01:23.456"</summary>
	internal string Describe() => ToSaveloc().Describe();

	internal string Serialize() => JsonSerializer.Serialize(this, Json);

	/// <summary>Null for text that isn't a state of this version</summary>
	internal static PlayerRunState? Deserialize(string json)
	{
		try
		{
			var state = JsonSerializer.Deserialize<PlayerRunState>(json, Json);
			return state?.Version == CurrentVersion ? state : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>Every number usable (no NaN / infinity from a broken row)</summary>
	internal bool IsSane() =>
		Ticks >= 0 && Ticks <= ReplayCodec.TickRate * 86400 && FrameCount >= 0
		&& new[] { Position, Angles, Velocity, StageEntryVelocity, CheckpointEntryVelocity, StartVelocity, LastPrespeed ?? [] }
			.All(values => values.All(float.IsFinite))
		&& float.IsFinite(DuckAmount) && float.IsFinite(Gravity);

	private static float[] Floats(VectorT vector) => [vector.X, vector.Y, vector.Z];
	private static VectorT Vector(float[] values) => new(At(values, 0), At(values, 1), At(values, 2));
	private static float At(float[] values, int index) => index < values.Length ? values[index] : 0f;
	private static int At(int[] values, int index) => index < values.Length ? values[index] : 0;
}
