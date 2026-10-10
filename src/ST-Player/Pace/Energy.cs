using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace SurfTimer;

/// <summary>sv_gravity - read once a second (maps can change it), 800 by default</summary>
internal static class Gravity
{
	private static float _value = 800f;
	private static int _nextReadTick;

	internal static float Current
	{
		get
		{
			if (Server.TickCount >= _nextReadTick)
			{
				_nextReadTick = Server.TickCount + 64;
				float value = ConVar.Find("sv_gravity")?.GetPrimitiveValue<float>() ?? 0f;
				if (value > 0f)
					_value = value;
			}
			return _value;
		}
	}
}

/// <summary>
/// A runner's mechanical energy as Momentum Mod shows it: the height their speed could carry them to, plus how high above
/// the run's start they are - |v|² / (2 g) + (z - start z), in units. It only changes by air strafing (up) and by ramp
/// hits, collisions and friction (down), so it shows how clean the surf is.
/// </summary>
internal sealed class EnergyMeter
{
	/// <summary>The current energy (units of height)</summary>
	internal float Value { get; private set; }
	private float _baseZ;
	private bool _wasRunning;
	private int _lastTicks;

	/// <summary>Every tick while alive</summary>
	internal void Tick(Player player)
	{
		var pawn = player.Controller.PlayerPawn.Value;
		var origin = pawn?.AbsOrigin;
		if (pawn == null || !pawn.IsValid || origin == null)
			return;

		var timer = player.Timer;
		// Measured from where the run started (the start zone left) - or, not running, the last ground stood on
		if (timer.IsRunning && (!_wasRunning || timer.Ticks < _lastTicks))
			_baseZ = origin.Z;
		else if (!timer.IsRunning && (pawn.Flags & (uint)CounterStrikeSharp.API.Modules.Utils.PlayerFlags.FL_ONGROUND) != 0)
			_baseZ = origin.Z;
		_wasRunning = timer.IsRunning;
		_lastTicks = timer.Ticks;

		var velocity = pawn.AbsVelocity;
		float speedSquared = velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z;
		Value = speedSquared / (2f * Gravity.Current) + (origin.Z - _baseZ);
	}
}
