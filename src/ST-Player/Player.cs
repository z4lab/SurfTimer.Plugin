using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;

namespace SurfTimer;

public class Player
{
	// CCS requirements
	public CCSPlayerController Controller { get; }
	public CCSPlayer_MovementServices MovementServices { get; } // Can be used later for any movement modification (eg: styles)

	// Timer-related properties
	public PlayerTimer Timer { get; set; }
	public PlayerStats Stats { get; set; }
	public PlayerHud HUD { get; set; }
	public ReplayRecorder ReplayRecorder { get; set; }

	// Player information
	public PlayerProfile Profile { get; set; }

	// !repeat - send the player back to the start of each stage they finish (off on join)
	internal bool IsRepeatMode { get; set; } = false;

	// !options - client options (own legs, hiding players, chat, HUD), saved in player_settings
	internal PlayerOptions Options { get; }

	// Scoreboard clan tag (see Scoreboard.cs): the player's own tag, and what the plugin last wrote - a
	// different value means the player changed their tag
	internal string UserClanTag { get; set; } = "";
	internal string? AppliedClanTag { get; set; }

	// !admin / !options - the open panels (tab, pages), kept while connected
	internal PanelSession? Admin { get; set; }
	internal PanelSession? OptionsPanel { get; set; }

	// Anti-prehop/bhop state (map start zone + every stage start zone). Deliberately not on
	// PlayerTimer - Timer.Reset() fires on every start-zone entry, which would wrongly clear this;
	// this state must only clear when velocity actually drops below the cap.
	// Zone boxes the player is currently inside, by ZoneInfo.ZoneId (ZoneTracker.cs). Several at once, so
	// overlapping / duplicate boxes of one zone are handled.
	internal Dictionary<int, ZoneInfo> TouchingTriggers { get; } = new();

	/// <summary>
	/// The course the player is locked to: 0 = the map (map start / end, stages, checkpoints), N = bonus N.
	/// Zones of other courses don't count at all - crossing them changes nothing - until !r / !s / !b (or a
	/// saveloc) switches the course. Stop, teleport-back and speed cap zones count on every course.
	/// </summary>
	internal short CourseBonus { get; set; }

	/// <summary>Tick of the last saveloc load - "Teleported to #N" is shown once per burst of loads</summary>
	internal int LastSavelocLoadTick { get; set; } = int.MinValue / 2;

	internal bool IsOnCourse(ZoneInfo zone) => zone.Type switch
	{
		ZoneType.MapStart or ZoneType.MapEnd or ZoneType.StageStart or ZoneType.Checkpoint => this.CourseBonus == 0,
		ZoneType.BonusStart or ZoneType.BonusEnd => this.CourseBonus > 0 && zone.Number == this.CourseBonus,
		_ => true,
	};

	// !startpos - where resets into a start zone put the player, per start zone (map start = (MapStart, 1)).
	// In memory only: a Player lives for one map / connection.
	internal Dictionary<(ZoneType Type, short Number), (VectorT Position, QAngleT Angles)> StartPositions { get; } = new();

	// Start zones where the anti-prehop cap applies. On staged_linear maps stage starts (2+) are part
	// of the run and may be bhopped through freely - only the map start (stage 1) and bonus starts count.
	internal bool IsInStartZone => this.TouchingTriggers.Values.Any(zone => zone.Type switch
	{
		ZoneType.MapStart or ZoneType.BonusStart => true,
		ZoneType.StageStart => !SurfTimer.CurrentMap.StagedLinear,
		_ => false
	});

	internal bool IsTouchingZone(ZoneType type, short number) => this.TouchingTriggers.Values.Any(zone =>
		zone.Type == type && zone.Number == number);

	// Any zone a run/stage/bonus starts from - the HUD's prespeed field shows live speed inside these
	internal bool IsTouchingAnyStartZone => this.TouchingTriggers.Values.Any(zone =>
		zone.Type is ZoneType.MapStart or ZoneType.StageStart or ZoneType.BonusStart);

