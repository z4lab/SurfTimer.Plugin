using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// !knife / !pistol - get the default knife or pistol back (dropped weapons are removed, see Weapons.cs).
/// </summary>
public partial class SurfTimer
{
	[ConsoleCommand("css_knife", "Get your knife back")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void GiveKnife(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !CanReceiveWeapon(player))
			return;

		if (WeaponInSlot(player, gear_slot_t.GEAR_SLOT_KNIFE) != null)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["weapon_have_knife"]}");
			return;
		}

		player.GiveNamedItem("weapon_knife");
		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["weapon_given_knife"]}");
	}

	[ConsoleCommand("css_pistol", "Get your team's default pistol back")]
	[ConsoleCommand("css_gun", "Get your team's default pistol back")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void GivePistol(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null)
			GivePistolItem(player, DefaultPistol(player.Team));
	}

	[ConsoleCommand("css_usp", "Get a USP-S")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void GiveUsp(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null)
			GivePistolItem(player, "weapon_usp_silencer");
	}

	[ConsoleCommand("css_p2000", "Get a P2000")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void GiveP2000(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null)
			GivePistolItem(player, "weapon_hkp2000");
	}

	[ConsoleCommand("css_glock", "Get a Glock-18")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void GiveGlock(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null)
			GivePistolItem(player, "weapon_glock");
	}

	/// <summary>
	/// Gives a pistol - only when the player is alive and doesn't carry one already.
	/// </summary>
	private static void GivePistolItem(CCSPlayerController player, string weapon)
	{
		if (!CanReceiveWeapon(player))
			return;

		if (WeaponInSlot(player, gear_slot_t.GEAR_SLOT_PISTOL) != null)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["weapon_have_pistol"]}");
			return;
		}

		player.GiveNamedItem(weapon);
		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["weapon_given_pistol"]}");
	}

	/// <summary>Only alive players on a team get weapons</summary>
	private static bool CanReceiveWeapon(CCSPlayerController player)
	{
		if (player.PawnIsAlive && player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
			return true;

		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["weapon_not_alive"]}");
		return false;
	}

	/// <summary>
	/// The server's default secondary for the team (mp_ct/t_default_secondary), USP-S / Glock if unset.
	/// </summary>
	private static string DefaultPistol(CsTeam team)
	{
		bool ct = team == CsTeam.CounterTerrorist;
		string value = ConVar.Find(ct ? "mp_ct_default_secondary" : "mp_t_default_secondary")?.StringValue?.Trim() ?? "";
		return value.StartsWith("weapon_") ? value : ct ? "weapon_usp_silencer" : "weapon_glock";
	}
}
