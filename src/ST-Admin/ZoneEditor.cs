using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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
	/// Puts an admin into the zone editor: timer stopped, moved to spectator (free roam), the map's zones
	/// copied into a draft and drawn for them. Returns an error when someone else is editing.
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
		if (controller.Team != CsTeam.Spectator)
			controller.ChangeTeam(CsTeam.Spectator);
		AddTimer(0.2f, () => SetFreeRoam(controller));

		RedrawEditorOutlines();
		_logger.LogInformation("[Zones] {Player} entered the zone editor on {Map}", controller.PlayerName, map.Name);
		return null;
	}

	/// <summary>Spectator free-cam (also switchable with the spectator keys)</summary>
	private static void SetFreeRoam(CCSPlayerController controller)
	{
		var observer = controller.IsValid ? controller.ObserverPawn.Value : null;
		var services = observer?.ObserverServices;
		if (observer == null || !observer.IsValid || services == null)
			return;

		services.ObserverMode = (byte)ObserverMode_t.OBS_MODE_ROAMING;
		services.ObserverTarget.Raw = uint.MaxValue;
		Utilities.SetStateChanged(observer, "CBasePlayerPawn", "m_pObserverServices");
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
					TeleportToZone(controller, ZoneType.MapStart, 1);
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

	/// <summary>Where the editor's spectator camera is (free roam) and where it looks</summary>
	private static (VectorT Position, QAngleT Angles)? EditorCamera(Player editor)
	{
		var observer = editor.Controller.ObserverPawn.Value;
		if (observer == null || !observer.IsValid || observer.AbsOrigin == null)
			return null;

		var angles = observer.V_angle;
		return (observer.AbsOrigin.ToVector_t(), new QAngleT(angles.X, angles.Y, 0));
	}

	/// <summary>Moves the editor's camera to a zone (its teleport point, else above its center)</summary>
	private static void MoveEditorCamera(Player editor, ZoneDefinition zone)
	{
		var observer = editor.Controller.ObserverPawn.Value;
		if (observer == null || !observer.IsValid)
			return;

		var size = zone.Size;
		var center = zone.Center;
		var position = zone.Teleport is VectorT teleport
			? new Vector(teleport.X, teleport.Y, teleport.Z + 64)
			: new Vector(center.X, center.Y - MathF.Max(size.Y, 128), center.Z + MathF.Max(size.Z, 64));
		var angles = zone.TeleportAngles is QAngleT a ? new QAngle(a.X, a.Y, 0) : new QAngle(20, 90, 0);
		observer.Teleport(position, angles, new Vector(0, 0, 0));
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
