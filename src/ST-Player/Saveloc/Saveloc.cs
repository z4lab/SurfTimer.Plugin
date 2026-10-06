using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>Where a saveloc's state came from</summary>
internal enum SavelocSource
{
	/// <summary>The saver's own run (timer running)</summary>
	Run,
	/// <summary>The saver, timer not running</summary>
	Unrun,
	/// <summary>A player the saver spectated</summary>
	Player,
	/// <summary>A replay bot the saver spectated (never has run state)</summary>
	Replay,
}

/// <summary>
/// One saveloc: a pawn state (position, view, velocity, crouch, move type, gravity) and - when it was saved
/// during a run - the run's state, so loading it continues that run (in practice mode). Ids are server-wide
/// per map session (SavelocSession).
/// </summary>
internal sealed class Saveloc
{
	internal int Id { get; init; }
	internal ulong OwnerSteamId { get; init; }
	internal string OwnerName { get; init; } = "";
	internal DateTime CreatedAt { get; init; } = DateTime.UtcNow;
	internal SavelocSource Source { get; init; }
	/// <summary>Spectated player / replay holder</summary>
	internal string? SourceName { get; init; }

	internal VectorT Position { get; init; }
	internal QAngleT Angles { get; init; }
	internal VectorT Velocity { get; init; }
	internal bool Ducked { get; init; }
	internal float DuckAmount { get; init; }
	internal MoveType_t MoveType { get; init; } = MoveType_t.MOVETYPE_WALK;
	internal float GravityScale { get; init; } = 1f;

	/// <summary>The course it was saved on (0 = map, N = bonus N) - loading it locks the player to that course</summary>
	internal short CourseBonus { get; init; }

	/// <summary>Run state - null for an unrun saveloc</summary>
	internal SavelocRun? Run { get; init; }

	/// <summary>The owner's set up to and including this saveloc when it was made (joining it falls back to this)</summary>
	internal List<int> Path { get; set; } = new();

	/// <summary>"stage 3 · 01:23.456", "unrun", "replay of X"</summary>
	internal string Describe()
	{
		if (Run == null)
			return Source == SavelocSource.Replay ? $"replay of {SourceName}" : "unrun";

		string where = Run.IsBonusMode ? $"bonus {Run.Bonus}"
			: Run.IsStageMode || Run.Stage > 0 ? $"stage {Math.Max((short)1, Run.Stage)}"
			: Run.Checkpoint > 0 ? $"cp {Run.Checkpoint}"
			: "map";
		return $"{where} · {PlayerHud.FormatTime(Run.Ticks)}";
	}

	/// <summary>
	/// A pawn's state. run = the run to copy (the player's own or a spectated player's), null for unrun.
	/// </summary>
	internal static Saveloc? Capture(int id, Player owner, CCSPlayerPawn pawn, Player? run, SavelocSource source, string? sourceName, short courseBonus)
	{
		if (!pawn.IsValid || pawn.AbsOrigin == null)
			return null;

		var origin = pawn.AbsOrigin;
		var eyes = pawn.EyeAngles;
		var velocity = pawn.AbsVelocity;
		var movement = pawn.MovementServices?.As<CCSPlayer_MovementServices>();

		return new Saveloc
		{
			Id = id,
			OwnerSteamId = owner.Controller.SteamID,
			OwnerName = owner.Controller.PlayerName,
			Source = source,
			SourceName = sourceName,
			Position = new VectorT(origin.X, origin.Y, origin.Z),
			Angles = new QAngleT(eyes.X, eyes.Y, 0),
			Velocity = new VectorT(velocity.X, velocity.Y, velocity.Z),
			Ducked = movement?.Ducked ?? false,
			DuckAmount = movement?.DuckAmount ?? 0f,
			MoveType = pawn.MoveType == MoveType_t.MOVETYPE_LADDER ? MoveType_t.MOVETYPE_LADDER : MoveType_t.MOVETYPE_WALK,
			GravityScale = pawn.GravityScale,
			CourseBonus = courseBonus,
			Run = run != null && run.Timer.IsRunning ? SavelocRun.Capture(run) : null,
		};
	}

	/// <summary>Puts a pawn into this state (teleport, crouch, move type, gravity)</summary>
	internal void ApplyPawn(CCSPlayerPawn pawn)
	{
		pawn.TeleportWithView(Position, Angles, Velocity); // Full view, pawn kept level

		pawn.MoveType = MoveType;
		pawn.ActualMoveType = MoveType;
		Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
		pawn.GravityScale = GravityScale;

		var movement = pawn.MovementServices?.As<CCSPlayer_MovementServices>();
		if (movement != null && Ducked)
		{
			movement.DuckAmount = DuckAmount;
			movement.Ducked = true;
			movement.Ducking = false;
			movement.DesiresDuck = true;
			Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pMovementServices");
		}
	}
}

/// <summary>A run's state at a saveloc - timer, splits so far, sync and entry speeds</summary>
internal sealed class SavelocRun
{
	internal int Ticks { get; init; }
	internal short Style { get; init; }
	internal bool IsStageMode { get; init; }
	internal bool IsBonusMode { get; init; }
	internal short Stage { get; init; }
	internal short Checkpoint { get; init; }
	internal short Bonus { get; init; }
	internal VectorT StageEntryVelocity { get; init; }
	internal VectorT CheckpointEntryVelocity { get; init; }

	internal int RunTime { get; init; }
	internal VectorT StartVelocity { get; init; }
	internal List<CheckpointEntity> Splits { get; init; } = new();

