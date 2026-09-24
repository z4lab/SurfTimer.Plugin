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

	// Anti-prehop/bhop state (map start zone + every stage start zone). Deliberately not on
	// PlayerTimer - Timer.Reset() fires on every start-zone entry, which would wrongly clear this;
	// this state must only clear when velocity actually drops below the cap.
	internal bool IsInStartZone { get; set; } = false;
	internal bool WasOnGroundLastTick { get; set; } = true;
	internal int StartZoneJumpCount { get; set; } = 0;
	internal bool StartZoneSpeedCapActive { get; set; } = false;
	private const float StartZoneSpeedCap = 260f;

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
	/// Caps airborne velocity to StartZoneSpeedCap after a second consecutive jump inside a start
	/// zone (map start or any stage start) without the player's velocity dropping below the cap in
	/// between - landing alone does not lift the cap, only an actual sub-cap velocity reading does.
	/// Ground movement (including ground-based speed-gain techniques) is never restricted, and
	/// leaving/re-entering a start zone does not reset this state either.
	/// </summary>
	internal void TickStartZoneSpeedCap()
	{
		var pawn = this.Controller.PlayerPawn.Value;
		if (pawn == null || !pawn.IsValid)
			return;

		bool isOnGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;

		if (this.IsInStartZone && this.WasOnGroundLastTick && !isOnGround)
		{
			this.StartZoneJumpCount++;
			if (this.StartZoneJumpCount >= 2)
				this.StartZoneSpeedCapActive = true;
		}

		this.WasOnGroundLastTick = isOnGround;

		VectorT vel = pawn.AbsVelocity.ToVector_t();
		float speed = vel.velMag();

		if (this.IsInStartZone && this.StartZoneSpeedCapActive && !isOnGround && speed > StartZoneSpeedCap)
		{
			VectorT clamped = vel * (StartZoneSpeedCap / speed);
			Extensions.Teleport(pawn, null, null, clamped);
		}

		// Only way to lift the cap / reset the jump count - not landing, not leaving the zone
		if (speed < StartZoneSpeedCap)
		{
			this.StartZoneJumpCount = 0;
			this.StartZoneSpeedCapActive = false;
		}
	}
}
