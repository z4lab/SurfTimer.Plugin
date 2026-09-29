using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace SurfTimer;

public class ReplayPlayer
{
	/// <summary>
	/// Enable or Disable the replay bots.
	/// </summary>
	public bool IsEnabled { get; set; } = Config.ReplaysEnabled;
	public bool IsPlaying { get; set; } = false;
	public bool IsPaused { get; set; } = false;
	public bool IsPlayable { get; set; } = false;

	// Tracking for replay counting
	public int RepeatCount { get; set; } = -1;

	public int MapID { get; set; } = -1;
	public int MapTimeID { get; set; } = -1;
	/// <summary>replays.id of the loaded frames - WR templates reload their replay only when it changes</summary>
	public int? ReplayId { get; set; }
	public int Type { get; set; } = -1;
	public int Stage { get; set; } = -1;
	public int Style { get; set; } = 0;

	public int RecordRank { get; set; } = -1; // This is used to determine whether replay is for wr or for pb
	public string RecordPlayerName { get; set; } = "N/A";
	internal int RecordPlayerId { get; set; } // players.id of whoever set the replayed run - 0 when unknown / several (best segments)
	internal string? TrailColor { get; set; } // Trail color from the holder's rank group (Trails.cs), for TrailColorPlayerId
	internal int TrailColorPlayerId { get; set; }
	public int RecordRunTime { get; set; } = -1;
	public int ReplayCurrentRunTime { get; set; } = 0;
	public bool IsReplayOutsideZone { get; set; } = false;

	// Pool slot bookkeeping
	public int RequestedByPlayerId { get; set; } = -1; // -1 = WR content, else the PB owner's Profile.ID
	public DateTime? IdleSince { get; set; } = null; // Set when this slot finishes a replay and goes idle
	public int? PendingSpectatorUserId { get; set; } = null; // UserId of whoever is waiting to auto-spectate once this slot's bot spawns

	// Pool bookkeeping (ReplayManager / OnTick upkeep)
	internal bool IsPermanent { get; set; } // The always-on map WR bot - not counted in the cap, never idled or kicked for being unwatched
	internal int? RequesterUserId { get; set; } // Player who requested this bot - one requested bot per player
	internal DateTime LastWatchedAt { get; set; } = DateTime.UtcNow; // Last time someone spectated it (or it was spawned / given new content)
	internal DateTime CreatedAt { get; } = DateTime.UtcNow; // For slots whose bot never arrives

	// Tracking
	public List<ReplayFrame> Frames { get; set; } = new List<ReplayFrame>();
	public List<int> StageEnterSituations { get; set; } = new List<int>();
	public List<int> StageExitSituations { get; set; } = new List<int>();
	public List<int> CheckpointEnterSituations { get; set; } = new List<int>();
	public List<int> CheckpointExitSituations { get; set; } = new List<int>();
	/// <summary>
	/// Indexes should always follow this pattern: START_ZONE_ENTER > START_ZONE_EXIT > END_ZONE_ENTER > END_ZONE_EXIT
	/// Where END_ZONE_EXIT is not guaranteed
	/// </summary>
	public List<int> MapSituations { get; set; } = new List<int>();
	/// <summary>
	/// Indexes should always follow this pattern: START_ZONE_ENTER > START_ZONE_EXIT > END_ZONE_ENTER > END_ZONE_EXIT
	/// Where END_ZONE_EXIT is not guaranteed
	/// </summary>
	public List<int> BonusSituations { get; set; } = new List<int>();

	// Playing
	public int CurrentFrameTick { get; set; } = 0;
	public int FrameTickIncrement { get; set; } = 1;
	// Frame the bot was put on this tick (CurrentFrameTick has already advanced past it)
	public int PlayedFrameIndex { get; private set; } = 0;

	// Frames where the recorded run's timer starts/stops - the HUD's sync and prespeed window
	public int RunStartFrame { get; private set; } = 0;
	public int RunEndFrame { get; private set; } = 0;
	// Frame list the run window was found for - recomputed when the content changes
	private List<ReplayFrame>? _runWindowFrames;