	// Speed when last leaving a start zone (HUD prespeed field) - null until the first exit
	internal VectorT? LastPrespeed { get; set; }

	// Strafe sync of the current run: air ticks turning in the direction of the held strafe key
	internal int SyncGoodTicks { get; private set; }
	internal int SyncTotalTicks { get; private set; }
	internal float SyncPercent => this.SyncTotalTicks > 0 ? 100f * this.SyncGoodTicks / this.SyncTotalTicks : 0f;
	private float _lastYaw;

	// Sync counters when the current stage / checkpoint segment started - a segment's sync is the difference
	private int _segmentSyncGood;
	private int _segmentSyncTotal;

	/// <summary>
	/// Strafe sync since the current stage / checkpoint segment started (MarkSegmentStart).
	/// </summary>
	internal float SegmentSyncPercent
	{
		get
		{
			int total = this.SyncTotalTicks - _segmentSyncTotal;
			return total > 0 ? 100f * (this.SyncGoodTicks - _segmentSyncGood) / total : 0f;
		}
	}

	/// <summary>The sync counters (run and current segment) - savelocs take and restore them</summary>
	internal (int Good, int Total, int SegmentGood, int SegmentTotal) SyncState
	{
		get => (this.SyncGoodTicks, this.SyncTotalTicks, _segmentSyncGood, _segmentSyncTotal);
		set
		{
			this.SyncGoodTicks = value.Good;
			this.SyncTotalTicks = value.Total;
			_segmentSyncGood = value.SegmentGood;
			_segmentSyncTotal = value.SegmentTotal;
		}
	}

	internal void MarkSegmentStart()
	{
		_segmentSyncGood = this.SyncGoodTicks;
		_segmentSyncTotal = this.SyncTotalTicks;
	}

	// Runs started on this map not yet written to player_course_attempts, by (type, stage)
	internal Dictionary<(short Type, short Stage), (int Started, int Finished)> PendingAttempts { get; } = new();

	/// <summary>
	/// Counts a started run (type 0 map, 1 bonus, 2 stage) - written in batches (StatsService).
	/// Finished runs aren't counted here: every finish is stored in run_history.
	/// </summary>
	internal void CountAttempt(short type, short stage, bool finished)
	{
		if (finished || this.Timer.IsPracticeMode)
			return;

		PendingAttempts.TryGetValue((type, stage), out var counts);
		PendingAttempts[(type, stage)] = (counts.Started + 1, counts.Finished);
	}
	internal bool WasOnGroundLastTick { get; set; } = true;
	internal int GroundTicks { get; set; } = 0;
	internal int StartZoneJumpCount { get; set; } = 0;
	internal bool StartZoneSpeedCapActive { get; set; } = false;
	// Staying on the ground this long (~0.25s at 64 tick) is walking/prestrafing, not a bhop
	private const int StartZoneWalkResetTicks = 16;

	// Constructor
	internal Player(CCSPlayerController Controller, CCSPlayer_MovementServices MovementServices, PlayerProfile Profile)
	{
		this.Controller = Controller;
		this.MovementServices = MovementServices;

		this.Profile = Profile;
		this.Options = new PlayerOptions(Profile);

		this.Timer = new PlayerTimer();
		this.Stats = new PlayerStats();
		this.ReplayRecorder = new ReplayRecorder();

		this.HUD = new PlayerHud(this);
	}

	/// <summary>
	/// Checks if current player is spectating player
	/// </summary>
	public bool IsSpectating(CCSPlayerController p)
	{
		if (p == null || this.Controller == null || this.Controller.Team != CounterStrikeSharp.API.Modules.Utils.CsTeam.Spectator)
			return false;

		var observerServices = this.Controller.ObserverPawn.Value?.ObserverServices;
		if (observerServices == null || !p.IsValid)
			return false;

		return observerServices.ObserverTarget.Raw == p.PlayerPawn.Raw;
	}

