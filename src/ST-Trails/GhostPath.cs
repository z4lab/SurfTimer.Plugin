using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// Ghost &amp; path (!options - HUD - Ghost &amp; path): a target run (PB, WR, next rank, #10, a group's last rank - the
/// pace's reference runs, ReferenceRuns) drawn only for its player and whoever spectates them:
/// - the path a few seconds ahead of where they are on it, in beam segments coloured by the target's speed there, with a
///   square where it jumped and a triangle where it crouched (down) / stood up (up) - in a start zone from the start;
/// - an orb racing them: where the target was at their run time.
/// Beams can't be moved once spawned (clients draw them where they spawned - Trails.cs), so the path is a rolling set of
/// static segments and the orb is spawned again when it moves. The path is updated every GhostUpdateTicks per player, the
/// orb every Config.GhostOrbTicks (timer_settings ghost_orb_ticks, Server tab).
/// </summary>
public partial class SurfTimer
{
	private const int GhostUpdateTicks = 4;
	private const int GhostMaxSegments = 160;
	private const int GhostMaxMarkers = 40;
	private const float GhostPathWidth = 1.5f;
	/// <summary>The path itself: wider and glowing (HDR scale), its colours lifted towards white - most maps are dark</summary>
	private const float GhostLineWidth = 2.5f;
	private const float GhostLineBrightness = 4f;
	private const float GhostLineLift = 0.35f;
	private const float GhostPathLift = 2f;
	private const float GhostOrbHeight = 36f; // About the middle of a standing player
	private const float GhostOrbSize = 7f;
	private const float GhostOrbWidth = 7f;

	private static readonly Color MarkerJump = ColorTranslator.FromHtml("#ECEFF4");
	private static readonly Color MarkerCrouch = ColorTranslator.FromHtml("#B48EAD");
	private static readonly Color MarkerUncrouch = ColorTranslator.FromHtml("#88C0D0");

	/// <summary>One player's drawn ghost: the run it follows, where they are on it, the beams</summary>
	private sealed class GhostState
	{
		internal string? ReferenceKey;
		internal ReferencePath? Path;
		internal int MatchFrame = -1;
		internal int NextFullScanTick;
		/// <summary>Path segments by index into ReferencePath.SegmentStarts</summary>
		internal readonly Dictionary<int, CBeam> Segments = new();
		/// <summary>Marker shapes by index into ReferencePath.Markers</summary>
		internal readonly Dictionary<int, List<CBeam>> Markers = new();
		internal readonly List<CBeam> Orb = new();
		internal int OrbFrame = -1;
		/// <summary>Every beam's entity index - what other viewers don't get (CheckTransmit)</summary>
		internal readonly List<uint> Indexes = new();
		internal bool IndexesDirty;
	}

	/// <summary>By the owner's controller index</summary>
	private readonly Dictionary<uint, GhostState> _ghosts = new();

	/// <summary>
	/// The run a player's ghost follows - the splits panel's target, or the picked one. Stage / bonus mode: that stage's /
	/// bonus's PB (target PB) or WR (anything else), like the pace.
	/// </summary>
	private static PaceReference? GhostReferenceOf(Player p)
	{
		var target = p.Options.GhostTarget;
		if (target == GhostTarget.Splits)
			return PlayerHud.PaceReferenceOf(p, p.Options.HudSplitTarget == SplitTarget.Off ? SplitTarget.Pb : p.Options.HudSplitTarget);
		if (target != GhostTarget.NextRank)
		{
			var split = target switch
			{
				GhostTarget.Pb => SplitTarget.Pb,
				GhostTarget.Wr => SplitTarget.Wr,
				GhostTarget.Top10 => SplitTarget.Top10,
				_ => SplitTarget.G1 + (target - GhostTarget.G1),
			};
			return PlayerHud.PaceReferenceOf(p, split);
		}

		// Next rank: the time one above yours - you hold the WR: the WR; no time yet: the last one
		if (p.Timer.IsStageMode || p.Timer.IsBonusMode)
			return PlayerHud.PaceReferenceOf(p, SplitTarget.Wr);
		var map = CurrentMap;
		int style = p.Timer.Style;
		var pb = p.Stats.PB.GetValueOrDefault(style);
		int pbRank = pb != null && pb.ID != -1 && pb.RunTime > 0 ? pb.Rank : 0;
		int rank = pbRank > 1 ? pbRank - 1 : pbRank == 1 ? 1 : map.MapCompletions.GetValueOrDefault(style);
		return rank > 0 ? PlayerHud.RankReferenceOf(p, rank) : null;
	}

