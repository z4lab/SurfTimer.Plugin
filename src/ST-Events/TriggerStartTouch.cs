namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>
	/// An alive player's hull started overlapping a zone box (ZoneTracker.cs) - what a trigger's StartTouch
	/// used to do. Every box counts, including a second box of the same zone.
	/// </summary>
	private void HandleZoneEnter(Player player, ZoneInfo zone)
	{
		player.TouchingTriggers[zone.ZoneId] = zone;

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> ZoneEnter -> {zone.Type} {zone.Number} ({zone.Name})");
#endif

		switch (zone.Type)
		{
			case ZoneType.MapEnd:
				StartTouchHandleMapEndZone(player);
				break;
			case ZoneType.MapStart:
				StartTouchHandleMapStartZone(player, zone);
				break;
			case ZoneType.StageStart:
				StartTouchHandleStageStartZone(player, zone);
				break;
			case ZoneType.Checkpoint:
				StartTouchHandleCheckpointZone(player, zone);
				break;
			case ZoneType.BonusStart:
				StartTouchHandleBonusStartZone(player, zone);
				break;
			case ZoneType.BonusEnd:
				StartTouchHandleBonusEndZone(player, zone);
				break;
			case ZoneType.Stop:
				StartTouchHandleStopZone(player);
				break;
			case ZoneType.TeleportBack:
				StartTouchHandleTeleportBackZone(player);
				break;
		}
	}
}
