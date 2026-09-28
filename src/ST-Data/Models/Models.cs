namespace SurfTimer;

/// <summary>
/// Kinds of courses (course_kinds table). Every map has a map course (number 0) plus one per stage,
/// bonus and linear checkpoint segment.
/// </summary>
internal enum CourseKind : byte
{
	Map = 1,
	Stage = 2,
	Bonus = 3,
	Checkpoint = 4,
}

internal static class CourseKinds
{
	/// <summary>
	/// The plugin's run type (0 map, 1 bonus, 2 stage, 3 checkpoint segment) as a course kind.
	/// </summary>
	internal static CourseKind FromRunType(int type) => type switch
	{
		1 => CourseKind.Bonus,
		2 => CourseKind.Stage,
		3 => CourseKind.Checkpoint,
		_ => CourseKind.Map,
	};

	/// <summary>
	/// A course kind as the plugin's run type (0 map, 1 bonus, 2 stage, 3 checkpoint segment).
	/// </summary>
	internal static short ToRunType(CourseKind kind) => kind switch
	{
		CourseKind.Bonus => 1,
		CourseKind.Stage => 2,
		CourseKind.Checkpoint => 3,
		_ => 0,
	};
}

/// <summary>
/// A time's values as the plugin uses them. Type / Stage are the plugin's run type and number
/// (see CourseKinds); in the database a time references its course.
/// </summary>
public abstract class RunStatsEntity
{
	public int ID { get; set; } = -1;
	public short Type { get; set; } // 0 = Map, 1 = Bonus, 2 = Stage, 3 = Checkpoint segment
	public int RunTime { get; set; }
	public short Stage { get; set; } // Bonus number when Type == 1, checkpoint segment when Type == 3
	public short Style { get; set; }
	public string? Name { get; set; }
	public float StartVelX { get; set; }
	public float StartVelY { get; set; }
	public float StartVelZ { get; set; }
	public float EndVelX { get; set; }
	public float EndVelY { get; set; }
	public float EndVelZ { get; set; }
	public int RunDate { get; set; } // Unix seconds (UTC)
	public float? Sync { get; set; }
}

/// <summary>
/// A stored time (times table) with its rank.
/// </summary>
public class MapTimeRunDataEntity : RunStatsEntity
{
	public int PlayerID { get; set; }
	public int MapID { get; set; }
	public int CourseId { get; set; }
	public int? ReplayId { get; set; }
	public int Rank { get; set; }
	public int TotalCount { get; set; }
}

/// <summary>
/// A split of a run: a stage / checkpoint zone entered (time_splits table).
/// </summary>
public class CheckpointEntity
{
	public int MapTimeID { get; set; }
	public short CP { get; set; }
	public int RunTime { get; set; } // Ticks when the zone was entered
	public int EndTouch { get; set; } // Ticks when it was left
	public int Attempts { get; set; }
	public float StartVelX { get; set; }
	public float StartVelY { get; set; }
	public float StartVelZ { get; set; }
	public float EndVelX { get; set; }
	public float EndVelY { get; set; }
	public float EndVelZ { get; set; }

	public CheckpointEntity() { }

	public CheckpointEntity(short cp, int ticks, float startVelX, float startVelY, float startVelZ,
		float endVelX, float endVelY, float endVelZ, int endTouch, int attempts)
	{
		CP = cp;
		RunTime = ticks;
		StartVelX = startVelX;
		StartVelY = startVelY;
		StartVelZ = startVelZ;
		EndVelX = endVelX;
		EndVelY = endVelY;
		EndVelZ = endVelZ;
		EndTouch = endTouch;
		Attempts = attempts;
	}
}

/// <summary>
/// A map's stored information (maps, map_authors, map_settings and its map course's tier).
/// </summary>
public class MapEntity
{
	public int ID { get; set; }
	public string? Name { get; set; }
	public string? Author { get; set; } // All authors, comma separated
	public short Tier { get; set; }
	public short Stages { get; set; }
	public short Bonuses { get; set; }
	public bool Ranked { get; set; }
	/// <summary>
	/// Staged map where stage starts (except stage 1) allow bhopping without the start-zone speed cap
	/// (map setting "staged_linear").
	/// </summary>
	public bool StagedLinear { get; set; }
	public int DateAdded { get; set; } // Unix seconds (UTC)
	public int LastPlayed { get; set; } // Unix seconds (UTC)
}

/// <summary>
/// A player's stored information (players table, visits from player_sessions).
/// </summary>
public class PlayerProfileEntity
{
	public int ID { get; set; }
	public string? Name { get; set; }
	public ulong SteamID { get; set; }
	public string? Country { get; set; }
	public int JoinDate { get; set; } // Unix seconds (UTC)
	public int LastSeen { get; set; } // Unix seconds (UTC)
	public int Connections { get; set; } // Sessions
}
