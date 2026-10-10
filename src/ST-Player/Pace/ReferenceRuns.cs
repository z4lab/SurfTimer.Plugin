using CounterStrikeSharp.API;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// The path of a reference run (PB, WR, a rank) for the live pace (PaceTracker): where the runner was at every tick of the
/// run - positions only, packed (12 bytes per tick) - and how fast. Frame i is run tick i.
/// </summary>
internal sealed class ReferencePath
{
	/// <summary>x, y, z per tick</summary>
	internal float[] Positions { get; }
	/// <summary>3D speed per tick (u/s)</summary>
	internal float[] Speeds { get; }
	internal int Count => Speeds.Length;

	private ReferencePath(float[] positions, float[] speeds)
	{
		Positions = positions;
		Speeds = speeds;
	}

	/// <summary>
	/// The run window of a replay: from leaving its start (map / bonus start, or the stage's start for a stage replay)
	/// to entering its end. Null when the replay has no usable run.
	/// </summary>
	/// <param name="type">0 map, 1 bonus, 2 stage</param>
	internal static ReferencePath? FromFrames(IReadOnlyList<ReplayFrame> frames, short type, short stage)
	{
		var startSituation = type == 2 && stage > 1 ? ReplayFrameSituation.STAGE_ZONE_EXIT : ReplayFrameSituation.START_ZONE_EXIT;
		int start = -1;
		for (int i = 0; i < frames.Count; i++)
		{
			if (frames[i].Situation == startSituation)
			{
				start = i;
				break;
			}
		}
		if (start < 0)
			start = 0;

		int end = -1;
		for (int i = start + 1; i < frames.Count; i++)
		{
			var situation = frames[i].Situation;
			if (situation == ReplayFrameSituation.END_ZONE_ENTER || (type == 2 && situation == ReplayFrameSituation.STAGE_ZONE_ENTER))
			{
				end = i;
				break;
			}
		}
		if (end < 0)
			end = frames.Count - 1;

		int count = end - start + 1;
		if (count < 2)
			return null;

		var positions = new float[count * 3];
		for (int i = 0; i < count; i++)
		{
			var pos = frames[start + i].pos;
			positions[i * 3] = pos[0];
			positions[i * 3 + 1] = pos[1];
			positions[i * 3 + 2] = pos[2];
		}

		var speeds = new float[count];
		for (int i = 0; i < count; i++)
		{
			int a = Math.Min(i, count - 2), b = a + 1;
			float dx = positions[b * 3] - positions[a * 3];
			float dy = positions[b * 3 + 1] - positions[a * 3 + 1];
			float dz = positions[b * 3 + 2] - positions[a * 3 + 2];
			speeds[i] = MathF.Sqrt(dx * dx + dy * dy + dz * dz) * ReplayCodec.TickRate;
		}
		return new ReferencePath(positions, speeds);
	}
}

/// <summary>
/// Reference paths by replay id - built from a replay already in memory (the WR templates) or loaded from the database in
/// the background (PBs, ranks). Kept for the map (a few hundred KB each, at most MaxPaths), forgotten on map change.
/// Main thread only.
/// </summary>
internal static class ReferenceRuns
{
	private const int MaxPaths = 24;

	private static readonly Dictionary<(int ReplayId, short Type, short Stage), ReferencePath?> _paths = new();
	private static readonly LinkedList<(int, short, short)> _order = new();
	private static readonly HashSet<(int, short, short)> _loading = new();
	private static int _version;

	internal static void Clear()
	{
		_paths.Clear();
		_order.Clear();
		_loading.Clear();
		_version++;
	}

	/// <summary>
	/// The path of a stored replay - null while it loads (started here) or when it has none.
	/// </summary>
	/// <param name="inMemory">The replay's frames when they're loaded already (WR templates) - no database load then</param>
	internal static ReferencePath? Get(int replayId, short type, short stage, IReadOnlyList<ReplayFrame>? inMemory = null)
	{
		var key = (replayId, type, stage);
		if (_paths.TryGetValue(key, out var path))
			return path;

		if (inMemory != null && inMemory.Count > 0)
		{
			Store(key, ReferencePath.FromFrames(inMemory, type, stage));
			return _paths[key];
		}

		if (replayId <= 0 || !_loading.Add(key))
			return null;

		int version = _version;
		_ = Task.Run(async () =>
		{
			ReferencePath? result = null;
			try
			{
				var data = await TimeRepository.GetReplayDataAsync(replayId);
				if (data != null)
					result = ReferencePath.FromFrames(ReplayCodec.Decode(data), type, stage);
			}
			catch (Exception ex)
			{
				// Cached as "none" - the pace falls back to the splits
				Logger.LogError(ex, "[ReferenceRuns] Loading replay {ReplayId} failed", replayId);
			}

			Server.NextFrame(() =>
			{
				if (version != _version)
					return;
				_loading.Remove(key);
				Store(key, result);
			});
		});
		return null;
	}

	private static void Store((int, short, short) key, ReferencePath? path)
	{
		_paths[key] = path;
		_order.Remove(key);
		_order.AddLast(key);
		while (_order.Count > MaxPaths)
		{
			_paths.Remove(_order.First!.Value);
			_order.RemoveFirst();
		}
	}

	private static ILogger? _logger;
	private static ILogger Logger => _logger ??=
		SurfTimer.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ReferenceRuns");
}