	/// <summary>
	/// Own first-person legs per the hide-legs option: render alpha 254 hides them for the player
	/// themselves while everyone else still sees a normal model (255 shows them). Also puts back what
	/// the old !hideself changed (invisible weapons, no shadow).
	/// </summary>
	internal void ApplySelfVisibility()
	{
		var pawn = this.Controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;

		EnforceVisible(pawn, this.Options.HideLegs ? LegsHiddenAlpha : 255);

		if (pawn.ShadowStrength < 1f)
		{
			pawn.ShadowStrength = 1f;
			Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_flShadowStrength");
		}

		var weapons = pawn.WeaponServices?.MyWeapons;
		if (weapons == null)
			return;

		foreach (var handle in weapons)
		{
			var weapon = handle.Value;
			if (weapon != null && weapon.IsValid && weapon.Render.A != 255)
				SetRenderAlpha(weapon, 255);
		}
	}

	/// <summary>Render alpha that hides a player's own first-person legs only</summary>
	internal const int LegsHiddenAlpha = 254;

	/// <summary>
	/// Makes a pawn visible again if a map hid it (render mode, alpha, EF_NODRAW) - only writes (and
	/// networks) what's actually different. Returns true when something was changed.
	/// </summary>
	internal static bool EnforceVisible(CBaseModelEntity pawn, int alpha)
	{
		bool changed = false;

		if (pawn.RenderMode != RenderMode_t.kRenderNormal)
		{
			pawn.RenderMode = RenderMode_t.kRenderNormal;
			Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_nRenderMode");
			changed = true;
		}

		if (pawn.Render.A != alpha)
		{
			SetRenderAlpha(pawn, alpha);
			changed = true;
		}

		if ((pawn.Effects & EffectNoDraw) != 0)
		{
			pawn.Effects &= ~EffectNoDraw;
			Utilities.SetStateChanged(pawn, "CBaseEntity", "m_fEffects");
			changed = true;
		}

		return changed;
	}

	// EF_NODRAW - maps use it (AddOutput effects 32) to make players invisible
	private const uint EffectNoDraw = 0x20;

	private static void SetRenderAlpha(CBaseModelEntity entity, int alpha)
	{
		entity.Render = Color.FromArgb(alpha, entity.Render.R, entity.Render.G, entity.Render.B);
		Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
	}

	/// <summary>
	/// Removes landing slowdowns so surf drops (e.g. into stage starts) don't cost speed:
	/// the "hurt" velocity modifier (lowered on hard landings even with fall damage scaled to 0) and
	/// the stamina penalty (backs up sv_staminamax 0 in case a map cfg overrides it).
	/// Both are written only when actually set, and networked so client prediction agrees -
	/// otherwise the client still predicts the slowdown and rubber-bands.
	/// </summary>
	internal void TickRemoveLandingSlowdown()
	{
		var pawn = this.Controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;

		if (pawn.VelocityModifier < 1f)
		{
			pawn.VelocityModifier = 1f;
			Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
		}

		var movement = pawn.MovementServices?.As<CCSPlayer_MovementServices>();
		if (movement != null && movement.Stamina > 0f)
		{
			movement.Stamina = 0f;
			Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pMovementServices");
		}
	}

	internal void ResetSync()
	{
		this.SyncGoodTicks = 0;
		this.SyncTotalTicks = 0;
		MarkSegmentStart();
	}

	/// <summary>
	/// Strafe sync (see StrafeSync). Only counted while the timer runs, so it covers the current run
	/// and freezes when it ends; reset when a run starts.
	/// </summary>
	internal void TickSync()
	{
		var pawn = this.Controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;

		float yaw = pawn.EyeAngles.Y;
		float prevYaw = _lastYaw;
		_lastYaw = yaw;

		if (!this.Timer.IsRunning || pawn.MoveType is MoveType_t.MOVETYPE_LADDER or MoveType_t.MOVETYPE_NOCLIP)
			return;

		bool onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
		bool? inSync = StrafeSync.Evaluate(prevYaw, yaw, onGround, this.Controller.Buttons);
		if (inSync == null)
			return;

		this.SyncTotalTicks++;
		if (inSync.Value)
			this.SyncGoodTicks++;
	}

