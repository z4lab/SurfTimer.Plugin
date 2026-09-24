using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>
	/// Handler for trigger start touch hook - CBaseTrigger_StartTouchFunc
	/// </summary>
	internal HookResult OnTriggerStartTouch(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
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
			_logger.LogError(ex, "[{ClassName}] OnTriggerStartTouch -> Could not assign `client` (name: {Name}). Exception: {Exception}",
				nameof(SurfTimer), name, ex.Message
			);
		}

		if (client == null || !client.IsValid || !client.PawnIsAlive || !playerList.ContainsKey((int)client.UserId!)) // !playerList.ContainsKey((int)client.UserId!) make sure to not check for user_id that doesnt exists
		{
			return HookResult.Continue;
		}
		// To-do: Sometimes this triggers before `OnPlayerConnect` and `playerList` does not contain the player how is this possible :thonk:
		if (!playerList.ContainsKey(client.UserId ?? 0))
		{
			_logger.LogCritical("[{ClassName}] OnTriggerStartTouch -> Player playerList does NOT contain client.UserId, this shouldn't happen. Player: {PlayerName} ({UserId})",
				nameof(SurfTimer), client.PlayerName, client.UserId
			);

			Exception exception = new($"[{nameof(SurfTimer)}] OnTriggerStartTouch -> Init -> Player playerList does NOT contain client.UserId, this shouldn't happen. Player: {client.PlayerName} ({client.UserId})");
			throw exception;
		}
		// Implement Trigger Start Touch Here
		Player player = playerList[client.UserId ?? 0];

#if DEBUG
		player.Controller.PrintToChat($"CS2 Surf DEBUG >> CBaseTrigger_StartTouchFunc -> {trigger.DesignerName} -> {trigger.Entity!.Name}");
#endif

		if (DB == null)
		{
			_logger.LogCritical("[{ClassName}] OnTriggerStartTouch -> DB object is null, this shouldn't happen.",
				nameof(SurfTimer)
			);

			Exception exception = new Exception($"[{nameof(SurfTimer)}] OnTriggerStartTouch -> DB object is null, this shouldn't happen.");
			throw exception;
		}

		// Classified by name on every touch, not looked up by entity index: round restarts re-create the
		// map's trigger entities with new indexes, which would leave an index-keyed lookup stale.
		if (!ZoneInfo.TryFromTrigger(trigger, out ZoneInfo zone))
			return HookResult.Continue; // Not a timer zone (filters, teleports, ...)

		// Every entry counts, including a second trigger of the same zone
		player.TouchingTriggers[zone.TriggerIndex] = zone;

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
		}
		return HookResult.Continue;
	}
}

