using System.Globalization;
using System.Text.Json;

namespace SurfTimer;

/// <summary>
/// zones - a map's zone boxes (see ZoneDefinition). Imported from the map on its first load, then
/// replaced as a whole by the zone editor / "reload from map".
/// </summary>
internal static class ZoneRepository
{
	private sealed class ZoneRow
	{
		public int Id { get; set; }
		public byte TypeId { get; set; }
		public short Number { get; set; }
		public string Name { get; set; } = "";
		public float MinX { get; set; }
		public float MinY { get; set; }
		public float MinZ { get; set; }
		public float MaxX { get; set; }
		public float MaxY { get; set; }
		public float MaxZ { get; set; }
		public float? TpX { get; set; }
		public float? TpY { get; set; }
		public float? TpZ { get; set; }
		public float? TpPitch { get; set; }
		public float? TpYaw { get; set; }
		public float? Value { get; set; }
		public byte Source { get; set; }
		public DateTime UpdatedAt { get; set; }
		public byte Shape { get; set; }
		public string? Points { get; set; }
		public float? Height { get; set; }
		public string? TriggerName { get; set; }
		public float? TriggerOriginX { get; set; }
		public float? TriggerOriginY { get; set; }
		public float? TriggerOriginZ { get; set; }

		internal ZoneDefinition ToDefinition() => new()
		{
			Id = Id,
			Type = Enum.IsDefined(typeof(ZoneType), TypeId) ? (ZoneType)TypeId : ZoneType.Unknown,
			Number = Number,
			Name = Name,
			Mins = new VectorT(MinX, MinY, MinZ),
			Maxs = new VectorT(MaxX, MaxY, MaxZ),
			Teleport = TpX is float x && TpY is float y && TpZ is float z ? new VectorT(x, y, z) : null,
			TeleportAngles = TpPitch is float pitch && TpYaw is float yaw ? new QAngleT(pitch, yaw, 0) : null,
			Value = Value,
			Source = (ZoneSource)Source,
			Shape = Enum.IsDefined(typeof(ZoneShape), Shape) && (Shape == 0 || ParsePoints(Points).Count >= 3) ? (ZoneShape)Shape : ZoneShape.Box,
			Points = Shape == 0 ? new List<VectorT>() : ParsePoints(Points),
			Height = Height ?? 0,
			TriggerName = TriggerName,
			TriggerOrigin = TriggerOriginX is float ox && TriggerOriginY is float oy && TriggerOriginZ is float oz ? new VectorT(ox, oy, oz) : null,
		};
	}

	private const string SelectZone = @"
		SELECT z.`id`, z.`zone_type` AS TypeId, z.`number`, z.`name`, z.`min_x`, z.`min_y`, z.`min_z`, z.`max_x`, z.`max_y`, z.`max_z`,
			z.`tp_x`, z.`tp_y`, z.`tp_z`, z.`tp_pitch`, z.`tp_yaw`, z.`value`, z.`source`, z.`updated_at`,
			z.`shape`, z.`points`, z.`height`, z.`trigger_name`, z.`trigger_origin_x`, z.`trigger_origin_y`, z.`trigger_origin_z`
		FROM `{p}zones` z";

	/// <summary>"[[x,y,z],...]" -> points (empty when missing / unreadable)</summary>
	private static List<VectorT> ParsePoints(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return new List<VectorT>();
		try
		{
			var raw = JsonSerializer.Deserialize<float[][]>(json);
			return raw?.Where(p => p.Length >= 3).Select(p => new VectorT(p[0], p[1], p[2])).ToList() ?? new List<VectorT>();
		}
		catch (JsonException)
		{
			return new List<VectorT>();
		}
	}

	private static string? FormatPoints(ZoneDefinition zone) => zone.Shape == ZoneShape.Box ? null
		: "[" + string.Join(",", zone.Points.Select(p => string.Create(CultureInfo.InvariantCulture, $"[{p.X:0.###},{p.Y:0.###},{p.Z:0.###}]"))) + "]";