	/// <summary>
	/// Start-zone (map start or any stage start) anti-prehop, on horizontal speed:
	/// - the first jump is free, so ground prestrafe speed can be taken into it;
	/// - a second jump that follows a landing within StartZoneWalkResetTicks (a bhop) caps airborne
	///   speed at the start speed cap (map setting / Config.StartSpeedCap) while in the zone, until speed drops below the cap;
	/// - landing inside a start zone counts as the first hop, so speed carried in from the air
	///   can't be bhopped out of the zone;
	/// - staying on the ground for StartZoneWalkResetTicks (walking/prestrafing) resets it all.
	/// Ground movement is never restricted, and vertical speed is left alone (jump height intact).
	/// </summary>
	internal void TickStartZoneSpeedCap()
	{
		var pawn = this.Controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;

		bool isOnGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
		bool landed = !this.WasOnGroundLastTick && isOnGround;
		bool leftGround = this.WasOnGroundLastTick && !isOnGround;

		if (isOnGround)
		{
			this.GroundTicks++;
			if (this.GroundTicks >= StartZoneWalkResetTicks)
			{
				this.StartZoneJumpCount = 0;
				this.StartZoneSpeedCapActive = false;
			}
			else if (landed && this.IsInStartZone)
			{
				this.StartZoneJumpCount = Math.Max(this.StartZoneJumpCount, 1);
			}
		}
		else
		{
			this.GroundTicks = 0;
		}

		if (leftGround && this.IsInStartZone)
		{
			this.StartZoneJumpCount++;
			if (this.StartZoneJumpCount >= 2)
				this.StartZoneSpeedCapActive = true;
		}

		this.WasOnGroundLastTick = isOnGround;

		// The map's start_speed_cap setting, else timer_settings.json's default - 0 = no cap
		float cap = SurfTimer.CurrentMap?.StartSpeedCap ?? Config.StartSpeedCap;
		if (cap <= 0)
		{
			this.StartZoneSpeedCapActive = false;
			return;
		}

		VectorT vel = pawn.AbsVelocity.ToVector_t();
		float horizontalSpeed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);

		if (this.IsInStartZone && this.StartZoneSpeedCapActive && !isOnGround && horizontalSpeed > cap)
		{
			float scale = cap / horizontalSpeed;
			Extensions.Teleport(pawn, null, null, new VectorT(vel.X * scale, vel.Y * scale, vel.Z));
		}

