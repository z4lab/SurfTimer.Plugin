using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// Zone outlines: every box drawn as 12 beams (its edges). Two sets -
/// - public: the active zones, for players with !options - Visibility - Show zones (only exists while
///   someone has it on);
/// - editor: the zone editor's draft, only for the admin editing (selected zone white, inactive dim).
/// Beams are never moved after they spawn (clients don't redraw moved beams reliably) - a changed zone gets
/// new ones. Who sees which beams is filtered in CheckTransmit (Visibility.cs). Main thread only.
/// </summary>
public partial class SurfTimer
{
	private sealed class ZoneOutline
	{
		internal readonly List<CBeam> Beams = new();
		/// <summary>Map / stage / bonus start or end - shown with "start &amp; end"</summary>
		internal bool StartEnd;
	}

	private readonly Dictionary<int, ZoneOutline> _publicOutlines = new();
	private readonly Dictionary<int, ZoneOutline> _editorOutlines = new();
	private bool _publicOutlinesDirty = true;

	// Beam entity indexes per set - what the transmit filter removes
	private readonly HashSet<uint> _publicStartEndBeams = new();
	private readonly HashSet<uint> _publicOtherBeams = new();
	private readonly HashSet<uint> _editorBeams = new();

	private const float ZoneBeamWidth = 1.5f;
	private const float SelectedZoneBeamWidth = 3f;

	private bool ZoneBeamsExist => _publicStartEndBeams.Count > 0 || _publicOtherBeams.Count > 0 || _editorBeams.Count > 0;

	/// <summary>The active zones changed - the public outlines are drawn again</summary>
	internal void MarkZoneOutlinesDirty() => _publicOutlinesDirty = true;

	private static bool IsStartOrEnd(ZoneType type) => type is ZoneType.MapStart or ZoneType.MapEnd or ZoneType.StageStart
		or ZoneType.BonusStart or ZoneType.BonusEnd;

	private static Color ZoneColor(ZoneType type) => type switch
	{
		ZoneType.MapStart or ZoneType.StageStart or ZoneType.BonusStart => Color.FromArgb(255, 60, 220, 90),
		ZoneType.MapEnd or ZoneType.BonusEnd => Color.FromArgb(255, 230, 60, 60),
		ZoneType.Checkpoint => Color.FromArgb(255, 240, 210, 60),
		ZoneType.Stop => Color.FromArgb(255, 150, 150, 150),
		ZoneType.TeleportBack => Color.FromArgb(255, 170, 90, 230),
		ZoneType.SpeedCap => Color.FromArgb(255, 245, 150, 40),
		_ => Color.White,
	};

	/// <summary>
	/// Twice a second (OnTick): public outlines exist while someone wants them and follow zone changes; beams
	/// lost to a round restart are drawn again.
	/// </summary>
	private void TickZoneOutlines()
	{
		var map = CurrentMap;
		bool wanted = map != null && playerList.Values.Any(p => p.Controller.IsValid && p.Options.ZonesShow != ZoneDisplay.Off);

		if (!wanted)
		{
			if (_publicOutlines.Count > 0)
				ClearOutlines(_publicOutlines, _publicStartEndBeams, _publicOtherBeams);
		}
		else if (_publicOutlinesDirty || AnyBeamLost(_publicOutlines))
		{
			ClearOutlines(_publicOutlines, _publicStartEndBeams, _publicOtherBeams);
			foreach (var zone in map!.ActiveZones)
			{
				bool startEnd = IsStartOrEnd(zone.Type);
				var outline = DrawOutline(zone.Mins, zone.Maxs, ZoneColor(zone.Type), ZoneBeamWidth);
				outline.StartEnd = startEnd;
				_publicOutlines[zone.ZoneId] = outline;
				foreach (var beam in outline.Beams)
					(startEnd ? _publicStartEndBeams : _publicOtherBeams).Add(beam.Index);
			}
			_publicOutlinesDirty = false;
		}

		// ActiveZoneEditor ends the editor of an admin who disconnected
		if (ActiveZoneEditor != null && AnyBeamLost(_editorOutlines))
			RedrawEditorOutlines();
	}

	/// <summary>Every zone of the editor's draft drawn again</summary>
	private void RedrawEditorOutlines()
	{
		ClearOutlines(_editorOutlines, _editorBeams, null);
		if (_zoneEditor == null)
			return;
		foreach (var zone in _zoneEditor.Draft)
			RedrawEditorOutline(zone);
	}

