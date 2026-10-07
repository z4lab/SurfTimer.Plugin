using CounterStrikeSharp.API.Core;

namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>
	/// The player behind a trigger touch - null for anything that isn't a player pawn with a controller
	/// (props, projectiles, or a bot's pawn while the bot is being kicked).
	/// </summary>
	private static CCSPlayerController? ControllerOfActivator(CEntityInstance? activator)
	{
		if (activator == null || !activator.IsValid || activator.DesignerName != "player")
			return null;

		var controller = new CCSPlayerPawn(activator.Handle).Controller.Value;
		return controller != null && controller.IsValid ? new CCSPlayerController(controller.Handle) : null;
	}

	/// <summary>The trigger-linked zone a map trigger belongs to (by name and origin) - null for other triggers</summary>
	private static ZoneInfo? LinkedZoneOf(CEntityInstance caller)
	{
		var map = CurrentMap;
		if (map == null || map.TriggerZones.Count == 0 || !caller.IsValid)
			return null;
		var trigger = new CBaseTrigger(caller.Handle);
		var origin = trigger.AbsOrigin;
		return origin == null ? null : map.TriggerZoneOf(trigger.Entity?.Name, origin.ToVector_t());
	}

	/// <summary>A map trigger of a trigger-linked zone was entered - its exact shape decides</summary>
	internal HookResult OnZoneTriggerStartTouch(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
	{
		var zone = LinkedZoneOf(caller);
		var client = zone == null ? null : ControllerOfActivator(activator);
		if (zone == null || client == null || !client.PawnIsAlive || !playerList.TryGetValue(client.UserId ?? 0, out var player))
			return HookResult.Continue;

		bool editing = _zoneEditor != null && ReferenceEquals(_zoneEditor.Editor, player);
		if (!editing && player.IsOnCourse(zone) && !player.TouchingTriggers.ContainsKey(zone.ZoneId))
			HandleZoneEnter(player, zone);
		return HookResult.Continue;
	}

	/// <summary>A map trigger of a trigger-linked zone was left</summary>
	internal HookResult OnZoneTriggerEndTouch(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
	{
		var zone = LinkedZoneOf(caller);
		var client = zone == null ? null : ControllerOfActivator(activator);
		if (zone == null || client == null || !playerList.TryGetValue(client.UserId ?? 0, out var player)
			|| !player.TouchingTriggers.ContainsKey(zone.ZoneId))
			return HookResult.Continue;

		if (client.PawnIsAlive)
			HandleZoneLeave(player, zone);
		else
			player.TouchingTriggers.Remove(zone.ZoneId); // Dead - forgotten without exit handlers
		return HookResult.Continue;
	}
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