	// Visible crouch currently applied to the bot
	private bool? _appliedDuck;
	// Set when writing the crouch fails once (e.g. unknown schema field) - shared by all bots
	private static bool _duckDisabled;

	/// <summary>
	/// Replays recorded before buttons were stored have no keys or sync.
	/// </summary>
	public bool HasInputData => this.Frames.Count > 0 && this.Frames[0].Buttons != null;

	public CCSPlayerController? Controller { get; set; }

	private readonly ILogger<ReplayPlayer> _logger;

	// Constructor
	internal ReplayPlayer()
	{
		// Resolve the logger instance from the DI container
		_logger = SurfTimer.ServiceProvider.GetRequiredService<ILogger<ReplayPlayer>>();
	}

	internal void ResetReplay()
	{
		this.CurrentFrameTick = 0;
		this.PlayedFrameIndex = 0;
		this.FrameTickIncrement = 1;
		if (this.RepeatCount > 0)
			this.RepeatCount--;

		this.IsReplayOutsideZone = false;
		this.ReplayCurrentRunTime = 0;
	}

	internal void Reset()
	{
		this.IsPlaying = false;
		this.IsPaused = false;
		this.IsPlayable = false;
		this.RepeatCount = -1;

		this.Frames.Clear();

		this.ResetReplay();

		this.Controller = null;
	}

	internal void SetController(CCSPlayerController c, int repeat_count = -1, [CallerMemberName] string methodName = "")
	{
		this.Controller = c;
		if (repeat_count != -1)
			this.RepeatCount = repeat_count;
		this.IsPlayable = true;

		_logger.LogTrace("[{ClassName}] {MethodName} -> Set controller for {PlayerName}",
			nameof(ReplayPlayer), methodName, c.PlayerName
		);
	}

	/// <summary>
	/// Loads a content template (a WR record, or an ad-hoc PB template) into this pool slot,
	/// leaving Controller/pool-lifecycle fields untouched.
	/// </summary>
	internal void LoadContentFrom(ReplayPlayer source, int requestedByPlayerId = -1)
	{
		this.Type = source.Type;
		this.Stage = source.Stage;
		this.Style = source.Style;
		this.MapID = source.MapID;
		this.MapTimeID = source.MapTimeID;
		this.ReplayId = source.ReplayId;
		this.RecordRank = source.RecordRank;
		this.RecordPlayerName = source.RecordPlayerName;
		this.RecordPlayerId = source.RecordPlayerId;
		this.RecordRunTime = source.RecordRunTime;
		this.Frames = source.Frames;
		this.StageEnterSituations = source.StageEnterSituations;
		this.StageExitSituations = source.StageExitSituations;
		this.CheckpointEnterSituations = source.CheckpointEnterSituations;
		this.CheckpointExitSituations = source.CheckpointExitSituations;
		this.MapSituations = source.MapSituations;
		this.BonusSituations = source.BonusSituations;
		this.RequestedByPlayerId = requestedByPlayerId;
		this.FindRunWindow();
		this._appliedDuck = null;
	}

	/// <summary>
	/// Finds the frames where the recorded run's timer starts and stops, from the zone situations
	/// (replays are trimmed to a couple of seconds around the run). Falls back to the whole replay.
	/// </summary>
	private void FindRunWindow()
	{
		ReplayFrameSituation startSituation = this.Type switch
		{
			2 when this.Stage > 1 => ReplayFrameSituation.STAGE_ZONE_EXIT,
			3 when this.Stage > 1 => ReplayFrameSituation.CHECKPOINT_ZONE_EXIT,
			_ => ReplayFrameSituation.START_ZONE_EXIT,
		};
		ReplayFrameSituation segmentEnd = this.Type switch
		{
			2 => ReplayFrameSituation.STAGE_ZONE_ENTER,
			3 => ReplayFrameSituation.CHECKPOINT_ZONE_ENTER,
			_ => ReplayFrameSituation.END_ZONE_ENTER, // Map/bonus runs pass stage/checkpoint zones mid-run
		};

		int start = this.Frames.FindIndex(f => f.Situation == startSituation);
		if (start < 0)
			start = 0;

		int end = start + 1 < this.Frames.Count
			? this.Frames.FindIndex(start + 1, f => f.Situation == segmentEnd || f.Situation == ReplayFrameSituation.END_ZONE_ENTER)
			: -1;
		if (end < 0)
			end = Math.Max(0, this.Frames.Count - 1);

		this.RunStartFrame = start;
		this.RunEndFrame = end;
		this._runWindowFrames = this.Frames;
	}