		if (horizontalSpeed < cap)
		{
			this.StartZoneJumpCount = 0;
			this.StartZoneSpeedCapActive = false;
		}
	}

	/// <summary>
	/// Hard limit when leaving a run start (map setting exit_speed_limit): scales the horizontal speed
	/// down to the limit, keeping direction and vertical speed. velocity is the exit velocity the caller
	/// uses (prespeed, start velocities) - updated when capped. Returns true when it was capped.
	/// </summary>
	internal bool ClampExitSpeed(float limit, ref VectorT velocity, out float before)
	{
		before = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
		if (limit <= 0 || before <= limit)
			return false;

		var pawn = this.Controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return false;

		float scale = limit / before;
		velocity = new VectorT(velocity.X * scale, velocity.Y * scale, velocity.Z);
		Extensions.Teleport(pawn, null, null, velocity);
		return true;
	}

	// ---- Idle checker ----

	private int _lastActivityTick = Server.TickCount;
	private float _lastIdleX, _lastIdleY, _lastIdleZ, _lastIdlePitch, _lastIdleYaw;

	/// <summary>No movement, input or looking around for Config.IdleThresholdSeconds</summary>
	internal bool IsIdle { get; private set; }

	/// <summary>
	/// Idle checker (every tick, alive players): after Config.IdleThresholdSeconds without movement, input
	/// or view changes the replay recording is stopped and its frames freed. The timer keeps running - a
	/// run that was idle saves without a replay. In a start zone recording restarts as soon as the
	/// player is active again.
	/// </summary>
	internal void TickIdle()
	{
		var pawn = this.Controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
			return;

		var pos = pawn.AbsOrigin;
		var angles = pawn.EyeAngles;
		float dx = pos.X - _lastIdleX, dy = pos.Y - _lastIdleY, dz = pos.Z - _lastIdleZ;
		bool moved = dx * dx + dy * dy + dz * dz > 1f;
		bool turned = MathF.Abs(angles.X - _lastIdlePitch) > 0.1f || MathF.Abs(angles.Y - _lastIdleYaw) > 0.1f;
		bool input = this.Controller.Buttons != 0;
		(_lastIdleX, _lastIdleY, _lastIdleZ, _lastIdlePitch, _lastIdleYaw) = (pos.X, pos.Y, pos.Z, angles.X, angles.Y);

		int now = Server.TickCount;
		var recorder = this.ReplayRecorder;

		if (moved || turned || input)
		{
			_lastActivityTick = now;
			if (this.IsIdle)
			{
				this.IsIdle = false;
				// Back from idle before a run - the next run gets a replay
				if (!this.Timer.IsRunning)
					RestartRecordingInStartZone();
			}
		}

		// A run whose recording was dropped is over once its timer stops - record again from the next start
		if (recorder.DroppedForRun && !this.Timer.IsRunning)
		{
			recorder.ClearDropped();
			RestartRecordingInStartZone();
		}

		int threshold = Config.IdleThresholdSeconds;
		if (threshold <= 0 || this.IsIdle || now - _lastActivityTick < threshold * 64)
			return;

		// A scheduled save still trims these frames - try again next tick
		if (recorder.IsSaving)
			return;

		this.IsIdle = true;
		if (this.Timer.IsRunning)
			recorder.DropForRun();
		else
			recorder.StopAndFree();
	}

	// A start zone entry while a save still trims the recorder's frames - its recording starts once the
	// save is done (TickPendingStartRecording)
	private ZoneType? _pendingStartRecording;

	/// <summary>
	/// Starts the replay recording of the run that starts from this start zone - right away, or once a
	/// pending save no longer needs the current frames.
	/// </summary>
	internal void BeginStartZoneRecording(ZoneType startZone)
	{
		if (this.ReplayRecorder.IsSaving)
		{
			_pendingStartRecording = startZone;
			return;
		}

		_pendingStartRecording = null;
		var recorder = this.ReplayRecorder;
		recorder.Reset();
		recorder.Start();
		recorder.CurrentSituation = ReplayFrameSituation.START_ZONE_ENTER;
		(startZone == ZoneType.BonusStart ? recorder.BonusSituations : recorder.MapSituations).Add(recorder.Frames.Count);
	}

	/// <summary>
	/// Every tick (alive): a start zone recording that waited for a save starts now - or, when the player
	/// already left the zone and runs, that run's recording still holds the old frames and gets no replay.
	/// </summary>
	internal void TickPendingStartRecording()
	{
		if (_pendingStartRecording is not ZoneType startZone || this.ReplayRecorder.IsSaving)
			return;

		_pendingStartRecording = null;
		bool inZone = this.TouchingTriggers.Values.Any(zone => zone.Type == startZone);
		if (inZone && !this.Timer.IsRunning)
			BeginStartZoneRecording(startZone);
		else if (this.Timer.IsRunning)
			this.ReplayRecorder.DropForRun();
	}

	private void RestartRecordingInStartZone()
	{
		if (!this.IsTouchingAnyStartZone || this.ReplayRecorder.IsSaving)
			return;

		this.ReplayRecorder.Reset();
		this.ReplayRecorder.Start();
	}
}
