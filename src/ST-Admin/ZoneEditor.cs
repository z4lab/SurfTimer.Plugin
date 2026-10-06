using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// The zone editor's state: a working copy (draft) of the map's zone definitions that is applied to the map
/// live with every change, the selected zone and the step. Save writes the draft to the database, Discard
/// goes back to the saved zones. One editor per map at a time. Main thread only.
/// </summary>
internal sealed class ZoneEditorSession(Player editor, List<ZoneDefinition> draft, CsTeam previousTeam)
{
	internal static readonly float[] Steps = [1f, 4f, 16f, 64f];

	internal Player Editor { get; } = editor;
	internal List<ZoneDefinition> Draft { get; set; } = draft;
	internal int? SelectedKey { get; set; }
	internal int StepIndex { get; set; } = 2;
	internal float Step => Steps[Math.Clamp(StepIndex, 0, Steps.Length - 1)];

	/// <summary>Changes since the last save / discard</summary>
	internal int Changes { get; set; }

	/// <summary>The team the admin was in before entering - they go back to it when leaving</summary>
	internal CsTeam PreviousTeam { get; } = previousTeam;

	/// <summary>"Set corner 1 here" - the point corner 2 spans the zone to</summary>
	internal VectorT? Corner1 { get; set; }

	/// <summary>Aim mode: shots set the selected zone's corners where they hit (1, 2, 1, ...)</summary>
	internal bool AimMode { get; set; }

	/// <summary>Aim mode: the corner the next shot sets (0 = corner 1, 1 = corner 2)</summary>
	internal int AimCorner { get; set; }

	/// <summary>Aim mode: the tick of the last corner set - one per shot (penetration fires several impacts)</summary>
	internal int LastAimTick { get; set; }

	/// <summary>Where the editor was last tick - a bigger jump than noclip flies is a map teleport, undone</summary>
	internal VectorT? LastPosition { get; set; }

	/// <summary>Until this tick the editor's own teleports (menu) aren't undone</summary>
	internal int OwnTeleportUntilTick { get; set; }

	internal ZoneDefinition? Selected => SelectedKey is int key ? Draft.FirstOrDefault(z => z.Key == key) : null;
}

public partial class SurfTimer
{
	private ZoneEditorSession? _zoneEditor;

	/// <summary>Whoever edits the zones right now - null when nobody does (or they left)</summary>
	private ZoneEditorSession? ActiveZoneEditor
	{
		get
		{
			if (_zoneEditor != null && !_zoneEditor.Editor.Controller.IsValid)
				EndZoneEditor(restorePlayer: false); // Disconnected - the unsaved draft goes, the saved zones apply
			return _zoneEditor;
		}
	}