	/// <summary>What the options page shows: the run followed and whether its replay is there</summary>
	internal static string GhostStatusOf(Player p)
	{
		var reference = GhostReferenceOf(p);
		if (reference == null)
			return "no such run for this course yet";
		if (reference.ReplayId is not int replayId)
			return $"{PlayerHud.FormatTime(reference.TotalTicks)} - no replay stored for it";
		var path = ReferenceRuns.Get(replayId, reference.Type, reference.Stage, reference.InMemoryFrames);
		return path == null ? $"{PlayerHud.FormatTime(reference.TotalTicks)} - loading its replay…" : $"{PlayerHud.FormatTime(reference.TotalTicks)}";
	}

	private void TickGhosts()
	{
		int now = Server.TickCount;

		// Owners that left
		if (_ghosts.Count > 0 && now % 64 == 0)
		{
			foreach (uint owner in _ghosts.Keys.ToList())
			{
				if (!playerList.Values.Any(p => p.Controller.IsValid && p.Controller.Index == owner))
					RemoveGhost(owner);
			}
		}

		foreach (var p in playerList.Values)
		{
			var controller = p.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;
			bool pathTick = (now + controller.Slot) % GhostUpdateTicks == 0;
			bool orbTick = (now + controller.Slot) % Config.GhostOrbTicks == 0;
			if (!pathTick && !orbTick)
				continue;

			var options = p.Options;
			if (!options.GhostPath && !options.GhostOrb || !controller.PawnIsAlive || p.IsPlacementPending)
			{
				RemoveGhost(controller.Index);
				continue;
			}

			UpdateGhost(p, now, pathTick, orbTick);
		}
	}

	private void UpdateGhost(Player p, int now, bool pathTick, bool orbTick)
	{
		var controller = p.Controller;
		var options = p.Options;
		var reference = GhostReferenceOf(p);
		var path = reference?.ReplayId is int replayId
			? ReferenceRuns.Get(replayId, reference.Type, reference.Stage, reference.InMemoryFrames)
			: null;

		if (!_ghosts.TryGetValue(controller.Index, out var state))
			_ghosts[controller.Index] = state = new GhostState();

		// Another run to follow (or its replay just loaded): start over
		if (reference?.Key != state.ReferenceKey || !ReferenceEquals(path, state.Path))
		{
			ClearGhostBeams(state);
			state.ReferenceKey = reference?.Key;
			state.Path = path;
			state.MatchFrame = -1;
		}
		if (path == null)
			return;

		bool running = p.Timer.IsRunning;
		bool inStart = !running && p.IsTouchingAnyStartZone;

		// The path ahead: from where the player is on it (from its start in a start zone)
		if (pathTick && options.GhostPath && (running || inStart))
		{
			int anchor = inStart ? 0 : MatchGhost(state, path, controller, now);
			if (anchor >= 0)
				DrawGhostWindow(state, path, anchor, anchor + options.GhostSeconds * 64, options.GhostMarkers);
		}
		else if (pathTick)
		{
			ClearGhostPath(state);
		}

		// The orb: where the target was at the player's run time
		if (!orbTick)
			return;
		if (options.GhostOrb && running)
			DrawGhostOrb(state, path, Math.Min(p.Timer.Ticks, path.Count - 1));
		else
			ClearGhostOrb(state);
	}

	/// <summary>The path tick closest to the player - searched around the last one, the whole path once a second when lost</summary>
	private static int MatchGhost(GhostState state, ReferencePath path, CCSPlayerController controller, int now)
	{
		const int searchBack = 64, searchAhead = 64 * 4;
		const float lost = 400f;
		var origin = controller.PlayerPawn.Value?.AbsOrigin;
		if (origin == null)
			return state.MatchFrame;

		int best = -1;
		float bestDistance = float.MaxValue;
		void Search(int from, int to)
		{
			var positions = path.Positions;
			for (int i = Math.Max(0, from); i <= Math.Min(path.Count - 1, to); i++)
			{
				float dx = positions[i * 3] - origin.X, dy = positions[i * 3 + 1] - origin.Y, dz = positions[i * 3 + 2] - origin.Z;
				float distance = dx * dx + dy * dy + dz * dz;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = i;
				}
			}
		}

		if (state.MatchFrame >= 0)
			Search(state.MatchFrame - searchBack, state.MatchFrame + searchAhead);
		if ((best < 0 || bestDistance > lost * lost) && now >= state.NextFullScanTick)
		{
			state.NextFullScanTick = now + 64;
			Search(0, path.Count - 1);
		}