	/// <summary>
	/// Frames where the recorded run starts (zone exit) and ends (zone enter).
	/// </summary>
	internal (int Start, int End) GetRunWindow()
	{
		if (!ReferenceEquals(this._runWindowFrames, this.Frames))
			this.FindRunWindow();
		return (this.RunStartFrame, this.RunEndFrame);
	}

	/// <summary>
	/// Buttons held in the frame being shown - null for replays without recorded buttons.
	/// </summary>
	internal PlayerButtons? CurrentButtons()
	{
		if (!this.HasInputData || this.PlayedFrameIndex >= this.Frames.Count)
			return null;
		return (PlayerButtons?)this.Frames[this.PlayedFrameIndex].Buttons;
	}

	/// <summary>
	/// Strafe sync of the recorded run up to the frame being shown: 0 before the run starts, frozen
	/// at the run's final sync after it ends. null for replays without recorded buttons.
	/// </summary>
	internal float? CurrentSync()
	{
		if (!this.HasInputData || this.Frames.Count == 0)
			return null;

		int frame = Math.Min(this.PlayedFrameIndex, this.Frames.Count - 1);
		if (frame <= this.RunStartFrame)
			return 0f;

		var start = this.Frames[this.RunStartFrame];
		var end = this.Frames[Math.Min(frame, this.RunEndFrame)];
		int total = end.SyncTotal - start.SyncTotal;
		return total > 0 ? 100f * (end.SyncGood - start.SyncGood) / total : 0f;
	}

	/// <summary>
	/// Prespeed like for players: the bot's live speed before the run starts, then the speed it left
	/// the start zone with (from the recorded positions, so old replays work too).
	/// </summary>
	internal float Prespeed(float liveVelocity)
	{
		if (this.PlayedFrameIndex <= this.RunStartFrame || this.RunStartFrame + 1 >= this.Frames.Count)
			return liveVelocity;

		var delta = this.Frames[this.RunStartFrame + 1].GetPos() - this.Frames[this.RunStartFrame].GetPos();
		return delta.Length() * 64;
	}

	/// <summary>
	/// Makes the bot model crouch when the recorded player was crouched (experimental - CS2's own
	/// movement code may override it, so it's re-applied every tick while crouched; standing up is
	/// only written once).
	/// </summary>
	private void ApplyDuck(CCSPlayerPawn pawn, bool ducked)
	{
		if (_duckDisabled || (!ducked && this._appliedDuck == false))
			return;

		var movement = pawn.MovementServices?.As<CCSPlayer_MovementServices>();
		if (movement == null)
			return;

		try
		{
			movement.DuckAmount = ducked ? 1f : 0f;
			movement.Ducked = ducked;
			movement.Ducking = false;
			movement.DesiresDuck = ducked;
			movement.DuckOverride = ducked;
			Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pMovementServices");
		}
		catch (Exception ex)
		{
			// E.g. a schema field this CS2 version doesn't have - never let it stop the replay
			_duckDisabled = true;
			_logger.LogError(ex, "[{ClassName}] Visible replay crouch failed - disabled until the plugin reloads", nameof(ReplayPlayer));
			return;
		}

		if (ducked)
			pawn.Flags |= (uint)PlayerFlags.FL_DUCKING;
		else
			pawn.Flags &= ~(uint)PlayerFlags.FL_DUCKING;

#if DEBUG
		if (this._appliedDuck != ducked)
			_logger.LogDebug("[{ClassName}] Replay bot '{RecordPlayerName}' {State} (frame {Frame})",
				nameof(ReplayPlayer), this.RecordPlayerName, ducked ? "crouches" : "stands up", this.PlayedFrameIndex);
#endif
		this._appliedDuck = ducked;
	}

