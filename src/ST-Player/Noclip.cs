using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// !noclip: flying for everyone. The first one turns the timer off (run reset, replay recording stopped, no zones,
/// no savelocs) until !r / !rs, which also end noclip. A second !noclip lands where the player is - the timer stays off.
/// Map teleports in noclip follow the player's option (Options - Gameplay - Noclip teleports), the zone editor's too:
/// normal, set back (a jump further than noclip flies that the plugin didn't make is undone), or disabled
/// (trigger_teleport doesn't touch them - CS2Fixes' gamedata gives the offsets; without it: set back).
/// </summary>
public partial class SurfTimer
{
	// More than noclip flies in one tick (sv_noclipspeed 5 = ~1250 u/s = ~20 units per tick)
	private const float NoclipTeleportJump = 200f;

	[ConsoleCommand("css_noclip", "Noclip on / off - turns your timer off until !r")]
	[ConsoleCommand("css_nc", "Noclip on / off - turns your timer off until !r")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void NoclipCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var p))
			return;

		if (_zoneEditor != null && ReferenceEquals(_zoneEditor.Editor, p))
		{
			Reply(player, "noclip_editor");
			return;
		}
		var pawn = player.PlayerPawn.Value;
		if (!player.PawnIsAlive || pawn == null || !pawn.IsValid || p.IsPlacementPending)
		{
			Reply(player, "noclip_not_alive");
			return;
		}

		if (p.Noclip)
		{
			p.Noclip = false;
			p.NoclipLastPosition = null;
			SetMoveType(pawn, MoveType_t.MOVETYPE_WALK);
			Reply(player, "noclip_off");
			return;
		}

		if (!p.TimerDisabled)
		{
			p.TimerDisabled = true;
			p.Timer.Reset();
			p.Stats.ThisRun.Checkpoints.Clear();
			p.ReplayRecorder.Stop();
			p.TouchingTriggers.Clear();
		}
		p.Noclip = true;
		p.NoclipLastPosition = null;
		SetMoveType(pawn, MoveType_t.MOVETYPE_NOCLIP);
		Reply(player, "noclip_on");
		if (p.Options.NoclipTeleports == NoclipTeleports.Disabled)
			EnsureTeleportTriggerHook();
	}

	private static void Reply(CCSPlayerController player, string key) =>
		player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull[key]}");

	/// <summary>
	/// !r / !rs: noclip and the turned-off timer end - the player is a normal runner again from where they're put.
	/// </summary>
	private static void EndNoclip(Player p)
	{
		if (!p.Noclip && !p.TimerDisabled)
			return;
		if (p.Noclip && p.Controller.PlayerPawn.Value is { IsValid: true } pawn)
			SetMoveType(pawn, MoveType_t.MOVETYPE_WALK);
		p.Noclip = false;
		p.TimerDisabled = false;
		p.NoclipLastPosition = null;
	}

	/// <summary>
	/// Every tick: keeps !noclip players flying (maps reset move types, dying ends it) and handles map teleports of
	/// everyone in noclip - !noclip and the zone editor - by their option.
	/// </summary>
	private void TickNoclip()
	{
		int now = Server.TickCount;
		int ignoring = 0;
		foreach (var p in playerList.Values)
		{
			var controller = p.Controller;
			bool editing = _zoneEditor != null && ReferenceEquals(_zoneEditor.Editor, p);
			if (!controller.IsValid || (!p.Noclip && !editing))
				continue;
			if (p.Options.NoclipTeleports == NoclipTeleports.Disabled)
				ignoring++;

			var pawn = controller.PawnIsAlive ? controller.PlayerPawn.Value : null;
			if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
			{
				// Dead / spectating: noclip ends, the timer stays off until !r
				p.Noclip = false;
				p.NoclipLastPosition = null;
				continue;
			}

			if (pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP)
				SetMoveType(pawn, MoveType_t.MOVETYPE_NOCLIP);

			var position = pawn.AbsOrigin.ToVector_t();
			if (p.Options.NoclipTeleports != NoclipTeleports.Normal && p.NoclipLastPosition is VectorT last
				&& now > p.OwnTeleportUntilTick && (position - last).Length() > NoclipTeleportJump)
			{
				Extensions.Teleport(pawn, last, null, new VectorT(0, 0, 0));
				continue; // NoclipLastPosition stays where the player was
			}
			p.NoclipLastPosition = position;
		}
		_teleportIgnorers = ignoring;
	}

	/// <summary>A teleport by the plugin (zones, menus) - not undone as a map teleport in noclip</summary>
	internal static void MarkOwnTeleport(Player p, VectorT position)
	{
		p.OwnTeleportUntilTick = Server.TickCount + 4;
		p.NoclipLastPosition = position;
	}

	/// <summary>Whether map teleports must not touch this pawn's player: in noclip with Noclip teleports disabled</summary>
	private bool IgnoresTeleports(CBaseEntity entity)
	{
		if (entity.DesignerName != "player")
			return false;
		var pawnController = new CCSPlayerPawn(entity.Handle).Controller.Value;
		if (pawnController == null || !pawnController.IsValid)
			return false;
		var controller = new CCSPlayerController(pawnController.Handle);
		if (!playerList.TryGetValue(controller.UserId ?? 0, out var p))
			return false;
		bool editing = _zoneEditor != null && ReferenceEquals(_zoneEditor.Editor, p);
		return (p.Noclip || editing) && p.Options.NoclipTeleports == NoclipTeleports.Disabled;
	}

	// ---- trigger_teleport hook (Noclip teleports: disabled) ----

	private static readonly string[] TeleportTouchFunctions = ["CBaseEntity::StartTouch", "CBaseEntity::Touch", "CBaseEntity::EndTouch"];
	private readonly List<VirtualFunctionVoid<CBaseEntity, CBaseEntity>> _teleportTouchHooks = new();
	private bool _teleportHookTried;
	/// <summary>Players in noclip with map teleports disabled (last tick) - none: the hook returns right away</summary>
	private int _teleportIgnorers;

	/// <summary>
	/// Hooks StartTouch / Touch / EndTouch of trigger_teleport (once - the functions are the same on every map), like
	/// CS2Fixes does for players banned from map items. The vtable offsets come from CS2Fixes' gamedata, kept up to date
	/// with CS2 updates there. Without it (or no trigger_teleport on the map yet) disabled works like set back.
	/// </summary>
	internal void EnsureTeleportTriggerHook()
	{
		if (_teleportHookTried || _teleportTouchHooks.Count > 0)
			return;

		var trigger = Utilities.FindAllEntitiesByDesignerName<CBaseTrigger>("trigger_teleport").FirstOrDefault(t => t.IsValid);
		if (trigger == null)
			return; // Tried again on the next !noclip / option change - maybe on another map
		_teleportHookTried = true;

		var offsets = Cs2FixesOffsets(TeleportTouchFunctions);
		if (offsets == null)
		{
			_logger.LogWarning("[CS2 Surf] Noclip teleports 'disabled' needs CS2Fixes' gamedata (addons/cs2fixes/gamedata/cs2fixes.jsonc) - map teleports are set back instead");
			return;
		}

		try
		{
			foreach (int offset in offsets)
			{
				var function = new VirtualFunctionVoid<CBaseEntity, CBaseEntity>(trigger, offset);
				function.Hook(OnTeleportTriggerTouch, HookMode.Pre);
				_teleportTouchHooks.Add(function);
			}
			_logger.LogInformation("[CS2 Surf] trigger_teleport hooked - Noclip teleports 'disabled' ignores map teleports");
		}
		catch (Exception ex)
		{
			RemoveTeleportTriggerHook();
			_logger.LogWarning(ex, "[CS2 Surf] Hooking trigger_teleport failed - Noclip teleports 'disabled' sets back instead");
		}
	}

	private HookResult OnTeleportTriggerTouch(DynamicHook hook)
	{
		if (_teleportIgnorers == 0)
			return HookResult.Continue;
		// The functions can be shared by other triggers (not overridden by trigger_teleport)
		var trigger = hook.GetParam<CBaseEntity>(0);
		var other = hook.GetParam<CBaseEntity>(1);
		if (trigger == null || other == null || trigger.DesignerName != "trigger_teleport")
			return HookResult.Continue;
		return IgnoresTeleports(other) ? HookResult.Handled : HookResult.Continue;
	}

	private void RemoveTeleportTriggerHook()
	{
		foreach (var function in _teleportTouchHooks)
		{
			try
			{
				function.Unhook(OnTeleportTriggerTouch, HookMode.Pre);
			}
			catch
			{
				// Unloading anyway
			}
		}
		_teleportTouchHooks.Clear();
	}

	/// <summary>The functions' vtable offsets for this OS from CS2Fixes' gamedata - null when any is missing</summary>
	private List<int>? Cs2FixesOffsets(IEnumerable<string> names)
	{
		string path = Path.Combine(Server.GameDirectory, "csgo", "addons", "cs2fixes", "gamedata", "cs2fixes.jsonc");
		if (!File.Exists(path))
			return null;

		try
		{
			using var document = JsonDocument.Parse(File.ReadAllText(path),
				new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
			string platform = OperatingSystem.IsWindows() ? "windows" : "linux";
			var offsets = new List<int>();
			foreach (string name in names)
			{
				if (FindProperty(document.RootElement, name) is not { } entry
					|| !entry.TryGetProperty(platform, out var value) || !value.TryGetInt32(out int offset))
					return null;
				offsets.Add(offset);
			}
			return offsets;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "[CS2 Surf] Reading CS2Fixes' gamedata failed");
			return null;
		}
	}

	/// <summary>The first property of that name anywhere in the document</summary>
	private static JsonElement? FindProperty(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object)
			return null;
		foreach (var property in element.EnumerateObject())
		{
			if (property.Name == name)
				return property.Value;
			if (FindProperty(property.Value, name) is { } nested)
				return nested;
		}
		return null;
	}
}
