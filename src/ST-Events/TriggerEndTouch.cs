using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>
	/// Handler for trigger end touch hook - CBaseTrigger_EndTouchFunc. Also fires for pawns without a
	/// controller (e.g. a bot being kicked, a player rejoining) - those are ignored.
	/// </summary>
	internal HookResult OnTriggerEndTouch(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
	{
		CBaseTrigger trigger = new CBaseTrigger(caller.Handle);
		CCSPlayerController? client = ControllerOfActivator(activator);

		if (client == null || !client.IsValid || client.UserId == -1 || !playerList.TryGetValue(client.UserId ?? 0, out Player? player))
			return HookResult.Continue;

		if (!ZoneInfo.TryFromTrigger(trigger, out ZoneInfo zone))
			return HookResult.Continue; // Not a timer zone (filters, teleports, ...)

		// Always forget the trigger, even for a dead player, so no stale "inside" state is left behind
		player.TouchingTriggers.Remove(zone.TriggerIndex);

		// `client.IsBot` throws error in server console when going to spectator
		if (!client.PawnIsAlive)
			return HookResult.Continue;

		// Still inside another trigger of the same zone (overlapping duplicates) - not a real exit yet
		if (player.IsTouchingZone(zone.Type, zone.Number))
			return HookResult.Continue;

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_EndTouchFunc -> {trigger.DesignerName} -> {zone.Name}");
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

		return HookResult.Continue;
	}
}