	/// <summary>One zone of the editor's draft drawn again (after it changed / got (de)selected)</summary>
	private void RedrawEditorOutline(ZoneDefinition zone)
	{
		RemoveEditorOutline(zone.Key);
		if (_zoneEditor == null || CurrentMap == null)
			return;

		bool selected = _zoneEditor.SelectedKey == zone.Key;
		var color = ZoneColor(zone.Type);
		if (selected)
			color = Color.White;
		else if (!IsZoneActiveInMap(zone))
			color = Color.FromArgb(255, color.R / 3, color.G / 3, color.B / 3); // Inactive until the map loads again

		var outline = DrawOutline(zone.Mins, zone.Maxs, color, selected ? SelectedZoneBeamWidth : ZoneBeamWidth);
		_editorOutlines[zone.Key] = outline;
		foreach (var beam in outline.Beams)
			_editorBeams.Add(beam.Index);
	}

	private void RemoveEditorOutline(int key)
	{
		if (!_editorOutlines.Remove(key, out var outline))
			return;
		foreach (var beam in outline.Beams)
		{
			if (beam.IsValid)
			{
				_editorBeams.Remove(beam.Index);
				beam.Remove();
			}
		}
	}

	private void ClearEditorOutlines() => ClearOutlines(_editorOutlines, _editorBeams, null);

	/// <summary>Whether a draft zone is (would be) active on the loaded map - within its stage / bonus / CP counts</summary>
	private bool IsZoneActiveInMap(ZoneDefinition zone)
	{
		var type = zone.Type;
		short number = zone.Number;
		return CurrentMap != null && ZoneName.Remap(ref type, ref number) && CurrentMap.IsWithinLoadedCounts(type, number);
	}

	private static ZoneOutline DrawOutline(VectorT mins, VectorT maxs, Color color, float width)
	{
		var outline = new ZoneOutline();
		var c = new Vector[8];
		for (int i = 0; i < 8; i++)
		{
			c[i] = new Vector((i & 1) == 0 ? mins.X : maxs.X, (i & 2) == 0 ? mins.Y : maxs.Y, (i & 4) == 0 ? mins.Z : maxs.Z);
		}

		// Corner bits: 1 = x, 2 = y, 4 = z - an edge joins corners that differ in one bit
		int[,] edges =
		{
			{ 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, // along x
			{ 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, // along y
			{ 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }, // along z
		};
		for (int e = 0; e < 12; e++)
		{
			var beam = CreateBeam(c[edges[e, 0]], c[edges[e, 1]], color, width);
			if (beam != null)
				outline.Beams.Add(beam);
		}
		return outline;
	}

	private static bool AnyBeamLost(Dictionary<int, ZoneOutline> outlines)
	{
		foreach (var outline in outlines.Values)
		{
			foreach (var beam in outline.Beams)
			{
				if (!beam.IsValid)
					return true;
			}
		}
		return false;
	}

	private static void ClearOutlines(Dictionary<int, ZoneOutline> outlines, HashSet<uint> indexes, HashSet<uint>? moreIndexes)
	{
		foreach (var outline in outlines.Values)
		{
			foreach (var beam in outline.Beams)
			{
				if (beam.IsValid)
					beam.Remove();
			}
		}
		outlines.Clear();
		indexes.Clear();
		moreIndexes?.Clear();
	}

	/// <summary>Map end - the beams go with the map, only the bookkeeping is dropped</summary>
	private void ForgetZoneOutlines()
	{
		_publicOutlines.Clear();
		_editorOutlines.Clear();
		_publicStartEndBeams.Clear();
		_publicOtherBeams.Clear();
		_editorBeams.Clear();
		_publicOutlinesDirty = true;
	}

	/// <summary>
	/// Leaves out the zone beams a viewer doesn't get: the editor's set for everyone but the editor (who
	/// doesn't get the public set), public start / end beams with Show zones off, the rest unless "all".
	/// </summary>
	private void FilterZoneTransmit(CCheckTransmitInfo info, Player viewer)
	{
		bool editing = _zoneEditor != null && ReferenceEquals(_zoneEditor.Editor, viewer);
		var show = viewer.Options.ZonesShow;

		if (editing || show != ZoneDisplay.All)
		{
			foreach (uint index in _publicOtherBeams)
				info.TransmitEntities.Remove(index);
		}
		if (editing || show == ZoneDisplay.Off)
		{
			foreach (uint index in _publicStartEndBeams)
				info.TransmitEntities.Remove(index);
		}
		if (!editing)
		{
			foreach (uint index in _editorBeams)
				info.TransmitEntities.Remove(index);
		}
	}
}
