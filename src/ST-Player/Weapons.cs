using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// Weapons: anything a player drops (drop key, death drops) is removed at once, so nothing lies around to
/// be picked up. Weapons the map places stay until someone has held and dropped them.
/// </summary>
public partial class SurfTimer
{
	// Entity indexes of weapons that have been in a player's hands - only those are removed when they
	// lose their parent (map-placed weapons have no parent from the start). Main thread only.
	private readonly HashSet<uint> _heldWeapons = new();

	/// <summary>
	/// Weapons get a player pawn as parent when picked up / given and lose it when dropped. A held weapon
	/// that lost its parent is checked again next frame - still without an owner, it's on the ground and
	/// removed.
	/// </summary>
	private void OnWeaponParentChanged(CEntityInstance entity, CEntityInstance newParent)
	{
		if (!entity.IsValid || !entity.DesignerName.StartsWith("weapon_"))
			return;

		uint index = entity.Index;
		if (newParent != null && newParent.IsValid)
		{
			if (newParent.DesignerName == "player")
				_heldWeapons.Add(index);
			return;
		}

		if (!_heldWeapons.Contains(index))
			return; // Never held - e.g. a map-placed weapon

		Server.NextFrame(() =>
		{
			var weapon = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)index);
			if (weapon == null || !weapon.IsValid || !weapon.DesignerName.StartsWith("weapon_"))
			{
				_heldWeapons.Remove(index);
				return;
			}
			if (weapon.OwnerEntity.IsValid)
				return; // Picked up / given again meanwhile

			_heldWeapons.Remove(index);
			weapon.Remove();
		});
	}

	/// <summary>Entity indexes start over with every map</summary>
	private void ClearHeldWeapons() => _heldWeapons.Clear();

	/// <summary>
	/// The player's weapon in a gear slot (knife, pistol, ...), or null.
	/// </summary>
	internal static CBasePlayerWeapon? WeaponInSlot(CCSPlayerController player, gear_slot_t slot)
	{
		var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
		if (weapons == null)
			return null;

		foreach (var handle in weapons)
		{
			var weapon = handle.Value;
			if (weapon == null || !weapon.IsValid)
				continue;
			if (weapon.As<CCSWeaponBase>().VData?.GearSlot == slot)
				return weapon;
		}
		return null;
	}
}