	/// <summary>
	/// Puts an admin into the zone editor: timer stopped, alive in noclip (map teleports undone, zones don't
	/// fire for them), the map's zones copied into a draft and drawn for them. Returns an error when someone
	/// else is editing.
	/// </summary>
	internal string? EnterZoneEditor(Player player)
	{
		var map = CurrentMap;
		if (map == null)
			return "No map loaded";

		var editor = ActiveZoneEditor;
		if (editor != null)
			return ReferenceEquals(editor.Editor, player) ? null : $"{editor.Editor.Controller.PlayerName} is editing the zones";

		var controller = player.Controller;
		var team = controller.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist ? controller.Team : CsTeam.CounterTerrorist;
		_zoneEditor = new ZoneEditorSession(player, map.ZoneDefinitions.Select(z => z.Clone()).ToList(), team);

		player.Timer.Reset();
		player.Stats.ThisRun.Checkpoints.Clear();
		player.ReplayRecorder.Stop();
		player.TouchingTriggers.Clear();

		// Alive in noclip - from spectator / dead the admin joins their team first (TickZoneEditor sets noclip)
		if (controller.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
			controller.ChangeTeam(team);
		if (!controller.PawnIsAlive)
			AddTimer(0.2f, () =>
			{
				if (controller.IsValid && !controller.PawnIsAlive)
					controller.Respawn();
			});

		RedrawEditorOutlines();
		_logger.LogInformation("[Zones] {Player} entered the zone editor on {Map}", controller.PlayerName, map.Name);
		return null;
	}

	// More than noclip flies in one tick (sv_noclipspeed 5 = ~1250 u/s = ~20 units per tick)
	private const float EditorTeleportJump = 200f;

	/// <summary>
	/// Every tick while someone edits: keeps them in noclip (maps reset move types) and undoes teleports the
	/// map does to them (trigger_teleport) - a jump further than noclip can fly that the menu didn't make.
	/// </summary>
	private void TickZoneEditor()
	{
		var editor = ActiveZoneEditor;
		var controller = editor?.Editor.Controller;
		var pawn = controller != null && controller.PawnIsAlive ? controller.PlayerPawn.Value : null;
		if (editor == null || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
		{
			if (editor != null)
				editor.LastPosition = null;
			return;
		}

		if (pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP)
			SetMoveType(pawn, MoveType_t.MOVETYPE_NOCLIP);

		var position = pawn.AbsOrigin.ToVector_t();
		if (editor.LastPosition is VectorT last && Server.TickCount > editor.OwnTeleportUntilTick
			&& (position - last).Length() > EditorTeleportJump)
		{
			Extensions.Teleport(pawn, last, null, new VectorT(0, 0, 0));
			return; // LastPosition stays where the editor was
		}
		editor.LastPosition = position;
	}

	private static void SetMoveType(CBasePlayerPawn pawn, MoveType_t moveType)
	{
		pawn.MoveType = moveType;
		pawn.ActualMoveType = moveType;
		Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
	}

	/// <summary>A teleport the editor asked for - not undone as a map teleport</summary>
	private static void EditorTeleport(ZoneEditorSession editor, VectorT position, QAngleT? angles)
	{
		var pawn = editor.Editor.Controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;
		editor.OwnTeleportUntilTick = Server.TickCount + 4;
		editor.LastPosition = position;
		if (angles is QAngleT view)
			pawn.TeleportWithView(position, view, new VectorT(0, 0, 0));
		else
			Extensions.Teleport(pawn, position, null, new VectorT(0, 0, 0));
	}

	/// <summary>
	/// Aim mode: a shot of the editor sets the selected zone's next corner where the bullet hit (bullet_impact,
	/// the first impact of a shot - penetration fires more).
	/// </summary>
	[GameEventHandler]
	public HookResult OnEditorBulletImpact(EventBulletImpact @event, GameEventInfo info)
	{
		var editor = ActiveZoneEditor;
		var shooter = @event.Userid;
		if (editor == null || !editor.AimMode || shooter == null || !shooter.IsValid
			|| shooter.UserId != editor.Editor.Controller.UserId || editor.LastAimTick == Server.TickCount)
			return HookResult.Continue;

		var zone = editor.Selected;
		if (zone == null)
		{
			shooter.PrintToChat($"{Config.PluginPrefix} Select a zone in the editor first - shots set its corners");
			return HookResult.Continue;
		}

		editor.LastAimTick = Server.TickCount;
		var point = new VectorT(@event.X, @event.Y, @event.Z);
		if (editor.AimCorner == 0)
		{
			editor.Corner1 = point;
			zone.SetCorners(point, FarthestCorner(zone, point));
		}
		else
		{
			zone.SetCorners(editor.Corner1 ?? FarthestCorner(zone, point), point);
		}
		shooter.PrintToChat($"{Config.PluginPrefix} {zone.Label}: corner {editor.AimCorner + 1} at {point.X:0} {point.Y:0} {point.Z:0}");
		editor.AimCorner = 1 - editor.AimCorner;
		ZoneDraftChanged(zone);
		if (editor.Editor.Admin is { } session)
			PanelRefresh(session);
		return HookResult.Continue;
	}

	/// <summary>
	/// Leaves the editor (unsaved changes are dropped - the panel asks before). The admin goes back to their
	/// team at the map start.
	/// </summary>
	internal void EndZoneEditor(bool restorePlayer = true)
	{
		var editor = _zoneEditor;
		if (editor == null)
			return;

		_zoneEditor = null;
		ClearEditorOutlines();

		// The live draft goes - the saved zones are the map's zones again
		if (editor.Changes > 0 && CurrentMap != null)
		{
			CurrentMap.ActivateZones(CurrentMap.ZoneDefinitions);
			OnZonesChanged();
		}

		var controller = editor.Editor.Controller;
		if (!restorePlayer || !controller.IsValid)
			return;

		if (controller.PlayerPawn.Value is { IsValid: true } pawn)
			SetMoveType(pawn, MoveType_t.MOVETYPE_WALK);
		if (controller.Team != editor.PreviousTeam)
			controller.ChangeTeam(editor.PreviousTeam);
		AddTimer(0.2f, () =>
		{
			if (!controller.IsValid)
				return;
			if (!controller.PawnIsAlive)
				controller.Respawn();
			AddTimer(0.2f, () =>
			{
				if (controller.IsValid && controller.PawnIsAlive)
				{
					editor.Editor.CourseBonus = 0;
					TeleportToZone(controller, ZoneType.MapStart, 1);
				}
			});
		});
	}

	/// <summary>A draft change: applied to the map live, the zone drawn again</summary>
	private void ZoneDraftChanged(ZoneDefinition? zone)
	{
		var editor = _zoneEditor;
		var map = CurrentMap;
		if (editor == null || map == null)
			return;

		editor.Changes++;
		if (zone != null)
			zone.Source = ZoneSource.Editor;
		map.ActivateZones(editor.Draft);
		OnZonesChanged();
		if (zone != null && editor.Draft.Contains(zone))
			RedrawEditorOutline(zone);
	}

	/// <summary>The active zones changed - players' inside state and the public outlines follow</summary>
	internal void OnZonesChanged()
	{
		ResyncZoneTouches();
		MarkZoneOutlinesDirty();
	}

	private void SelectZone(ZoneDefinition? zone)
	{
		var editor = _zoneEditor;
		if (editor == null)
			return;

		var previous = editor.Selected;
		editor.SelectedKey = zone?.Key;
		if (previous != null)
			RedrawEditorOutline(previous);
		if (zone != null)
			RedrawEditorOutline(zone);
	}

	/// <summary>Where the editor stands (feet) and looks</summary>
	private static (VectorT Position, QAngleT Angles)? EditorCamera(Player editor)
	{
		var pawn = editor.Controller.PawnIsAlive ? editor.Controller.PlayerPawn.Value : null;
		if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
			return null;

		var angles = pawn.EyeAngles;
		return (pawn.AbsOrigin.ToVector_t(), new QAngleT(angles.X, angles.Y, 0));
	}

	/// <summary>Moves the editor to a zone (its teleport point, else in front of and above its center)</summary>
	private void MoveEditorCamera(Player editor, ZoneDefinition zone)
	{
		var session = _zoneEditor;
		if (session == null)
			return;

		var size = zone.Size;
		var center = zone.Center;
		var position = zone.Teleport is VectorT teleport
			? teleport
			: new VectorT(center.X, center.Y - MathF.Max(size.Y, 128), center.Z + MathF.Max(size.Z, 64));
		var angles = zone.TeleportAngles is QAngleT a ? new QAngleT(a.X, a.Y, 0) : new QAngleT(20, 90, 0);
		EditorTeleport(session, position, angles);
	}

	/// <summary>
	/// Saves the draft: written to the database (replacing the map's zones), it becomes the map's zones.
	/// then gets whether the stage / bonus / checkpoint counts changed (a map restart applies them).
	/// </summary>
	private void SaveZoneDraft(PanelContext ctx, Action<bool> then)
	{
		var editor = _zoneEditor;
		var map = CurrentMap;
		if (editor == null || map == null || map.ID <= 0)
		{
			ctx.Done("Nothing to save");
			return;
		}

		var draft = editor.Draft.Select(z => z.Clone()).ToList();
		int mapId = map.ID;
		int before = map.ZoneDefinitions.Count;
		ctx.Run("Saving zones…", async () =>
		{
			await ZoneRepository.ReplaceAsync(mapId, draft);
			return $"Saved {draft.Count} zones";
		}, status =>
		{
			if (status.StartsWith("Failed") || CurrentMap != map)
			{
				PanelRefresh(ctx.Session);
				return;
			}

			// The saved copies (with their new ids) are the map's zones; the draft keeps editing them
			map.SetZoneDefinitions(draft);
			if (_zoneEditor != null)
			{
				_zoneEditor.Draft = draft.Select(z => z.Clone()).ToList();
				_zoneEditor.Changes = 0;
			}
			OnZonesChanged();
			ctx.Audit("zones saved", "map", mapId, $"{map.Name}: {before} -> {draft.Count} zones");
			then(map.ChangesCounts(draft));
		});
	}

	/// <summary>Back to the saved zones (the draft's changes are dropped)</summary>
	private void DiscardZoneDraft()
	{
		var editor = _zoneEditor;
		var map = CurrentMap;
		if (editor == null || map == null)
			return;

		editor.Draft = map.ZoneDefinitions.Select(z => z.Clone()).ToList();
		editor.Changes = 0;
		if (editor.Selected == null)
			editor.SelectedKey = null;
		map.ActivateZones(map.ZoneDefinitions);
		OnZonesChanged();
		RedrawEditorOutlines();
	}

	/// <summary>
	/// Reload from map: the map's own trigger zones replace the stored ones. then gets whether the counts
	/// changed.
	/// </summary>
	private void ReloadZonesFromMap(PanelContext ctx, Action<bool> then)
	{
		var map = CurrentMap;
		if (map == null || map.ID <= 0)
		{
			ctx.Done("No map loaded");
			return;
		}

		var zones = ZoneImport.FromTriggers();
		int mapId = map.ID;
		ctx.Run("Reloading zones from the map…", async () =>
		{
			await ZoneRepository.ReplaceAsync(mapId, zones);
			return $"{zones.Count} zones loaded from the map";
		}, status =>
		{
			if (status.StartsWith("Failed") || CurrentMap != map)
			{
				PanelRefresh(ctx.Session);
				return;
			}

			map.SetZoneDefinitions(zones);
			if (_zoneEditor != null)
			{
				_zoneEditor.Draft = zones.Select(z => z.Clone()).ToList();
				_zoneEditor.Changes = 0;
				_zoneEditor.SelectedKey = null;
				RedrawEditorOutlines();
			}
			OnZonesChanged();
			ctx.Audit("zones reloaded from map", "map", mapId, $"{map.Name}: {zones.Count} zones");
			_logger.LogInformation("[Zones] {Map}: {Count} zones reloaded from the map's triggers", map.Name, zones.Count);
			then(map.ChangesCounts(zones));
		});
	}
}
