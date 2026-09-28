using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// Text input for the popup menus: the next chat message of a player is taken as the value (and not
/// shown to anyone). The popup is closed while waiting, so the chat key works (cursor mode would take
/// the keyboard). !cancel aborts; prompts run out after a minute.
/// </summary>
internal static class ChatPrompt
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

	/// <param name="OnValue">Gets the typed text - returns an error message to ask again, or null when done</param>
	/// <param name="OnCancel">Runs on !cancel (not on timeout / disconnect)</param>
	private sealed record Pending(string Text, Func<CCSPlayerController, string, string?> OnValue, Action<CCSPlayerController>? OnCancel, DateTime Expires);

	// By UserId - main thread only
	private static readonly Dictionary<int, Pending> _pending = new();

	internal static void Ask(Player player, string text, Func<CCSPlayerController, string, string?> onValue, Action<CCSPlayerController>? onCancel = null)
	{
		var controller = player.Controller;
		if (!controller.IsValid || controller.UserId is not int userId)
			return;

		player.HUD.CloseMenu();
		_pending[userId] = new Pending(text, onValue, onCancel, DateTime.UtcNow + Timeout);
		controller.PrintToChat($"{Config.PluginPrefix} {ChatColors.Yellow}{text}{ChatColors.Default} {LocalizationService.LocalizerNonNull["prompt_cancel_hint"]}");
	}

	internal static bool IsWaiting(CCSPlayerController controller) =>
		controller.UserId is int userId && _pending.TryGetValue(userId, out var pending) && pending.Expires > DateTime.UtcNow;

	internal static void Forget(int userId) => _pending.Remove(userId);

	internal static void ForgetAll() => _pending.Clear();

	/// <summary>
	/// say / say_team listener - swallows the message of a player with an open prompt.
	/// </summary>
	internal static HookResult OnSay(CCSPlayerController? player, CommandInfo info)
	{
		if (player == null || !player.IsValid || player.UserId is not int userId || !_pending.TryGetValue(userId, out var pending))
			return HookResult.Continue;

		if (pending.Expires <= DateTime.UtcNow)
		{
			_pending.Remove(userId);
			return HookResult.Continue;
		}

		string text = info.GetArg(1).Trim();
		if (text.Length == 0)
			return HookResult.Handled;

		if (text.Equals("!cancel", StringComparison.OrdinalIgnoreCase) || text.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
		{
			_pending.Remove(userId);
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["prompt_cancelled"]}");
			pending.OnCancel?.Invoke(player);
			return HookResult.Handled;
		}

		// Removed first: the callback may ask again (e.g. a second value)
		_pending.Remove(userId);
		string? error = pending.OnValue(player, text);
		if (error != null)
		{
			_pending[userId] = pending with { Expires = DateTime.UtcNow + Timeout };
			player.PrintToChat($"{Config.PluginPrefix} {ChatColors.Red}{error}{ChatColors.Default} {LocalizationService.LocalizerNonNull["prompt_cancel_hint"]}");
		}
		return HookResult.Handled;
	}

	/// <summary>
	/// Registers the say listeners - called from Load.
	/// </summary>
	internal static void Register(BasePlugin plugin)
	{
		plugin.AddCommandListener("say", OnSay, HookMode.Pre);
		plugin.AddCommandListener("say_team", OnSay, HookMode.Pre);
	}
}