	/// <summary>
	/// Stops playback, returns the underlying bot to Spectator with the generic "Replay" name,
	/// and marks the slot idle (eligible for reclaim by a new request, or kicked after
	/// Config.ReplayIdleTimeoutSeconds if nothing claims it).
	/// </summary>
	internal void GoIdle()
	{
		this.Stop();
		this.IdleSince = DateTime.UtcNow;

		if (this.Controller == null)
			return;

		SchemaString<CBasePlayerController> bot_name = new SchemaString<CBasePlayerController>(this.Controller, "m_iszPlayerName");
		bot_name.Set("Replay");

		Server.NextFrame(() =>
		{
			if (this.Controller == null)
				return;

			Utilities.SetStateChanged(this.Controller, "CBasePlayerController", "m_iszPlayerName");
			this.Controller.MoveToSpectator();
		});
	}

	internal void Start([CallerMemberName] string methodName = "")
	{
		// Only pool slots with a bot play - content templates just hold the frames
		if (!this.IsPlayable || !this.IsEnabled || this.Controller == null)
			return;

		Server.NextFrame(() =>
	{
		this.FormatBotName();
		this.IsPlaying = true;

#if DEBUG
		_logger.LogDebug("[{ClassName}] {MethodName} -> Starting replay for run {MapTimeID} (Map ID {MapID}) - {RecordPlayerName} (Stage {Stage})",
			nameof(ReplayPlayer), methodName, this.MapTimeID, this.MapID, this.RecordPlayerName, this.Stage
		);
#endif
	});
	}

	internal void Stop([CallerMemberName] string methodName = "")
	{
		this.IsPlaying = false;
#if DEBUG
		_logger.LogDebug("[{ClassName}] {MethodName} -> Stopping replay for run {MapTimeID} (Map ID {MapID}) - {RecordPlayerName} (Stage {Stage})",
			nameof(ReplayPlayer), methodName, this.MapTimeID, this.MapID, this.RecordPlayerName, this.Stage
		);
#endif
	}

	internal void Pause([CallerMemberName] string methodName = "")
	{
		if (!this.IsPlaying || !this.IsEnabled)
			return;

		this.IsPaused = !this.IsPaused;
#if DEBUG
		_logger.LogDebug("[{ClassName}] {MethodName} -> Pausing replay for run {MapTimeID} (Map ID {MapID}) - {RecordPlayerName} (Stage {Stage})",
			nameof(ReplayPlayer), methodName, this.MapTimeID, this.MapID, this.RecordPlayerName, this.Stage
		);
#endif
	}

	internal void Tick()
	{
		if (this.MapID == -1 || !this.IsEnabled || !this.IsPlaying || !this.IsPlayable || this.Frames.Count == 0)
			return;

		var pawn = this.Controller?.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid || !this.Controller!.PawnIsAlive)
			return;

		ReplayFrame current_frame = this.Frames[this.CurrentFrameTick];
		this.PlayedFrameIndex = this.CurrentFrameTick;

		this.FormatBotName();

		// Run timer straight from the frame position: frames from the recorded run's start (zone exit)
		// to its end (zone enter) - covers map/bonus/stage/checkpoint replays, pause and reverse alike
		if (!ReferenceEquals(this._runWindowFrames, this.Frames))
			this.FindRunWindow();
		this.IsReplayOutsideZone = this.PlayedFrameIndex > this.RunStartFrame && this.PlayedFrameIndex < this.RunEndFrame;
		this.ReplayCurrentRunTime = Math.Clamp(this.PlayedFrameIndex - this.RunStartFrame, 0, Math.Max(0, this.RunEndFrame - this.RunStartFrame));

		var current_pos = pawn.AbsOrigin!.ToVector_t();
		var current_frame_pos = current_frame.GetPos();
		var current_frame_ang = current_frame.GetAng();

