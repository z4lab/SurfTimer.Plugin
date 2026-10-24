using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using CsVector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace SurfTimer;

/// <summary>Labels and default colors of the map overlay's kinds, and which trigger is which</summary>
internal static class OverlayKinds
{
	internal static readonly OverlayKind[] All = Enum.GetValues<OverlayKind>();
	internal static readonly OverlayKind[] Triggers = All.Where(k => k != OverlayKind.Clip).ToArray();

	internal static string Label(OverlayKind kind) => kind switch
	{
		OverlayKind.Teleport => "Teleport triggers",
		OverlayKind.Push => "Push triggers",
		OverlayKind.Multiple => "Multiple triggers",
		OverlayKind.Hurt => "Hurt triggers",
		OverlayKind.Other => "Other triggers",
		_ => "Player clips",
	};

	internal static string Sub(OverlayKind kind) => kind switch
	{
		OverlayKind.Teleport => "trigger_teleport - where the map teleports you",
		OverlayKind.Push => "trigger_push - boosters and push areas",
		OverlayKind.Multiple => "trigger_multiple / once - boosters, gravity, map logic",
		OverlayKind.Hurt => "trigger_hurt - damage and kill areas",
		OverlayKind.Other => "every other trigger (gravity, ...)",
		_ => "invisible walls of the map",
	};

	/// <summary>A TrailColors.Palette color, so menus show its name</summary>
	internal static string DefaultColor(OverlayKind kind) => kind switch
	{
		OverlayKind.Teleport => "#B070E0", // Purple
		OverlayKind.Push => "#00E5FF", // Cyan
		OverlayKind.Multiple => "#FB8C00", // Orange
		OverlayKind.Hurt => "#E53935", // Red
		OverlayKind.Other => "#FFFFFF", // White
		_ => "#F06292", // Pink
	};

	/// <summary>The player's color of a kind, else its default</summary>
	internal static string ColorOf(PlayerOptions options, OverlayKind kind) =>
		options.OverlayColor(kind) is { Length: > 0 } own ? own : DefaultColor(kind);

	/// <summary>The kind a trigger entity is drawn as - null: not drawn</summary>
	internal static OverlayKind? OfTrigger(string designerName) => designerName switch
	{
		"trigger_teleport" => OverlayKind.Teleport,
		"trigger_push" => OverlayKind.Push,
		"trigger_multiple" or "trigger_once" => OverlayKind.Multiple,
		"trigger_hurt" => OverlayKind.Hurt,
		"trigger_soundscape" => null, // Sound areas all over the map - nothing to see for a run
		_ => designerName.StartsWith("trigger_", StringComparison.Ordinal) ? OverlayKind.Other : null,
	};
}

/// <summary>
/// Map triggers and player clips, drawn for the admins who turn them on (!options - Visibility - Triggers &amp; clips,
/// !showtriggers / !showclips - anyone with an admin panel section): each kind on / off in its own color, per player. CS2 maps have nothing visible to show
/// for either (trigger models carry only physics, clips are part of the world collision), so their edges are drawn
/// with beams: triggers as their rotated bounds (from the entities), clips from the map's world physics read from its
/// VPK in the background (ClipGeometry.cs). Only edges within overlay_draw_distance of a viewer get a beam - one per
/// edge and color, shared by viewers with the same color - and CheckTransmit gives each viewer only theirs.
/// Main thread only.
/// </summary>
public partial class SurfTimer
{
	private readonly record struct OverlayEdge(Vector3 A, Vector3 B, OverlayKind Kind);
	private readonly record struct OverlayBeamKey(Vector3 A, Vector3 B, int Argb);

	/// <summary>Beams on the server at most - entities: Paint's dots, trails and the map need room too (16k)</summary>
	private const int MaxOverlayBeams = 2000;
	/// <summary>Beams one viewer gets at most - the nearest edges</summary>
	private const int MaxOverlayBeamsPerViewer = 1000;
	/// <summary>New beams per refresh - spawning a big batch at once would stall the tick</summary>
	private const int MaxOverlaySpawnsPerRefresh = 250;
	private const float OverlayBeamWidth = 1f;
	/// <summary>Every how many ticks the edges near each viewer are worked out again (8 a second)</summary>
	internal const int OverlayRefreshTicks = 8;
	/// <summary>Triggers are read again every 2 s: round restarts re-create them, maps turn them on / off or move them</summary>
	private const int OverlayTriggerTicks = 128;
	/// <summary>The eye above a pawn's origin - distances are measured from where the player looks</summary>
	private const float OverlayEyeHeight = 64f;

