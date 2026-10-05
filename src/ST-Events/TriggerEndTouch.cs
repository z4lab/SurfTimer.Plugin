namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>
	/// An alive player's hull stopped overlapping a zone box (ZoneTracker.cs) - what a trigger's EndTouch
	/// used to do.
	/// </summary>
	private void HandleZoneLeave(Player player, ZoneInfo zone)
	{
		player.TouchingTriggers.Remove(zone.ZoneId);

		// Still inside another box of the same zone (overlapping duplicates) - not a real exit yet
		if (player.IsTouchingZone(zone.Type, zone.Number))
			return;

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> ZoneLeave -> {zone.Type} {zone.Number} ({zone.Name})");
#endif

		switch (zone.Type)
		{
			case ZoneType.MapEnd:
				EndTouchHandleMapEndZone(player);
				break;
			case ZoneType.MapStart:
				EndTouchHandleMapStartZone(player, zone);
				break;
			case ZoneType.StageStart:
				EndTouchHandleStageStartZone(player, zone);
				break;
			case ZoneType.Checkpoint:
				EndTouchHandleCheckpointZone(player, zone);
				break;
			case ZoneType.BonusStart:
				EndTouchHandleBonusStartZone(player, zone);
				break;
			case ZoneType.BonusEnd:
				EndTouchHandleBonusEndZone(player);
				break;
		}
	}
}