		bool is_on_ground = (current_frame.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;

		VectorT velocity = (current_frame_pos - current_pos) * 64;

		if (is_on_ground)
			pawn.MoveType = MoveType_t.MOVETYPE_WALK;
		else
			pawn.MoveType = MoveType_t.MOVETYPE_NOCLIP;

		if ((current_pos - current_frame_pos).Length() > 200)
			Extensions.Teleport(pawn, current_frame_pos, current_frame_ang, null);
		else
			Extensions.Teleport(pawn, null, current_frame_ang, velocity);

		ApplyDuck(pawn, (current_frame.Flags & (uint)PlayerFlags.FL_DUCKING) != 0);


		if (!this.IsPaused)
		{
			this.CurrentFrameTick = Math.Max(0, this.CurrentFrameTick + this.FrameTickIncrement);
		}

		if (this.CurrentFrameTick >= this.Frames.Count)
			this.ResetReplay();
	}

	internal void LoadReplayData(int repeat_count = -1, [CallerMemberName] string methodName = "")
	{
		if (!this.IsPlayable || !this.IsEnabled)
			return;

		string replayType = this.Type switch
		{
			1 => "Bonus Replay",
			2 => "Stage Replay",
			0 => "Map Replay",
			3 => "Checkpoint Replay",
			ReplayManager.BestSegmentsType => "Best Segments Replay",
			_ => "Unknown Type",
		};

		if (this.MapID == -1)
		{
			_logger.LogWarning("[{ClassName}] {MethodName} -> [{Type}] No replay data found for Player. MapID {MapID} | MapTimeID {MapTimeID} | RecordPlayerName {RecordPlayerName}",
				nameof(ReplayPlayer), methodName, replayType, this.MapID, this.MapTimeID, RecordPlayerName
			);
			return;
		}

		_logger.LogTrace("[{ClassName}] {MethodName} -> [{Type}] Loaded replay data for Player '{RecordPlayerName}' | MapTime ID: {MapTimeID} | Repeat {Repeat} | Frames {TotalFrames} | Ticks {RecordTicks}",
			nameof(ReplayPlayer), methodName, replayType, this.RecordPlayerName, this.MapTimeID, repeat_count, this.Frames.Count, this.RecordRunTime
		);

		this.ResetReplay();
		this.RepeatCount = repeat_count;
	}

	internal void FormatBotName([CallerMemberName] string methodName = "")
	{
		// Content templates (MapWR etc.) have frames but no bot
		if (!this.IsPlayable || !this.IsEnabled || this.MapID == -1 || this.Controller == null || !this.Controller.IsValid)
			return;

		string prefix;
		if (this.RecordRank == 1)
		{
			prefix = "WR";
		}
		else
		{
			prefix = $"Rank #{this.RecordRank}";
		}

		if (this.Type == 1)
			prefix += $"B {this.Stage}";
		else if (this.Type == 2)
			prefix += $"S {this.Stage}";
		else if (this.Type == 3)
			prefix += $"CP {this.Stage}";

		SchemaString<CBasePlayerController> bot_name = new SchemaString<CBasePlayerController>(this.Controller!, "m_iszPlayerName");

		string replay_name = $"[{prefix}] {this.RecordPlayerName} | {PlayerHud.FormatTime(this.RecordRunTime)}";
		if (this.Type == ReplayManager.BestSegmentsType)
			replay_name = $"[{this.RecordPlayerName}] {PlayerHud.FormatTime(this.RecordRunTime)}"; // Several players' runs - no single name
		else if (this.RecordRunTime <= 0)
			replay_name = $"[{prefix}] {this.RecordPlayerName}";

		bot_name.Set(replay_name);
		Server.NextFrame(() =>
			Utilities.SetStateChanged(this.Controller!, "CBasePlayerController", "m_iszPlayerName")
		);
#if DEBUG
		// _logger.LogTrace("[{ClassName}] {MethodName} -> Changed replay bot name from '{OldName}' to '{NewName}'",
		//     nameof(ReplayPlayer), methodName, bot_name, replay_name
		// );
#endif
	}
}