	private enum ClipLoadState { NotLoaded, Loading, Loaded, Failed }

	private List<OverlayEdge> _triggerEdges = new();
	private List<OverlayEdge> _clipEdges = new();
	private int? _triggersReadTick;
	private ClipLoadState _clipLoad;
	private int _clipLoadAttempts;
	private int _clipRetryTick;
	/// <summary>Counts map changes - a clip load finishing after its map ended is dropped</summary>
	private int _overlayGeneration;

	private readonly Dictionary<OverlayBeamKey, CBeam> _overlayBeams = new();
	// Per viewer slot: the overlay beams it must not receive. Everyone without an entry gets none of them.
	private readonly Dictionary<int, List<uint>> _overlayHiddenFor = new();
	private readonly List<uint> _overlayBeamIndexes = new();

	private bool OverlayBeamsExist => _overlayBeams.Count > 0;

	/// <summary>
	/// Every OverlayRefreshTicks (OnTick): the edges near each viewer, beams for the ones missing (a batch at a time),
	/// the ones nobody needs any more removed, and who gets which.
	/// </summary>
	private void TickMapOverlay()
	{
		var viewers = CurrentMap == null ? [] : playerList.Values
			.Where(p => p.Controller.IsValid && !p.Controller.IsBot && p.Options.AnyOverlay && AdminPermissions.CanOpen(p.Controller)).ToList();
		if (viewers.Count == 0)
		{
			if (_overlayBeams.Count > 0)
				ClearOverlayBeams();
			return;
		}

		if (viewers.Any(v => OverlayKinds.Triggers.Any(v.Options.OverlayOn))
			&& (_triggersReadTick is not int last || Server.TickCount - last >= OverlayTriggerTicks))
		{
			_triggersReadTick = Server.TickCount;
			_triggerEdges = ReadTriggerEdges();
		}
		if (viewers.Any(v => v.Options.OverlayOn(OverlayKind.Clip)))
			LoadClipEdges();

		var wantedBy = new Dictionary<int, HashSet<OverlayBeamKey>>();
		var wanted = new HashSet<OverlayBeamKey>();
		foreach (var viewer in viewers)
		{
			if (OverlayEyeOf(viewer.Controller) is not Vector3 eye)
				continue;
			var keys = NearOverlayEdges(viewer.Options, eye, Config.OverlayDrawDistance);
			wantedBy[viewer.Controller.Slot] = keys;
			wanted.UnionWith(keys);
		}

		// Beams nobody needs any more go - and the ones a round restart removed are drawn again
		foreach (var (key, beam) in _overlayBeams.ToList())
		{
			if (wanted.Contains(key) && beam.IsValid)
				continue;
			if (beam.IsValid)
				beam.Remove();
			_overlayBeams.Remove(key);
		}

		// shortcut: missing beams are spawned in set order, not nearest first, and once MaxOverlayBeams are up a third
		// viewer far from the others gets none - fine for a few viewers; spawn nearest first per viewer if that shows
		int spawns = 0;
		foreach (var key in wanted)
		{
			if (_overlayBeams.ContainsKey(key))
				continue;
			if (spawns >= MaxOverlaySpawnsPerRefresh || _overlayBeams.Count >= MaxOverlayBeams)
				break;
			var beam = CreateBeam(new CsVector(key.A.X, key.A.Y, key.A.Z), new CsVector(key.B.X, key.B.Y, key.B.Z),
				Color.FromArgb(key.Argb), OverlayBeamWidth);
			if (beam == null)
				break;
			_overlayBeams[key] = beam;
			spawns++;
		}

		_overlayBeamIndexes.Clear();
		_overlayBeamIndexes.AddRange(_overlayBeams.Values.Select(b => b.Index));
		_overlayHiddenFor.Clear();
		foreach (var (slot, keys) in wantedBy)
			_overlayHiddenFor[slot] = _overlayBeams.Where(b => !keys.Contains(b.Key)).Select(b => b.Value.Index).ToList();
	}

