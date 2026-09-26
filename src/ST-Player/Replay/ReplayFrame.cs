using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using SurfTimer.Shared.Types;

namespace SurfTimer;

public enum ReplayFrameSituation
{
	NONE,

	STAGE_ZONE_ENTER,
	STAGE_ZONE_EXIT,

	START_ZONE_ENTER,
	START_ZONE_EXIT,

	END_ZONE_ENTER,
	END_ZONE_EXIT,

	CHECKPOINT_ZONE_ENTER,
	CHECKPOINT_ZONE_EXIT,

	// START_RUN,
	// END_RUN,
	// TOUCH_CHECKPOINT,
	// START_STAGE,
	// END_STAGE,
	// ENTER_STAGE,
}

[Serializable]
public class ReplayFrame
{
	public float[] pos { get; set; } = { 0, 0, 0 };
	public float[] ang { get; set; } = { 0, 0, 0 };
	public ReplayFrameSituation Situation { get; set; } = ReplayFrameSituation.NONE;
	public uint Flags { get; set; }
	/// <summary>
	/// Buttons held this tick (PlayerButtons). null in replays recorded before buttons were stored.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ulong? Buttons { get; set; }

	// Strafe sync prefix sums up to and including this frame (see PrepareSync) - not stored
	[JsonIgnore] internal int SyncGood { get; set; }
	[JsonIgnore] internal int SyncTotal { get; set; }

	public VectorT GetPos()
	{
		return new VectorT(this.pos[0], this.pos[1], this.pos[2]);
	}
	public QAngleT GetAng()
	{
		return new QAngleT(this.ang[0], this.ang[1], this.ang[2]);
	}

	/// <summary>
	/// Decompresses and deserializes a stored replay_frames blob into a frame list.
	/// Shared by Map.SetReplayData (WR content templates) and PB replay loading.
	/// </summary>
	public static List<ReplayFrame> Deserialize(ReplayFramesString data)
	{
		JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = false, Converters = { new VectorTConverter(), new QAngleTConverter() } };
		string json = Compressor.Decompress(data.ToString());
		var frames = JsonSerializer.Deserialize<List<ReplayFrame>>(json, options)!;
		PrepareSync(frames);
		return frames;
	}

	/// <summary>
	/// Fills each frame's strafe sync prefix sums (StrafeSync, same as live players), so the sync of
	/// any frame window is a subtraction - works with pause and reverse playback. Replays without
	/// recorded buttons are left at 0.
	/// </summary>
	internal static void PrepareSync(List<ReplayFrame> frames)
	{
		int good = 0, total = 0;
		for (int i = 0; i < frames.Count; i++)
		{
			var frame = frames[i];
			if (i > 0 && frame.Buttons != null)
			{
				bool onGround = (frame.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
				bool? inSync = StrafeSync.Evaluate(frames[i - 1].ang[1], frame.ang[1], onGround, (PlayerButtons)frame.Buttons.Value);
				if (inSync != null)
				{
					total++;
					if (inSync.Value)
						good++;
				}
			}
			frame.SyncGood = good;
			frame.SyncTotal = total;
		}
	}
}
