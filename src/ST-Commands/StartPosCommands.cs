using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// !startpos / !clearstartpos - a player's own spot (and view direction) in a start zone, used by every
/// reset into that zone (!r, !rs, !s, !b, repeat mode). Kept in memory for the map only.
/// </summary>
public partial class SurfTimer
{
	private const float StartPosMaxSpeed = 5f;

	[ConsoleCommand("css_startpos", "Save where !r / !rs put you in this start zone")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void SetStartPos(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var oPlayer))
			return;

		var pawn = player.PlayerPawn.Value;
		if (!player.PawnIsAlive || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["startpos_not_alive"]}");
			return;
		}

		var zone = StartZoneOf(oPlayer);
		if (zone == null)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["startpos_not_in_zone"]}");
			return;
		}

		// Standing still on the ground, not crouched - a crouched spot under a low ceiling would trap
		// the player once they stand up after the teleport
		bool onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
		bool crouched = (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0;
		float speed = pawn.AbsVelocity.ToVector_t().velMag();
		if (!onGround || crouched || speed > StartPosMaxSpeed)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["startpos_not_still"]}");
			return;
		}

		var angles = pawn.EyeAngles;
		oPlayer.StartPositions[(zone.Type, zone.Number)] = (pawn.AbsOrigin.ToVector_t(), new QAngleT(angles.X, angles.Y, 0));
		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["startpos_saved", StartZoneLabel(zone.Type, zone.Number)]}");
	}

	[ConsoleCommand("css_clearstartpos", "Remove your start position of this start zone ('all' removes every one)")]
	[CommandHelper(usage: "[all]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void ClearStartPos(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var oPlayer))
			return;

		if (command.GetArg(1).Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			oPlayer.StartPositions.Clear();
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["startpos_cleared_all"]}");
			return;
		}

		var zone = StartZoneOf(oPlayer);
		if (zone == null || !oPlayer.StartPositions.Remove((zone.Type, zone.Number)))
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["startpos_none", oPlayer.StartPositions.Count]}");
			return;
		}

		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["startpos_cleared", StartZoneLabel(zone.Type, zone.Number)]}");
	}

	/// <summary>The start zone (map / stage / bonus start) the player is standing in, or null</summary>
	private static ZoneInfo? StartZoneOf(Player player) =>
		player.TouchingTriggers.Values.FirstOrDefault(zone => zone.Type is ZoneType.MapStart or ZoneType.StageStart or ZoneType.BonusStart);

	private static string StartZoneLabel(ZoneType type, short number) => type switch
	{
		ZoneType.MapStart => "the map start",
		ZoneType.StageStart => $"stage {number}",
		ZoneType.BonusStart => $"bonus {number}",
		_ => $"zone {number}",
	};
}