	/// <summary>The beams a viewer gets: the edges of its kinds within range, in its colors - the nearest when too many</summary>
	private HashSet<OverlayBeamKey> NearOverlayEdges(PlayerOptions options, Vector3 eye, float range)
	{
		var colors = OverlayKinds.All.Select(k => TrailColors.Resolve(OverlayKinds.ColorOf(options, k), 0).ToArgb()).ToArray();
		float rangeSq = range * range;
		var near = new List<(float DistanceSq, OverlayEdge Edge)>();
		foreach (var edges in (List<OverlayEdge>[])[_triggerEdges, _clipEdges])
		{
			foreach (var edge in edges)
			{
				if (!options.OverlayOn(edge.Kind))
					continue;
				float distanceSq = DistanceSq(eye, edge.A, edge.B);
				if (distanceSq <= rangeSq)
					near.Add((distanceSq, edge));
			}
		}

		if (near.Count > MaxOverlayBeamsPerViewer)
		{
			near.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
			near.RemoveRange(MaxOverlayBeamsPerViewer, near.Count - MaxOverlayBeamsPerViewer);
		}
		return near.Select(n => new OverlayBeamKey(n.Edge.A, n.Edge.B, colors[(int)n.Edge.Kind])).ToHashSet();
	}

	/// <summary>Squared distance from a point to the closest point of a segment</summary>
	private static float DistanceSq(Vector3 point, Vector3 a, Vector3 b)
	{
		var ab = b - a;
		float t = Math.Clamp(Vector3.Dot(point - a, ab) / Math.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
		return Vector3.DistanceSquared(point, a + ab * t);
	}

	/// <summary>Where a viewer looks from: its pawn, or the player / bot it spectates</summary>
	private static Vector3? OverlayEyeOf(CCSPlayerController controller)
	{
		CBaseEntity? pawn = ObservedPawn(controller) ?? controller.Pawn.Value;
		var origin = pawn != null && pawn.IsValid ? pawn.AbsOrigin : null;
		return origin == null ? null : new Vector3(origin.X, origin.Y, origin.Z + OverlayEyeHeight);
	}

	/// <summary>
	/// The 12 edges of every enabled trigger's bounds, turned like the trigger (collision mins / maxs are entity-local).
	/// Zone triggers (map_start, s2_start, ...) are left out - Show zones draws them.
	/// </summary>
	private static List<OverlayEdge> ReadTriggerEdges()
	{
		var edges = new List<OverlayEdge>();
		Span<Vector3> corners = stackalloc Vector3[8];
		foreach (var trigger in Utilities.FindAllEntitiesByDesignerName<CBaseTrigger>("trigger_"))
		{
			if (!trigger.IsValid || trigger.Disabled || OverlayKinds.OfTrigger(trigger.DesignerName) is not OverlayKind kind)
				continue;
			if (kind == OverlayKind.Multiple && ZoneName.TryParse(trigger.Entity?.Name, out _, out _))
				continue;
			var origin = trigger.AbsOrigin;
			var angles = trigger.AbsRotation;
			if (origin == null || angles == null)
				continue;

			// Source angles: pitch (X) about Y, yaw (Y) about Z, roll (Z) about X - roll first, yaw last
			const float degrees = MathF.PI / 180f;
			var rotation = Matrix4x4.CreateRotationX(angles.Z * degrees) * Matrix4x4.CreateRotationY(angles.X * degrees)
				* Matrix4x4.CreateRotationZ(angles.Y * degrees);
			var mins = trigger.Collision.Mins;
			var maxs = trigger.Collision.Maxs;
			var center = new Vector3(origin.X, origin.Y, origin.Z);
			for (int i = 0; i < 8; i++)
			{
				var local = new Vector3((i & 1) == 0 ? mins.X : maxs.X, (i & 2) == 0 ? mins.Y : maxs.Y, (i & 4) == 0 ? mins.Z : maxs.Z);
				corners[i] = center + Vector3.Transform(local, rotation);
			}
			// Corner i and i with one more bit set differ in one axis: an edge
			for (int i = 0; i < 8; i++)
			{
				for (int bit = 1; bit < 8; bit <<= 1)
				{
					if ((i & bit) == 0)
						edges.Add(new OverlayEdge(corners[i], corners[i | bit], kind));
				}
			}
		}
		return edges;
	}

	/// <summary>
	/// Reads the map's player clips in the background once someone wants them. The map's workshop id may not be known
	/// right after the map started (map list scan) - a failed read is tried again a few times.
	/// </summary>
	private void LoadClipEdges()
	{
		if (_clipLoad is ClipLoadState.Loading or ClipLoadState.Loaded
			|| (_clipLoad == ClipLoadState.Failed && Server.TickCount < _clipRetryTick))
			return;
		string? mapName = CurrentMap?.Name;
		if (mapName == null)
			return;

		ulong? workshopId = WorkshopIdOf(mapName) ?? CurrentMap!.WorkshopId;
		var addonFolders = workshopId is ulong id
			? WorkshopContentFolders().Select(folder => Path.Combine(folder, id.ToString())).ToList()
			: [];
		string localVpk = Path.Combine(Server.GameDirectory, "csgo", "maps", mapName + ".vpk");
		int generation = _overlayGeneration;
		_clipLoad = ClipLoadState.Loading;

		Task.Run(() =>
		{
			List<OverlayEdge>? edges = null;
			string? error = null;
			try
			{
				var bytes = ReadMapVpkFile(addonFolders, localVpk, mapName, "vmdl_c", $"maps/{mapName}", "world_physics");
				if (bytes == null)
					error = workshopId == null ? "the map's VPK wasn't found (workshop id unknown)" : "the map's VPK wasn't found";
				else
					edges = ClipGeometry.Read(bytes).Select(e => new OverlayEdge(e.A, e.B, OverlayKind.Clip)).ToList();
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}

			Server.NextFrame(() =>
			{
				if (generation != _overlayGeneration)
					return;
				if (edges != null)
				{
					_clipEdges = edges;
					_clipLoad = ClipLoadState.Loaded;
					_logger.LogInformation("[MapOverlay] {Map}: {Edges} player clip edges", mapName, edges.Count);
					return;
				}

				_clipLoad = ClipLoadState.Failed;
				_clipLoadAttempts++;
				bool giveUp = _clipLoadAttempts >= 3;
				_clipRetryTick = giveUp ? int.MaxValue : Server.TickCount + 64 * 15;
				if (giveUp)
					_logger.LogWarning("[MapOverlay] {Map}: player clips can't be shown - {Error}", mapName, error);
			});
		});
	}

	/// <summary>Leaves out the overlay beams a viewer doesn't get - all of them for viewers without the overlay</summary>
	private void FilterOverlayTransmit(CCheckTransmitInfo info, CCSPlayerController viewer)
	{
		var hidden = _overlayHiddenFor.TryGetValue(viewer.Slot, out var own) ? own : _overlayBeamIndexes;
		foreach (uint index in hidden)
			info.TransmitEntities.Remove(index);
	}

	private void ClearOverlayBeams()
	{
		foreach (var beam in _overlayBeams.Values)
		{
			if (beam.IsValid)
				beam.Remove();
		}
		_overlayBeams.Clear();
		_overlayBeamIndexes.Clear();
		_overlayHiddenFor.Clear();
	}

	/// <summary>Map end - the beams go with the map, the map's edges are read again on the next one</summary>
	private void ForgetMapOverlay()
	{
		_overlayBeams.Clear();
		_overlayBeamIndexes.Clear();
		_overlayHiddenFor.Clear();
		_triggerEdges = new();
		_clipEdges = new();
		_triggersReadTick = null;
		_clipLoad = ClipLoadState.NotLoaded;
		_clipLoadAttempts = 0;
		_overlayGeneration++;
	}
}
