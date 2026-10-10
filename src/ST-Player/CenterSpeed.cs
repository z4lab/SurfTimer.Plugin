using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>A player's centre speed text and what it was built for (CenterSpeed.cs)</summary>
internal sealed class CenterSpeedView
{
	internal CPointWorldText? Text { get; set; }
	internal string Shown { get; set; } = "";
	internal int Size { get; set; }
	internal int Offset { get; set; }
	/// <summary>The pawn it hangs on - a new pawn (respawn) gets a new text</summary>
	internal nint Pawn { get; set; }
}

/// <summary>
/// Options - HUD - Center speed: the player's speed in the middle of the screen, at the crosshair (moved up / down in
/// steps), in five sizes and a monospace font (timer_settings.json center_speed_font). Without the custom HUD addon: a
/// point_worldtext a few units in front of the eyes. It hangs on the pawn (so it moves with the player smoothly, however
/// fast) and is put back in front of the eyes every tick for where they look - CS2 has no server view models any more
/// (July 2025), which used to keep such text fixed on screen, so it trails the view a little on fast mouse movement.
/// Only the player themselves receives it.
/// </summary>
public partial class SurfTimer
{
	private const int CenterSpeedUpdateTicks = 4;
	/// <summary>Font size per size step (1-5)</summary>
	private static readonly int[] CenterSpeedFontSizes = [24, 32, 42, 54, 68];
	/// <summary>How far in front of the eyes the text floats</summary>
	private const float CenterSpeedDistance = 7f;
	private const float CenterSpeedUnitsPerPx = 0.0085f;
	/// <summary>World units per position step at CenterSpeedDistance (about 1% of the screen height)</summary>
	private const float CenterSpeedStep = 0.08f;

	private void TickCenterSpeed(Player player)
	{
		var controller = player.Controller;
		var view = player.CenterSpeedView;
		var options = player.Options;
		var pawn = controller.PawnIsAlive ? controller.PlayerPawn.Value : null;
		if (options.CenterSpeed <= 0 || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || player.IsPlacementPending)
		{
			RemoveCenterSpeed(view);
			return;
		}

		float scale = FovScale(controller);
		bool rebuild = view.Text == null || !view.Text.IsValid || view.Size != options.CenterSpeed || view.Pawn != pawn.Handle;
		if (rebuild)
		{
			RemoveCenterSpeed(view);
			string first = SpeedText(pawn, options);
			view.Text = CreateCenterSpeed(pawn, first, options.CenterSpeed, scale);
			view.Shown = first;
			view.Size = options.CenterSpeed;
			view.Pawn = pawn.Handle;
			if (view.Text == null)
				return;
		}

		// Every tick: in front of the eyes, where they look now
		var (position, angles) = CenterSpeedPlacement(pawn, options.CenterSpeedOffset, scale);
		view.Text!.Teleport(position, angles, null);

		if ((Server.TickCount + controller.Slot) % CenterSpeedUpdateTicks != 0)
			return;
		string text = SpeedText(pawn, options);
		if (text != view.Shown)
		{
			view.Text.MessageText = text;
			Utilities.SetStateChanged(view.Text, "CPointWorldText", "m_messageText");
			view.Shown = text;
		}
	}

	private static string SpeedText(CCSPlayerPawn pawn, PlayerOptions options) =>
		$"{Extensions.Speed(pawn.AbsVelocity.ToVector_t(), options.SpeedAxes):0}";

	/// <summary>The same share of the screen at any field of view</summary>
	private static float FovScale(CCSPlayerController controller)
	{
		float fov = controller.DesiredFOV == 0 ? 90 : controller.DesiredFOV;
		return fov == 90 ? 1f : MathF.Tan(fov / 2 * MathF.PI / 180) / MathF.Tan(45 * MathF.PI / 180);
	}

	private static void RemoveCenterSpeed(CenterSpeedView view)
	{
		if (view.Text != null && view.Text.IsValid)
			view.Text.Remove();
		view.Text = null;
		view.Shown = "";
	}

	private static CPointWorldText? CreateCenterSpeed(CCSPlayerPawn pawn, string text, int size, float scale)
	{
		var entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
		if (entity == null)
			return null;

		entity.MessageText = text;
		entity.Enabled = true;
		entity.FontSize = CenterSpeedFontSizes[Math.Clamp(size, 1, CenterSpeedFontSizes.Length) - 1] * scale;
		entity.FontName = Config.CenterSpeedFont;
		entity.Fullbright = true;
		entity.Color = Color.White;
		entity.WorldUnitsPerPx = CenterSpeedUnitsPerPx;
		entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
		entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
		entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
		entity.RenderMode = RenderMode_t.kRenderNormal;
		entity.DispatchSpawn();

		// Hangs on the pawn: moves with the player between ticks (the client smooths the pawn's movement)
		entity.AcceptInput("SetParent", pawn, null, "!activator");
		return entity;
	}

	// Reused every tick (main thread only) - a new Vector / QAngle allocates native memory each time
	private static readonly Vector _centerForward = new(), _centerRight = new(), _centerUp = new(), _centerPosition = new();
	private static readonly QAngle _centerAngles = new();

	/// <summary>In front of the eyes, raised / lowered by the offset, facing the player</summary>
	private static (Vector Position, QAngle Angles) CenterSpeedPlacement(CCSPlayerPawn pawn, int offset, float scale)
	{
		var eyeAngles = pawn.EyeAngles;
		NativeAPI.AngleVectors(eyeAngles.Handle, _centerForward.Handle, _centerRight.Handle, _centerUp.Handle);
		var origin = pawn.AbsOrigin!;
		float height = offset * CenterSpeedStep * scale;
		_centerPosition.X = origin.X + _centerForward.X * CenterSpeedDistance + _centerUp.X * height;
		_centerPosition.Y = origin.Y + _centerForward.Y * CenterSpeedDistance + _centerUp.Y * height;
		_centerPosition.Z = origin.Z + pawn.ViewOffset.Z + _centerForward.Z * CenterSpeedDistance + _centerUp.Z * height;
		_centerAngles.X = 0;
		_centerAngles.Y = eyeAngles.Y + 270;
		_centerAngles.Z = 90 - eyeAngles.X;
		return (_centerPosition, _centerAngles);
	}

	/// <summary>CheckTransmit: every centre speed text goes only to its owner</summary>
	private void FilterCenterSpeedTransmit(CCheckTransmitInfo info, CCSPlayerController viewer)
	{
		foreach (var owner in playerList.Values)
		{
			var text = owner.CenterSpeedView.Text;
			if (text != null && text.IsValid && !ReferenceEquals(owner.Controller, viewer) && owner.Controller.Slot != viewer.Slot)
				info.TransmitEntities.Remove(text.Index);
		}
	}

	private bool AnyCenterSpeed => playerList.Values.Any(p => p.CenterSpeedView.Text != null);
}
