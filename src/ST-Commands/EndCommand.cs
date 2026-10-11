using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

public partial class SurfTimer
{
	/// <summary>
	/// The course the player is on: where its run starts and the zone that ends it - the map (map start → map end), a
	/// bonus (bonus start → bonus end) or, in stage mode, a stage (its start → the next stage's start, the map end for
	/// the last one).
	/// </summary>
	private static (ZoneType StartType, short StartNumber, ZoneType EndType, short EndNumber, string Label) CourseEndsOf(Player p)
	{
		var map = CurrentMap;
		if (p.CourseBonus > 0)
			return (ZoneType.BonusStart, p.CourseBonus, ZoneType.BonusEnd, p.CourseBonus, $"bonus {p.CourseBonus}");

		if (p.Timer.IsStageMode && map.Stages > 0)
		{
			short stage = Math.Max((short)1, p.Timer.Stage);
			var start = stage == 1 ? ZoneType.MapStart : ZoneType.StageStart;
			short next = (short)(stage + 1);
			return map.HasZone(ZoneType.StageStart, next)
				? (start, stage, ZoneType.StageStart, next, $"stage {stage}")
				: (start, stage, ZoneType.MapEnd, (short)0, $"stage {stage}");
		}

		return (ZoneType.MapStart, 1, ZoneType.MapEnd, 0, "the map"); // Map end zones have number 0 (ZoneName.cs)
	}

	[ConsoleCommand("css_end", "Teleport to the end of your course - from its start zone")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void PlayerGoToEnd(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var p) || CurrentMap == null)
			return;

		var course = CourseEndsOf(p);
		if (!player.PawnIsAlive || !p.IsTouchingZone(course.StartType, course.StartNumber))
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["end_not_in_start", course.Label]}");
			return;
		}

		if (!CurrentMap.Zones.TryGetValue((course.EndType, course.EndNumber), out var ends) || ends.Count == 0)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["end_no_end", course.Label]}");
			return;
		}

		if (ends.Count == 1)
		{
			TeleportToEnd(p, ends[0]);
			return;
		}

		// Several end zones: the player picks one
		var origin = player.PlayerPawn.Value?.AbsOrigin?.ToVector_t();
		var items = ends.OrderBy(z => z.ZoneId).Select((zone, i) =>
		{
			string distance = origin is VectorT from ? $"{(zone.Teleport - from).Length():0} u away" : "";
			return new HudMenuItem($"End {i + 1}", _ => TeleportToEnd(p, zone), zone.Name, () => distance);
		}).ToList();
		MenuPresenter.Show(p, new HudMenu($"Ends · {course.Label}", [new HudMenuTab("Ends", items)]));
	}

	/// <summary>
	/// Puts the player into an end zone of their course - still from its start zone (the menu may be answered later). The
	/// timer is stopped and landing there isn't entering it, so nothing starts or finishes.
	/// </summary>
	private void TeleportToEnd(Player p, ZoneInfo end)
	{
		var controller = p.Controller;
		var course = CourseEndsOf(p);
		if (!controller.IsValid || !controller.PawnIsAlive || !p.IsTouchingZone(course.StartType, course.StartNumber))
		{
			if (controller.IsValid)
				controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["end_not_in_start", course.Label]}");
			return;
		}

		var pawn = controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;

		ClaimPlacement(p);
		p.Timer.Reset();
		p.Stats.ThisRun.Checkpoints.Clear();

		if (end.Angles is QAngleT view)
			pawn.TeleportWithView(end.Teleport, view, new VectorT(0, 0, 0));
		else
			Extensions.Teleport(pawn, end.Teleport, null, new VectorT(0, 0, 0));

		// Landing inside a zone isn't entering it - now, and next frame in case the position settles a frame later
		ResyncZoneTouches(p);
		Server.NextFrame(() => ResyncZoneTouches(p));
	}
}
