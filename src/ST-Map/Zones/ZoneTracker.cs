namespace SurfTimer;

/// <summary>
/// Tick-based zone detection: every tick each alive player's hull (origin + collision box, so crouching
/// counts) is checked against the map's active zone boxes. Boxes it stopped overlapping are left first (a
/// teleport from the end to the start leaves the end before it enters the start), then new ones entered -
/// the same handlers the map's triggers used to drive. Main thread only.
/// </summary>
public partial class SurfTimer
{
	// Reused every tick - no allocations per player
	private readonly List<ZoneInfo> _zonesLeft = new();
	private readonly List<ZoneInfo> _zonesEntered = new();

	private void TickZones(Player player)
	{
		var map = CurrentMap;
		var controller = player.Controller;
		var pawn = controller.PlayerPawn.Value;
		if (map == null || !controller.PawnIsAlive || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
		{
			// Dead / spectating: forgotten without exit handlers (as a trigger's EndTouch did for dead players)
			player.TouchingTriggers.Clear();
			return;
		}

		if (!HullOf(pawn, out var mins, out var maxs))
			return;

		_zonesLeft.Clear();
		_zonesEntered.Clear();

		foreach (var (id, zone) in player.TouchingTriggers)
		{
			if (!map.ActiveZoneById.TryGetValue(id, out var current) || !current.Overlaps(mins, maxs))
				_zonesLeft.Add(zone);
		}

		foreach (var zone in map.ActiveZones)
		{
			if (!player.TouchingTriggers.ContainsKey(zone.ZoneId) && zone.Overlaps(mins, maxs))
				_zonesEntered.Add(zone);
		}

		foreach (var zone in _zonesLeft)
			HandleZoneLeave(player, zone);

		foreach (var zone in _zonesEntered)
		{
			// An earlier handler may have teleported the player (repeat mode, teleport-back zone)
			if (!controller.PawnIsAlive)
				break;
			HandleZoneEnter(player, zone);
		}

		TickSpeedCapZones(player, pawn);
	}

	/// <summary>
	/// After the active zones changed (zone editor, reload from map): every player's "inside" state is
	/// recomputed without firing enter / leave handlers - only player movement starts or stops anything.
	/// </summary>
	internal void ResyncZoneTouches()
	{
		var map = CurrentMap;
		if (map == null)
			return;

		foreach (var player in playerList.Values)
		{
			player.TouchingTriggers.Clear();
			var controller = player.Controller;
			var pawn = controller.IsValid && controller.PawnIsAlive ? controller.PlayerPawn.Value : null;
			if (pawn == null || !pawn.IsValid || !HullOf(pawn, out var mins, out var maxs))
				continue;

			foreach (var zone in map.ActiveZones)
			{
				if (zone.Overlaps(mins, maxs))
					player.TouchingTriggers[zone.ZoneId] = zone;
			}
		}
	}

	private static bool HullOf(CounterStrikeSharp.API.Core.CCSPlayerPawn pawn, out VectorT mins, out VectorT maxs)
	{
		var origin = pawn.AbsOrigin;
		var collision = pawn.Collision;
		if (origin == null || collision == null)
		{
			mins = maxs = default;
			return false;
		}

		mins = new VectorT(origin.X + collision.Mins.X, origin.Y + collision.Mins.Y, origin.Z + collision.Mins.Z);
		maxs = new VectorT(origin.X + collision.Maxs.X, origin.Y + collision.Maxs.Y, origin.Z + collision.Maxs.Z);
		return true;
	}

	/// <summary>
	/// Speed cap zones: horizontal speed is capped to the lowest cap of the speed cap zones the player is in.
	/// </summary>
	private static void TickSpeedCapZones(Player player, CounterStrikeSharp.API.Core.CCSPlayerPawn pawn)
	{
		float cap = float.MaxValue;
		foreach (var zone in player.TouchingTriggers.Values)
		{
			if (zone.Type == ZoneType.SpeedCap)
				cap = MathF.Min(cap, zone.Value ?? ZoneDefinition.DefaultSpeedCap);
		}
		if (cap == float.MaxValue)
			return;

		var velocity = pawn.AbsVelocity.ToVector_t();
		player.ClampExitSpeed(cap, ref velocity, out _);
	}
}