	internal (int Good, int Total, int SegmentGood, int SegmentTotal) Sync { get; init; }
	internal VectorT? LastPrespeed { get; init; }

	private static CheckpointEntity Copy(CheckpointEntity c) =>
		new(c.CP, c.RunTime, c.StartVelX, c.StartVelY, c.StartVelZ, c.EndVelX, c.EndVelY, c.EndVelZ, c.EndTouch, c.Attempts);

	internal static SavelocRun Capture(Player player)
	{
		var timer = player.Timer;
		var run = player.Stats.ThisRun;
		return new SavelocRun
		{
			Ticks = timer.Ticks,
			Style = timer.Style,
			IsStageMode = timer.IsStageMode,
			IsBonusMode = timer.IsBonusMode,
			Stage = timer.Stage,
			Checkpoint = timer.Checkpoint,
			Bonus = timer.Bonus,
			StageEntryVelocity = new VectorT(timer.StageEntryVelX, timer.StageEntryVelY, timer.StageEntryVelZ),
			CheckpointEntryVelocity = new VectorT(timer.CheckpointEntryVelX, timer.CheckpointEntryVelY, timer.CheckpointEntryVelZ),
			RunTime = run.RunTime,
			StartVelocity = new VectorT(run.StartVelX, run.StartVelY, run.StartVelZ),
			Splits = run.Checkpoints.Values.Select(Copy).ToList(),
			Sync = player.SyncState,
			LastPrespeed = player.LastPrespeed,
		};
	}

	/// <summary>The run continues from here - in practice mode (nothing it finishes is saved)</summary>
	internal void Apply(Player player)
	{
		var timer = player.Timer;
		timer.Reset();
		timer.Ticks = Ticks;
		timer.Style = Style;
		timer.IsStageMode = IsStageMode;
		timer.IsBonusMode = IsBonusMode;
		timer.Stage = Stage;
		timer.Checkpoint = Checkpoint;
		timer.Bonus = Bonus;
		timer.StageEntryVelX = StageEntryVelocity.X;
		timer.StageEntryVelY = StageEntryVelocity.Y;
		timer.StageEntryVelZ = StageEntryVelocity.Z;
		timer.CheckpointEntryVelX = CheckpointEntryVelocity.X;
		timer.CheckpointEntryVelY = CheckpointEntryVelocity.Y;
		timer.CheckpointEntryVelZ = CheckpointEntryVelocity.Z;
		timer.IsPracticeMode = true;
		timer.Start();

		var run = player.Stats.ThisRun;
		run.RunTime = RunTime;
		run.StartVelX = StartVelocity.X;
		run.StartVelY = StartVelocity.Y;
		run.StartVelZ = StartVelocity.Z;
		run.Checkpoints.Clear();
		foreach (var split in Splits)
			run.Checkpoints[split.CP] = Copy(split);

		player.SyncState = Sync;
		player.LastPrespeed = LastPrespeed;
	}
}

/// <summary>A player's saveloc set: ordered saveloc ids and where they are in it</summary>
internal sealed class SavelocSet
{
	internal List<int> Ids { get; } = new();
	internal int Cursor { get; set; } = -1;

	internal int? Current => Cursor >= 0 && Cursor < Ids.Count ? Ids[Cursor] : null;
}

/// <summary>
/// Savelocs of one map session: ids count from 1 across all players, every player has a set (by SteamID, so
/// a reconnect keeps it). Saving drops the set's ids after the cursor and appends; loading someone else's id
/// copies that set. In memory only - a new session (and id) on every map start. Main thread only.
/// </summary>
internal sealed class SavelocSession
{
	internal Guid SessionId { get; } = Guid.NewGuid();
	internal Dictionary<int, Saveloc> All { get; } = new();
	private readonly Dictionary<ulong, SavelocSet> _sets = new();
	private int _nextId = 1;

	internal int NextId => _nextId;

	internal SavelocSet SetOf(ulong steamId)
	{
		if (!_sets.TryGetValue(steamId, out var set))
			_sets[steamId] = set = new SavelocSet();
		return set;
	}

	internal int TakeId() => _nextId++;

	/// <summary>Adds a new saveloc to its owner's set (the set's ids after the cursor are dropped from it)</summary>
	internal void Add(Saveloc saveloc)
	{
		var set = SetOf(saveloc.OwnerSteamId);
		if (set.Cursor + 1 < set.Ids.Count)
			set.Ids.RemoveRange(set.Cursor + 1, set.Ids.Count - set.Cursor - 1);
		set.Ids.Add(saveloc.Id);
		set.Cursor = set.Ids.Count - 1;
		saveloc.Path = new List<int>(set.Ids);
		All[saveloc.Id] = saveloc;
	}

	/// <summary>
	/// Makes saveloc id the player's current one: in their set the cursor moves there, otherwise they get a
	/// copy of the set it belongs to (its owner's current set if it still has it, else the set it was made
	/// in). False for an unknown id.
	/// </summary>
	internal bool Select(ulong steamId, int id)
	{
		if (!All.TryGetValue(id, out var saveloc))
			return false;

		var set = SetOf(steamId);
		int index = set.Ids.IndexOf(id);
		if (index < 0)
		{
			var source = _sets.TryGetValue(saveloc.OwnerSteamId, out var ownerSet) && ownerSet.Ids.Contains(id)
				? ownerSet.Ids
				: saveloc.Path;
			set.Ids.Clear();
			set.Ids.AddRange(source.Where(All.ContainsKey));
			index = set.Ids.IndexOf(id);
		}
		set.Cursor = index;
		return true;
	}
}
