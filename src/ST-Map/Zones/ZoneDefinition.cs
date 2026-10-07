namespace SurfTimer;

/// <summary>Where a stored zone came from</summary>
internal enum ZoneSource : byte
{
	/// <summary>Imported from the map's trigger_multiple entities</summary>
	Map = 0,
	/// <summary>Created or changed in the zone editor</summary>
	Editor = 1,
}

/// <summary>How a zone's shape is defined (zones.shape - stored, never renumber)</summary>
internal enum ZoneShape : byte
{
	/// <summary>Axis-aligned box (Mins / Maxs)</summary>
	Box = 0,
	/// <summary>Polygon footprint (Points, each with its own height) extended straight up by Height</summary>
	Prism = 1,
	/// <summary>
	/// Linked to a map trigger_multiple (TriggerName / TriggerOrigin) - detected by the trigger's own touch
	/// events, so its exact brush shape counts. Points / Height hold its rotated bounds for drawing.
	/// </summary>
	Trigger = 2,
}

/// <summary>
/// A zone as stored in the database (zones) and edited in the zone editor. Type and Number are raw -
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

	internal ZoneShape Shape { get; set; }

	/// <summary>Prism / trigger footprint in order (each point with its own Z) - empty for boxes</summary>
	internal List<VectorT> Points { get; set; } = new();

	/// <summary>Prism / trigger height above every footprint point</summary>
	internal float Height { get; set; }

	/// <summary>Trigger zones: the map trigger's name and origin (re-created triggers are found by these)</summary>
	internal string? TriggerName { get; set; }
	internal VectorT? TriggerOrigin { get; set; }

	internal const float DefaultPrismHeight = 64f;

	/// <summary>The footprint of any shape - a box's four bottom corners</summary>
	internal List<VectorT> Footprint => Shape == ZoneShape.Box
		?
		[
			new VectorT(Mins.X, Mins.Y, Mins.Z), new VectorT(Maxs.X, Mins.Y, Mins.Z),
			new VectorT(Maxs.X, Maxs.Y, Mins.Z), new VectorT(Mins.X, Maxs.Y, Mins.Z),
		]
		: Points;

	/// <summary>The height of any shape above its footprint</summary>
	internal float ShapeHeight => Shape == ZoneShape.Box ? Maxs.Z - Mins.Z : Height;

	/// <summary>Bounds (Mins / Maxs) of a prism / trigger from its points and height</summary>
	internal void UpdateBounds()
	{
		if (Shape == ZoneShape.Box || Points.Count == 0)
			return;
		Mins = new VectorT(Points.Min(p => p.X), Points.Min(p => p.Y), Points.Min(p => p.Z));
		Maxs = new VectorT(Points.Max(p => p.X), Points.Max(p => p.Y), Points.Max(p => p.Z) + Height);
	}

	/// <summary>
	/// Makes the zone an editable prism with its current footprint and height (a box or a trigger link becomes a
	/// custom shape - the map trigger isn't used for it any more).
	/// </summary>
	internal void ToPrism()
	{
		if (Shape == ZoneShape.Prism)
			return;
		var footprint = Footprint.ToList();
		float height = ShapeHeight;
		Shape = ZoneShape.Prism;
		Points = footprint;
		Height = height > 0 ? height : DefaultPrismHeight;
		TriggerName = null;
		TriggerOrigin = null;
		UpdateBounds();
	}

	/// <summary>Moves the whole zone (and its teleport point)</summary>
	internal void Translate(VectorT offset)
	{
		Mins += offset;
		Maxs += offset;
		for (int i = 0; i < Points.Count; i++)
			Points[i] += offset;
		if (Teleport is VectorT teleport)
			Teleport = teleport + offset;
	}

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

	internal ZoneDefinition Clone()
	{
		var copy = (ZoneDefinition)MemberwiseClone();
		copy.Points = new List<VectorT>(Points);
		return copy;
	}

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