		// Off the path: the window stays where it was
		if (best < 0 || bestDistance > lost * lost)
			return state.MatchFrame;
		state.MatchFrame = best;
		return best;
	}

	/// <summary>The segments and markers between two path ticks - the ones that left the window are removed</summary>
	private static void DrawGhostWindow(GhostState state, ReferencePath path, int from, int to, bool markers)
	{
		var starts = path.SegmentStarts;
		int first = Array.BinarySearch(starts, from);
		if (first < 0)
			first = Math.Max(0, ~first - 1); // The segment the player is in

		var wanted = new HashSet<int>();
		for (int k = first; k < starts.Length - 1 && starts[k] < to && wanted.Count < GhostMaxSegments; k++)
			wanted.Add(k);

		foreach (var (k, beam) in state.Segments.ToList())
		{
			if (wanted.Contains(k) && beam.IsValid)
				continue;
			if (beam.IsValid)
				beam.Remove();
			state.Segments.Remove(k);
			state.IndexesDirty = true;
		}
		foreach (int k in wanted)
		{
			if (state.Segments.ContainsKey(k))
				continue;
			var a = path.PositionAt(starts[k]);
			var b = path.PositionAt(starts[k + 1]);
			var color = Lighten(ColorTranslator.FromHtml(Extensions.GetSpeedColorGradient(path.Speeds[starts[k]])), GhostLineLift);
			var beam = CreateBeam(new Vector(a.X, a.Y, a.Z + GhostPathLift), new Vector(b.X, b.Y, b.Z + GhostPathLift), color,
				GhostLineWidth, GhostLineBrightness, GhostBeamName);
			if (beam != null)
			{
				state.Segments[k] = beam;
				state.IndexesDirty = true;
			}
		}

		// Markers in the window
		var wantedMarkers = new HashSet<int>();
		if (markers)
		{
			for (int m = 0; m < path.Markers.Count && wantedMarkers.Count < GhostMaxMarkers; m++)
			{
				int frame = path.Markers[m].Frame;
				if (frame >= from && frame < to)
					wantedMarkers.Add(m);
			}
		}

		foreach (var (m, shape) in state.Markers.ToList())
		{
			if (wantedMarkers.Contains(m) && shape.All(b => b.IsValid))
				continue;
			RemoveBeams(shape);
			state.Markers.Remove(m);
			state.IndexesDirty = true;
		}
		foreach (int m in wantedMarkers)
		{
			if (state.Markers.ContainsKey(m))
				continue;
			state.Markers[m] = MarkerShape(path, path.Markers[m].Frame, path.Markers[m].Kind);
			state.IndexesDirty = true;
		}
	}

	/// <summary>A colour moved part of the way towards white</summary>
	private static Color Lighten(Color color, float amount) => Color.FromArgb(
		(int)(color.R + (255 - color.R) * amount), (int)(color.G + (255 - color.G) * amount), (int)(color.B + (255 - color.B) * amount));

	/// <summary>A flat square (jump), a triangle pointing down (crouch) or up (stand up) across the path</summary>
	private static List<CBeam> MarkerShape(ReferencePath path, int frame, PathMarker kind)
	{
		var at = path.PositionAt(frame);
		var corners = new List<Vector>();
		Color color;

		if (kind == PathMarker.Jump)
		{
			const float half = 8f;
			float z = at.Z + GhostPathLift;
			corners.Add(new Vector(at.X - half, at.Y - half, z));
			corners.Add(new Vector(at.X + half, at.Y - half, z));
			corners.Add(new Vector(at.X + half, at.Y + half, z));
			corners.Add(new Vector(at.X - half, at.Y + half, z));
			color = MarkerJump;
		}
		else
		{
			// Upright, across the direction the target moved
			var ahead = path.PositionAt(frame + 4);
			float dx = ahead.X - at.X, dy = ahead.Y - at.Y;
			float length = MathF.Sqrt(dx * dx + dy * dy);
			float sx = length > 0.01f ? -dy / length : 1f, sy = length > 0.01f ? dx / length : 0f;
			const float half = 8f, low = 6f, high = 22f;
			bool down = kind == PathMarker.Crouch;
			float baseZ = at.Z + (down ? high : low), tipZ = at.Z + (down ? low : high);
			corners.Add(new Vector(at.X + sx * half, at.Y + sy * half, baseZ));
			corners.Add(new Vector(at.X - sx * half, at.Y - sy * half, baseZ));
			corners.Add(new Vector(at.X, at.Y, tipZ));
			color = down ? MarkerCrouch : MarkerUncrouch;
		}

		var beams = new List<CBeam>();
		for (int i = 0; i < corners.Count; i++)
		{
			var beam = CreateBeam(corners[i], corners[(i + 1) % corners.Count], color, GhostPathWidth, name: GhostBeamName);
			if (beam != null)
				beams.Add(beam);
		}
		return beams;
	}

	/// <summary>The orb: three thick crossing beams, spawned again whenever it moves</summary>
	private static void DrawGhostOrb(GhostState state, ReferencePath path, int frame)
	{
		if (frame == state.OrbFrame && state.Orb.All(b => b.IsValid))
			return;

		RemoveBeams(state.Orb);
		state.Orb.Clear();
		state.OrbFrame = frame;
		state.IndexesDirty = true;

		var at = path.PositionAt(frame);
		float x = at.X, y = at.Y, z = at.Z + GhostOrbHeight;
		var color = ColorTranslator.FromHtml(Extensions.GetSpeedColorGradient(path.Speeds[frame]));
		(Vector, Vector)[] axes =
		[
			(new Vector(x - GhostOrbSize, y, z), new Vector(x + GhostOrbSize, y, z)),
			(new Vector(x, y - GhostOrbSize, z), new Vector(x, y + GhostOrbSize, z)),
			(new Vector(x, y, z - GhostOrbSize), new Vector(x, y, z + GhostOrbSize)),
		];
		foreach (var (a, b) in axes)
		{
			var beam = CreateBeam(a, b, color, GhostOrbWidth, name: GhostBeamName);
			if (beam != null)
				state.Orb.Add(beam);
		}
	}

	private static void RemoveBeams(List<CBeam> beams)
	{
		foreach (var beam in beams)
		{
			if (beam.IsValid)
				beam.Remove();
		}
	}

	private static void ClearGhostPath(GhostState state)
	{
		if (state.Segments.Count == 0 && state.Markers.Count == 0)
			return;
		foreach (var beam in state.Segments.Values)
		{
			if (beam.IsValid)
				beam.Remove();
		}
		state.Segments.Clear();
		foreach (var shape in state.Markers.Values)
			RemoveBeams(shape);
		state.Markers.Clear();
		state.IndexesDirty = true;
	}

	private static void ClearGhostOrb(GhostState state)
	{
		if (state.Orb.Count == 0)
			return;
		RemoveBeams(state.Orb);
		state.Orb.Clear();
		state.OrbFrame = -1;
		state.IndexesDirty = true;
	}

	private static void ClearGhostBeams(GhostState state)
	{
		ClearGhostPath(state);
		ClearGhostOrb(state);
	}

	private void RemoveGhost(uint owner)
	{
		if (_ghosts.Remove(owner, out var state))
			ClearGhostBeams(state);
	}

	/// <summary>Plugin unload - every ghost's beams</summary>
	private void RemoveAllGhosts()
	{
		foreach (uint owner in _ghosts.Keys.ToList())
			RemoveGhost(owner);
	}

	/// <summary>Every beam the ghosts track - SweepBeams keeps these</summary>
	private IEnumerable<uint> GhostBeamIndexes() =>
		_ghosts.Values.SelectMany(state => state.Segments.Values
			.Concat(state.Markers.Values.SelectMany(shape => shape))
			.Concat(state.Orb))
			.Where(beam => beam.IsValid)
			.Select(beam => beam.Index);

	/// <summary>Map end - the map's entities are gone already</summary>
	private void ForgetGhosts() => _ghosts.Clear();

	/// <summary>A viewer gets only their own ghost and the one of whoever they spectate (Visibility.cs)</summary>
	private void FilterGhostTransmit(CCheckTransmitInfo info, CCSPlayerController viewer)
	{
		uint observed = ObservedPawn(viewer) is { } target
			? new CCSPlayerPawn(target.Handle).Controller.Value?.Index ?? 0
			: 0;

		foreach (var (owner, state) in _ghosts)
		{
			if (owner == viewer.Index || owner == observed)
				continue;

			if (state.IndexesDirty)
			{
				state.Indexes.Clear();
				state.Indexes.AddRange(state.Segments.Values.Where(b => b.IsValid).Select(b => b.Index));
				state.Indexes.AddRange(state.Markers.Values.SelectMany(s => s).Where(b => b.IsValid).Select(b => b.Index));
				state.Indexes.AddRange(state.Orb.Where(b => b.IsValid).Select(b => b.Index));
				state.IndexesDirty = false;
			}
			foreach (uint index in state.Indexes)
				info.TransmitEntities.Remove(index);
		}
	}
}
