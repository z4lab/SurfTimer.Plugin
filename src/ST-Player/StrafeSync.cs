using CounterStrikeSharp.API;

namespace SurfTimer;

/// <summary>
/// Strafe sync: of the ticks in the air where the player turns, how many turn the same way as the
/// strafe key held (left with only A, right with only D). Shared by live players and replays so
/// both show the same number.
/// </summary>
internal static class StrafeSync
{
	/// <summary>
	/// Evaluates one tick. null = the tick doesn't count (on the ground or not turning),
	/// true = in sync, false = out of sync.
	/// </summary>
	internal static bool? Evaluate(float prevYaw, float yaw, bool onGround, PlayerButtons buttons)
	{
		if (onGround)
			return null;

		float delta = yaw - prevYaw;
		// Wrap across the -180/180 seam
		if (delta > 180f)
			delta -= 360f;
		else if (delta < -180f)
			delta += 360f;

		if (MathF.Abs(delta) < 0.001f)
			return null;

		bool left = buttons.HasFlag(PlayerButtons.Moveleft) && !buttons.HasFlag(PlayerButtons.Moveright);
		bool right = buttons.HasFlag(PlayerButtons.Moveright) && !buttons.HasFlag(PlayerButtons.Moveleft);

		return (delta > 0 && left) || (delta < 0 && right); // Positive yaw delta = turning left
	}
}
