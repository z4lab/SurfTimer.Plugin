using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

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
	public List<SavelocFrame> SavedLocations { get; set; }
	public int CurrentSavedLocation { get; set; }

	// Player information
	public PlayerProfile Profile { get; set; }

	// !repeat - send the player back to the start of each stage they finish (off on join)
	internal bool IsRepeatMode { get; set; } = false;

	// Anti-prehop/bhop state (map start zone + every stage start zone). Deliberately not on
	// PlayerTimer - Timer.Reset() fires on every start-zone entry, which would wrongly clear this;
	// this state must only clear when velocity actually drops below the cap.
	// Index of the start-zone trigger the player is inside (0 = none). Tracks the entity rather
	// than a bool so a late EndTouch from the zone they teleported out of (e.g. !r from a stage
	// start) can't clear the zone they teleported into.
	internal uint CurrentStartZoneIndex { get; set; } = 0;
	internal bool IsInStartZone => this.CurrentStartZoneIndex != 0;
	internal bool WasOnGroundLastTick { get; set; } = true;
	internal int GroundTicks { get; set; } = 0;
	internal int StartZoneJumpCount { get; set; } = 0;
	internal bool StartZoneSpeedCapActive { get; set; } = false;
	private const float StartZoneSpeedCap = 260f;
	// Staying on the ground this long (~0.25s at 64 tick) is walking/prestrafing, not a bhop
	private const int StartZoneWalkResetTicks = 16;

	// Constructor
	internal Player(CCSPlayerController Controller, CCSPlayer_MovementServices MovementServices, PlayerProfile Profile)
	{
		this.Controller = Controller;
		this.MovementServices = MovementServices;

		this.Profile = Profile;

		this.Timer = new PlayerTimer();
		this.Stats = new PlayerStats();
		this.ReplayRecorder = new ReplayRecorder();
		this.SavedLocations = new List<SavelocFrame>();
		CurrentSavedLocation = 0;

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

	/// <summary>
	/// Start-zone (map start or any stage start) anti-prehop, on horizontal speed:
	/// - the first jump is free, so ground prestrafe speed can be taken into it;
	/// - a second jump that follows a landing within StartZoneWalkResetTicks (a bhop) caps airborne
	///   speed at StartZoneSpeedCap while in the zone, until speed drops below the cap;
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

		VectorT vel = pawn.AbsVelocity.ToVector_t();
		float horizontalSpeed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);

		if (this.IsInStartZone && this.StartZoneSpeedCapActive && !isOnGround && horizontalSpeed > StartZoneSpeedCap)
		{
			float scale = StartZoneSpeedCap / horizontalSpeed;
			Extensions.Teleport(pawn, null, null, new VectorT(vel.X * scale, vel.Y * scale, vel.Z));
		}

		if (horizontalSpeed < StartZoneSpeedCap)
		{
			this.StartZoneJumpCount = 0;
			this.StartZoneSpeedCapActive = false;
		}
	}
}
