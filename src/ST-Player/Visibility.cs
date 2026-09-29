using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Player visibility: everyone is always visible (maps that hide players are undone), and each player
/// can hide other players and / or replay bots for themselves only (!options - Visibility) by leaving
/// them out of what the server transmits to that client.
/// </summary>
public partial class SurfTimer
{
	/// <summary>A pawn that can be hidden from viewers, with the weapons it carries</summary>
	private readonly record struct TransmitTarget(uint PawnIndex, List<uint> WeaponIndexes, bool IsBot);

	// Rebuilt every tick while at least one player hides someone - CheckTransmit runs often and must
	// not walk entities itself
	private readonly List<TransmitTarget> _transmitTargets = new();
	private bool _anyoneHiding;

	/// <summary>
	/// Collects the alive human and replay bot pawns (OnTick) - only when someone uses a hide option.
	/// </summary>
	private void RefreshTransmitTargets()
	{
		_transmitTargets.Clear();
		_anyoneHiding = playerList.Values.Any(p => p.Options.HidePlayers || p.Options.HideBots);
		if (!_anyoneHiding)
			return;

		foreach (var player in playerList.Values)
		{
			if (player.Controller.IsValid && !player.Controller.IsBot)
				AddTransmitTarget(player.Controller, isBot: false);
		}

		foreach (var slot in CurrentMap?.ReplayManager?.Pool ?? [])
		{
			if (slot.Controller != null && slot.Controller.IsValid)
				AddTransmitTarget(slot.Controller, isBot: true);
		}
	}

	private void AddTransmitTarget(CCSPlayerController controller, bool isBot)
	{
		var pawn = controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid || !controller.PawnIsAlive)
			return;

		var weapons = new List<uint>();
		var carried = pawn.WeaponServices?.MyWeapons;
		if (carried != null)
		{
			foreach (var handle in carried)
			{
				var weapon = handle.Value;
				if (weapon != null && weapon.IsValid)
					weapons.Add(weapon.Index);
			}
		}
		_transmitTargets.Add(new TransmitTarget(pawn.Index, weapons, isBot));
	}

	/// <summary>
	/// Leaves hidden players / bots (and their weapons) out of what each viewer receives. The viewer's
	/// own pawn and whoever they spectate are always sent.
	/// </summary>
	private void OnCheckTransmit(CCheckTransmitInfoList infoList)
	{
		if (!_anyoneHiding || _transmitTargets.Count == 0)
			return;

		foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
		{
			if (viewer == null || !viewer.IsValid || viewer.IsBot || !playerList.TryGetValue(viewer.UserId ?? 0, out var player))
				continue;

			var options = player.Options;
			if (!options.HidePlayers && !options.HideBots)
				continue;

			uint ownPawn = viewer.PlayerPawn.Value?.Index ?? 0;
			uint observed = viewer.ObserverPawn.Value?.ObserverServices?.ObserverTarget.Value?.Index ?? 0;

			foreach (var target in _transmitTargets)
			{
				if (target.PawnIndex == ownPawn || target.PawnIndex == observed)
					continue;
				if (target.IsBot ? !options.HideBots : !options.HidePlayers)
					continue;

				info.TransmitEntities.Remove(target.PawnIndex);
				foreach (uint weapon in target.WeaponIndexes)
					info.TransmitEntities.Remove(weapon);
			}
		}
	}

	/// <summary>
	/// Undoes maps hiding players (render mode, alpha, EF_NODRAW) - every 0.5s. Humans keep their own
	/// hide-legs alpha, replay bots are fully visible.
	/// </summary>
	private void EnforcePlayerVisibility()
	{
		if (CurrentMap == null)
			return;

		try
		{
			foreach (var player in playerList.Values)
			{
				var controller = player.Controller;
				if (!controller.IsValid || controller.IsBot || !controller.PawnIsAlive)
					continue;

				var pawn = controller.PlayerPawn.Value;
				if (pawn != null && pawn.IsValid)
					Player.EnforceVisible(pawn, player.Options.HideLegs ? Player.LegsHiddenAlpha : 255);
			}

			foreach (var slot in CurrentMap.ReplayManager?.Pool ?? [])
			{
				var pawn = slot.Controller != null && slot.Controller.IsValid && slot.Controller.PawnIsAlive
					? slot.Controller.PlayerPawn.Value
					: null;
				if (pawn != null && pawn.IsValid)
					Player.EnforceVisible(pawn, 255);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[{ClassName}] Enforcing player visibility failed", nameof(SurfTimer));
		}
	}
}
