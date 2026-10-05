namespace SurfTimer;

/// <summary>Where a stored zone came from</summary>
internal enum ZoneSource : byte
{
	/// <summary>Imported from the map's trigger_multiple entities</summary>
	Map = 0,
	/// <summary>Created or changed in the zone editor</summary>
	Editor = 1,
}

/// <summary>
/// A zone box as stored in the database (zones) and edited in the zone editor. Type and Number are raw -
/// as the map names them; the stages-as-checkpoints remap happens when the zones are activated
/// (Map.ActivateZones). Mins / Maxs are world-space corners, kept ordered (Mins &lt;= Maxs).
/// </summary>
internal sealed class ZoneDefinition
{
	private static int _nextKey;

	/// <summary>Database id - 0 for a zone that isn't saved yet</summary>
	internal int Id { get; set; }

	/// <summary>
	/// Runtime identity (ZoneInfo.ZoneId) - kept by Clone, so a zone being edited / saved stays the same zone
	/// for players standing in it. Not stored.
	/// </summary>
	internal int Key { get; private set; } = Interlocked.Increment(ref _nextKey);
	internal ZoneType Type { get; set; }
	internal short Number { get; set; }
	internal string Name { get; set; } = "";
	internal VectorT Mins { get; set; }
	internal VectorT Maxs { get; set; }
	/// <summary>Where players are placed in this zone - null = the box's bottom center</summary>
	internal VectorT? Teleport { get; set; }
	internal QAngleT? TeleportAngles { get; set; }
	/// <summary>Speed cap zones: the cap (u/s)</summary>
	internal float? Value { get; set; }
	internal ZoneSource Source { get; set; }

	internal const float DefaultSpeedCap = 350f;

	/// <summary>Zone types that have a number (stage, checkpoint, bonus)</summary>
	internal static bool IsNumbered(ZoneType type) => type is ZoneType.StageStart or ZoneType.Checkpoint
		or ZoneType.BonusStart or ZoneType.BonusEnd;

	internal VectorT Center => new((Mins.X + Maxs.X) / 2, (Mins.Y + Maxs.Y) / 2, (Mins.Z + Maxs.Z) / 2);

	internal VectorT Size => new(Maxs.X - Mins.X, Maxs.Y - Mins.Y, Maxs.Z - Mins.Z);

	/// <summary>The teleport target, or the box's bottom center (a little above the floor)</summary>
	internal VectorT TeleportOrCenter => Teleport ?? new VectorT((Mins.X + Maxs.X) / 2, (Mins.Y + Maxs.Y) / 2, Mins.Z + 1);

	/// <summary>Sets both corners from any two points (any order)</summary>
	internal void SetCorners(VectorT a, VectorT b)
	{
		Mins = new VectorT(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));
		Maxs = new VectorT(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
	}

	internal ZoneDefinition Clone() => (ZoneDefinition)MemberwiseClone();

	/// <summary>A copy that is a new zone (own key, not saved yet)</summary>
	internal ZoneDefinition Duplicate()
	{
		var copy = Clone();
		copy.Id = 0;
		copy.Key = Interlocked.Increment(ref _nextKey);
		copy.Source = ZoneSource.Editor;
		return copy;
	}

	/// <summary>"Stage 3 start", "Bonus 1 end", "Speed cap 350"</summary>
	internal string Label => Describe(Type, Number, Value);

	internal static string Describe(ZoneType type, short number, float? value = null) => type switch
	{
		ZoneType.MapStart => "Map start",
		ZoneType.MapEnd => "Map end",
		ZoneType.StageStart => $"Stage {number} start",
		ZoneType.Checkpoint => $"Checkpoint {number}",
		ZoneType.BonusStart => $"Bonus {number} start",
		ZoneType.BonusEnd => $"Bonus {number} end",
		ZoneType.Stop => "Stop zone",
		ZoneType.TeleportBack => "Teleport back",
		ZoneType.SpeedCap => $"Speed cap {value ?? DefaultSpeedCap:0}",
		_ => "Zone",
	};
}
