using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Trails: a line of beam segments behind players in a rank group (#1, #2, #3, top 10 / 50 / 100) or with a
/// role (VIP / admin / root), and behind replay bots. Every segment is its own beam, removed once it's older
/// than the trail length - beams aren't moved after they spawn (clients don't redraw moved beams reliably).
/// Who sees which trail is filtered per viewer in CheckTransmit (Visibility.cs).
/// </summary>
public partial class SurfTimer
{
	private sealed class TrailSegment
	{
		internal CBeam Beam = null!;
		internal int Tick;
	}

	private sealed class TrailRing
	{
		internal readonly Queue<TrailSegment> Segments = new(); // Oldest first
		internal Vector? LastPos;
		internal int Step; // Rainbow hue steps
		internal bool IsBot;
	}

	private const float TrailHeight = 4f;
	private const float TrailTeleportDistance = 300f;

	// By the source's controller entity index (players and replay bots). Main thread only.
	private readonly Dictionary<uint, TrailRing> _trails = new();

	/// <summary>Replay bot trail colors being looked up (holder's rank) - by holder player id</summary>
	private readonly HashSet<int> _trailColorLookups = new();

	private void LoadTrailSettings()
	{
		string? error = TrailSettings.Load();
		if (error != null)
			_logger.LogError("[Trails] trail_settings.json couldn't be read ({Error}) - using the defaults", error);
	}

	// ---- Colors ----

	private static string? RankGroupColor(int? rank, TrailSettings settings) => rank switch
	{
		null => null,
		1 => settings.Colors.First,
		2 => settings.Colors.Second,
		3 => settings.Colors.Third,
		<= 10 => settings.Colors.Top10,
		<= 50 => settings.Colors.Top50,
		<= 100 => settings.Colors.Top100,
		_ => null,
	};

	private static string? RoleColor(CCSPlayerController controller, TrailSettings settings)
	{
		var flags = ChatSettings.Current.Flags;
		return AdminManager.PlayerHasPermissions(controller, flags.Root) ? settings.Colors.Root
			: AdminManager.PlayerHasPermissions(controller, flags.Admin) ? settings.Colors.Admin
			: AdminManager.PlayerHasPermissions(controller, flags.Vip) ? settings.Colors.Vip
			: null;
	}

	/// <summary>Top 3 and VIP / admin / root may pick their own trail color</summary>
	internal static bool CanPickTrailColor(Player player) =>
		player.Profile.ServerRank is <= 3 || RoleColor(player.Controller, TrailSettings.Current) != null;

	/// <summary>
	/// The player's trail color (hex or rainbow): their own pick if they may choose one, else the rank group
	/// color, else the role color - null means no trail.
	/// </summary>
	internal static string? TrailColorFor(Player player)
	{
		var settings = TrailSettings.Current;
		string? group = RankGroupColor(player.Profile.ServerRank, settings) ?? RoleColor(player.Controller, settings);
		if (group == null)
			return null;

		string custom = player.Options.TrailColor;
		return custom.Length > 0 && CanPickTrailColor(player) ? custom : group;
	}

	/// <summary>
	/// A replay bot's color: its holder's rank group color (looked up once per holder), else the replay color.
	/// </summary>
	private string ReplayTrailColor(ReplayPlayer slot)
	{
		var settings = TrailSettings.Current;
		if (slot.RecordPlayerId <= 0)
			return settings.Colors.Replay;
		if (slot.TrailColorPlayerId == slot.RecordPlayerId && slot.TrailColor != null)
			return slot.TrailColor;

		int holderId = slot.RecordPlayerId;
		if (_trailColorLookups.Add(holderId))
		{
			_ = Task.Run(async () =>
			{
				(int Rank, long Points)? standing = null;
				try
				{
					var ranks = await PlayerRepository.GetServerRanksAsync([holderId]);
					if (ranks.TryGetValue(holderId, out var found))
						standing = found;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "[Trails] Looking up the rank of replay holder {PlayerId} failed", holderId);
				}

				Server.NextFrame(() =>
				{
					_trailColorLookups.Remove(holderId);
					string color = RankGroupColor(standing?.Rank, TrailSettings.Current) ?? TrailSettings.Current.Colors.Replay;
					foreach (var other in CurrentMap?.ReplayManager?.Pool ?? [])
					{
						if (other.RecordPlayerId == holderId)
						{
							other.TrailColor = color;
							other.TrailColorPlayerId = holderId;
						}
					}
				});
			});
		}
		return settings.Colors.Replay;
	}

	// ---- Drawing ----

	/// <summary>
	/// Every settings.SegmentTicks (OnTick): a new segment behind every source that moves, segments older
	/// than the trail length removed (moving or not), trails of sources that stopped having one removed.
	/// </summary>
	private void TickTrails()
	{
		var settings = TrailSettings.Current;
		if (!settings.Enabled || CurrentMap == null)
		{
			if (_trails.Count > 0)
				RemoveAllTrails();
			return;
		}

		int now = Server.TickCount;
		if (now % settings.SegmentTicks != 0)
			return;

		var drawn = new HashSet<uint>();

		foreach (var player in playerList.Values)
		{
			var controller = player.Controller;
			if (!controller.IsValid || controller.IsBot || !player.Options.TrailMine)
				continue;

			string? color = TrailColorFor(player);
			if (color != null && DrawTrail(controller, color, isBot: false, settings, now))
				drawn.Add(controller.Index);
		}

		foreach (var slot in CurrentMap.ReplayManager?.Pool ?? [])
		{
			if (slot.Controller == null || !slot.Controller.IsValid || !slot.IsPlaying)
				continue;
			if (DrawTrail(slot.Controller, ReplayTrailColor(slot), isBot: true, settings, now))
				drawn.Add(slot.Controller.Index);
		}

		int lengthTicks = (int)(settings.LengthSeconds * 64);
		int maxSegments = settings.SegmentCount;
		foreach (var (source, ring) in _trails.ToList())
		{
			if (!drawn.Contains(source))
			{
				RemoveTrail(source);
				continue;
			}

			// Oldest first: too old, or more than the trail length allows (after a settings change)
			while (ring.Segments.Count > 0 && (now - ring.Segments.Peek().Tick > lengthTicks || ring.Segments.Count > maxSegments))
				RemoveSegment(ring.Segments.Dequeue());
		}
	}

	/// <summary>
	/// Adds a segment behind one source (alive, moving). Returns false when the source has no trail now.
	/// </summary>
	private bool DrawTrail(CCSPlayerController controller, string color, bool isBot, TrailSettings settings, int now)
	{
		var pawn = controller.PlayerPawn.Value;
		if (!controller.PawnIsAlive || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
			return false;

		if (!_trails.TryGetValue(controller.Index, out var ring))
			_trails[controller.Index] = ring = new TrailRing { IsBot = isBot };

		var origin = pawn.AbsOrigin;
		var position = new Vector(origin.X, origin.Y, origin.Z + TrailHeight);
		var velocity = pawn.AbsVelocity;
		float speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);

		if (ring.LastPos is Vector last)
		{
			float dx = position.X - last.X, dy = position.Y - last.Y, dz = position.Z - last.Z;
			float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

			// Teleports (!r, map teleporters) start a new trail instead of a line across the map
			if (distance <= TrailTeleportDistance && distance > 1f && speed >= settings.MinSpeed)
			{
				var beam = CreateBeam(last, position, TrailColors.Resolve(color, ring.Step++), (float)settings.Width);
				if (beam != null)
					ring.Segments.Enqueue(new TrailSegment { Beam = beam, Tick = now });
			}
		}

		ring.LastPos = position;
		return true;
	}

	/// <summary>
	/// A new beam for one segment. Beams are never moved after they spawn - clients cull and draw them by
	/// where they spawned, so a moved beam vanishes or shows in pieces.
	/// </summary>
	private static CBeam? CreateBeam(Vector start, Vector end, Color color, float width)
	{
		var beam = Utilities.CreateEntityByName<CBeam>("beam");
		if (beam == null)
			return null;

		beam.Render = color;
		beam.Width = width;
		beam.EndWidth = width;
		beam.Teleport(start, new QAngle(0, 0, 0), new Vector(0, 0, 0));
		beam.EndPos.X = end.X;
		beam.EndPos.Y = end.Y;
		beam.EndPos.Z = end.Z;
		beam.DispatchSpawn();
		return beam;
	}

	private static void RemoveSegment(TrailSegment segment)
	{
		if (segment.Beam.IsValid)
			segment.Beam.Remove();
	}

	private void RemoveTrail(uint source)
	{
		if (!_trails.Remove(source, out var ring))
			return;

		foreach (var segment in ring.Segments)
		{
			if (segment.Beam.IsValid)
				segment.Beam.Remove();
		}
	}

	private void RemoveAllTrails()
	{
		foreach (var source in _trails.Keys.ToList())
			RemoveTrail(source);
	}

	/// <summary>Map end - the beams go with the map, only the bookkeeping is dropped</summary>
	private void ForgetTrails()
	{
		_trails.Clear();
		_trailColorLookups.Clear();
	}

	// ---- Who sees which trail (CheckTransmit, see Visibility.cs) ----

	/// <summary>
	/// Leaves out the beams a viewer doesn't want: their own, the spectated source's, replay bots' and other
	/// players' trails each have their own toggle; hidden players / bots take their trails along.
	/// </summary>
	private void FilterTrailTransmit(CCheckTransmitInfo info, CCSPlayerController viewer, PlayerOptions options)
	{
		uint observed = ObservedPawn(viewer) is { } target
			? new CCSPlayerPawn(target.Handle).Controller.Value?.Index ?? 0
			: 0;

		foreach (var (source, ring) in _trails)
		{
			bool visible = source == viewer.Index ? options.TrailsOwn
				: source == observed ? options.TrailsSpectate
				: ring.IsBot ? options.TrailsBots && !options.HideBots
				: options.TrailsOthers && !options.HidePlayers;
			if (visible)
				continue;

			foreach (var segment in ring.Segments)
			{
				if (segment.Beam.IsValid)
					info.TransmitEntities.Remove(segment.Beam.Index);
			}
		}
	}
}
