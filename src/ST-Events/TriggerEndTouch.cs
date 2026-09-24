using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>
	/// Handler for trigger end touch hook - CBaseTrigger_EndTouchFunc.
	/// 
	/// Sometimes this gets triggered when a player joins the server (for the 2nd time) so we assign `client` to `null` to bypass the error.
	/// - T
	/// </summary>
	internal HookResult OnTriggerEndTouch(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
	{
		CBaseTrigger trigger = new CBaseTrigger(caller.Handle);
		CBaseEntity entity = new CBaseEntity(activator.Handle);
		CCSPlayerController client = null!;

		try
		{
			client = new CCSPlayerController(new CCSPlayerPawn(entity.Handle).Controller.Value!.Handle);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[{ClassName}] OnTriggerEndTouch -> Could not assign `client` (name: {Name}). Exception: {Exception}",
				nameof(SurfTimer), name, ex.Message
			);
		}

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
