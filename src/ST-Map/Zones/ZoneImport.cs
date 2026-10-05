using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// Reads the zones the map itself defines: its trigger_multiple entities named after ZoneName's rules,
/// with teleport targets from info_teleport_destination / spawn_ entities. Used on a map's first load
/// (no zones stored yet) and by "Reload from map". Main thread (reads entities).
/// </summary>
internal static class ZoneImport
{
	/// <summary>
	/// Every zone trigger of the loaded map as a definition - raw type / number (no stages-as-checkpoints
	/// remap), bounds = trigger origin + collision mins / maxs.
	/// </summary>
	internal static List<ZoneDefinition> FromTriggers()
	{
		var zones = new List<ZoneDefinition>();
		var destinations = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_teleport_destination").ToList();

		foreach (var trigger in Utilities.FindAllEntitiesByDesignerName<CBaseTrigger>("trigger_multiple"))
		{
			string? name = trigger.Entity?.Name;
			if (!ZoneName.TryParse(name, out ZoneType type, out short number) || trigger.AbsOrigin == null)
				continue;

			var origin = trigger.AbsOrigin;
			var mins = trigger.Collision.Mins;
			var maxs = trigger.Collision.Maxs;
			var zone = new ZoneDefinition
			{
				Type = type,
				Number = number,
				Name = name!,
				Source = ZoneSource.Map,
			};
			zone.SetCorners(new VectorT(origin.X + mins.X, origin.Y + mins.Y, origin.Z + mins.Z),
				new VectorT(origin.X + maxs.X, origin.Y + maxs.Y, origin.Z + maxs.Z));

			var (teleport, angles) = FindTeleportTarget(zone, origin.ToVector_t(), destinations);
			zone.Teleport = teleport;
			zone.TeleportAngles = angles;
			zones.Add(zone);
		}

		return zones;
	}

	/// <summary>
	/// Where to put a player teleporting into a trigger: an info_teleport_destination inside it, else the
	/// matching named spawn (spawn_map_start, spawn_s2_start, ...) closest to it, else the trigger's origin.
	/// </summary>
	private static (VectorT Position, QAngleT? Angles) FindTeleportTarget(ZoneDefinition zone, VectorT origin, List<CBaseEntity> destinations)
	{
		var inside = destinations.FirstOrDefault(d => d.AbsOrigin != null && Contains(zone, d.AbsOrigin.ToVector_t()));
		var chosen = inside ?? destinations
			.Where(d => d.AbsOrigin != null
				&& ZoneName.TryParseSpawn(d.Entity?.Name, out var spawnType, out var spawnNumber)
				&& spawnType == zone.Type && spawnNumber == zone.Number)
			.OrderBy(d => (d.AbsOrigin!.ToVector_t() - origin).Length())
			.FirstOrDefault();

		if (chosen == null)
			return (origin, null);

		return (chosen.AbsOrigin!.ToVector_t(),
			new QAngleT(chosen.AbsRotation!.X, chosen.AbsRotation!.Y, chosen.AbsRotation!.Z));
	}

	private static bool Contains(ZoneDefinition zone, VectorT point) =>
		point.X >= zone.Mins.X && point.X <= zone.Maxs.X
		&& point.Y >= zone.Mins.Y && point.Y <= zone.Maxs.Y
		&& point.Z >= zone.Mins.Z && point.Z <= zone.Maxs.Z;
}