	/// <summary>
	/// A map's zones by its name - read on map start, before the map's row is loaded. Empty for a map
	/// that has none stored (yet).
	/// </summary>
	internal static async Task<List<ZoneDefinition>> GetByMapNameAsync(string mapName) =>
		(await SurfTimer.DB.QueryAsync<ZoneRow>(SelectZone + @"
			JOIN `{p}maps` m ON m.`id` = z.`map_id` WHERE m.`name` = @Name ORDER BY z.`id`", new { Name = mapName }))
		.Select(r => r.ToDefinition())
		.Where(d => d.Type != ZoneType.Unknown)
		.ToList();

	internal sealed class Summary
	{
		public long Zones { get; set; }
		public DateTime? ImportedAt { get; set; }
		public DateTime? EditedAt { get; set; }
	}

	/// <summary>How many zones a map has stored, when they were imported and last edited</summary>
	internal static async Task<Summary> GetSummaryAsync(int mapId) =>
		await SurfTimer.DB.QueryFirstOrDefaultAsync<Summary>(@"
			SELECT COUNT(*) AS Zones, MIN(`created_at`) AS ImportedAt,
				MAX(CASE WHEN `source` = 1 THEN `updated_at` END) AS EditedAt
			FROM `{p}zones` WHERE `map_id` = @MapId", new { MapId = mapId }) ?? new Summary();

	/// <summary>
	/// Replaces all of a map's zones (one transaction) and gives the definitions their new ids.
	/// </summary>
	internal static Task ReplaceAsync(int mapId, IReadOnlyList<ZoneDefinition> zones) =>
		SurfTimer.DB.InTransactionAsync(async tx =>
		{
			await tx.ExecuteAsync("DELETE FROM `{p}zones` WHERE `map_id` = @MapId", new { MapId = mapId });

			foreach (var zone in zones)
			{
				zone.UpdateBounds();
				await tx.ExecuteAsync(@"
					INSERT INTO `{p}zones` (`map_id`, `zone_type`, `number`, `shape`, `name`, `min_x`, `min_y`, `min_z`, `max_x`, `max_y`, `max_z`,
						`points`, `height`, `trigger_name`, `trigger_origin_x`, `trigger_origin_y`, `trigger_origin_z`,
						`tp_x`, `tp_y`, `tp_z`, `tp_pitch`, `tp_yaw`, `value`, `source`, `created_at`, `updated_at`)
					VALUES (@MapId, @Type, @Number, @Shape, @Name, @MinX, @MinY, @MinZ, @MaxX, @MaxY, @MaxZ,
						@Points, @Height, @TriggerName, @TriggerOriginX, @TriggerOriginY, @TriggerOriginZ,
						@TpX, @TpY, @TpZ, @TpPitch, @TpYaw, @Value, @Source, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))",
					new
					{
						MapId = mapId,
						Type = (byte)zone.Type,
						zone.Number,
						Shape = (byte)zone.Shape,
						Name = zone.Name.Length > 64 ? zone.Name[..64] : zone.Name,
						Points = FormatPoints(zone),
						Height = zone.Shape == ZoneShape.Box ? (float?)null : zone.Height,
						TriggerName = zone.TriggerName is { Length: > 64 } n ? n[..64] : zone.TriggerName,
						TriggerOriginX = zone.TriggerOrigin?.X, TriggerOriginY = zone.TriggerOrigin?.Y, TriggerOriginZ = zone.TriggerOrigin?.Z,
						MinX = zone.Mins.X, MinY = zone.Mins.Y, MinZ = zone.Mins.Z,
						MaxX = zone.Maxs.X, MaxY = zone.Maxs.Y, MaxZ = zone.Maxs.Z,
						TpX = zone.Teleport?.X, TpY = zone.Teleport?.Y, TpZ = zone.Teleport?.Z,
						TpPitch = zone.TeleportAngles?.X, TpYaw = zone.TeleportAngles?.Y,
						zone.Value,
						Source = (byte)zone.Source,
					});
				zone.Id = (int)await tx.ExecuteScalarAsync<ulong>("SELECT LAST_INSERT_ID()");
			}
		});
}
