using System.Text;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Player chat is printed by the plugin instead of the engine: "[#rank] ~ name: message" with rank and
/// role colors (chat_settings.json). Valid commands (!cmd, /cmd of any plugin) are never posted.
/// Order: admin chat prompts, commands, gags (CS2-SimpleAdmin), anti-spam, then the formatted line.
/// </summary>
public partial class SurfTimer
{
	private sealed class SpamState
	{
		internal DateTime LastMessage;
		internal string LastText = "";
		internal DateTime LastTextAt;
	}

	// By UserId - main thread only
	private readonly Dictionary<int, SpamState> _chatSpam = new();

	private static readonly Regex FormatToken = new(@"\{(\w+)\}", RegexOptions.Compiled);

	private void RegisterChatProcessor()
	{
		string? error = ChatSettings.Load();
		if (error != null)
			_logger.LogError("[Chat] chat_settings.json couldn't be read ({Error}) - using the defaults", error);

		CommandRegistry.Init(this, _logger);
		AddCommandListener("say", (player, info) => OnPlayerChat(player, info, team: false), HookMode.Pre);
		AddCommandListener("say_team", (player, info) => OnPlayerChat(player, info, team: true), HookMode.Pre);
	}

	private HookResult OnPlayerChat(CCSPlayerController? player, CommandInfo info, bool team)
	{
		// Server console / map say - MapCommandFilter's business
		if (player == null || !player.IsValid || player.IsBot)
			return HookResult.Continue;

		// An open admin panel prompt takes the message first
		var prompt = ChatPrompt.OnSay(player, info);
		if (prompt != HookResult.Continue)
			return prompt;

		var settings = ChatSettings.Current;
		if (!settings.Enabled)
			return HookResult.Continue;

		string text = Sanitize(MessageText(info));
		if (text.Length == 0)
			return HookResult.Handled;

		// Commands: never posted. "/cmd" is CSS's silent trigger - it runs and isn't shown. "!cmd" is sent
		// again as "/cmd" so it runs once, in chat context, without being posted.
		var (trigger, silent) = MatchChatTrigger(text);
		if (trigger != null)
		{
			string rest = text[trigger.Length..];
			string name = rest.Split(' ', 2)[0];
			if (CommandRegistry.Exists(name))
			{
				if (silent)
					return HookResult.Continue;

				// Commands are registered in lower case - "!R" runs css_r
				rest = name.ToLowerInvariant() + rest[name.Length..];
				string silentTrigger = CoreConfig.SilentChatTrigger.FirstOrDefault() ?? "/";
				player.ExecuteClientCommandFromServer($"{(team ? "say_team" : "say")} \"{silentTrigger}{rest.Replace('"', '\'')}\"");
				return HookResult.Handled;
			}
		}

		if (SimpleAdminGags.IsGagged(player))
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["chat_gagged"]}");
			return HookResult.Handled;
		}

		if (IsSpam(player, text, settings))
			return HookResult.Handled;

		SendChatLine(player, text, team, settings);
		return HookResult.Handled;
	}

	/// <summary>The typed text - `say "text"` from clients, or several words from the console</summary>
	private static string MessageText(CommandInfo info)
	{
		string text = info.ArgString.Trim();
		if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
			text = text[1..^1];
		return text.Trim();
	}

	/// <summary>
	/// Removes control characters - chat color codes, newlines - so nobody can color or break lines.
	/// </summary>
	internal static string Sanitize(string text)
	{
		var builder = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (!char.IsControl(c))
				builder.Append(c);
		}
		return builder.ToString().Trim();
	}

	/// <summary>The chat trigger the text starts with (longest first), and whether it's a silent one</summary>
	private static (string? Trigger, bool Silent) MatchChatTrigger(string text)
	{
		var triggers = CoreConfig.SilentChatTrigger.Select(t => (Trigger: t, Silent: true))
			.Concat(CoreConfig.PublicChatTrigger.Select(t => (Trigger: t, Silent: false)))
			.Where(t => !string.IsNullOrEmpty(t.Trigger))
			.OrderByDescending(t => t.Trigger.Length);

		foreach (var (trigger, silent) in triggers)
		{
			if (text.StartsWith(trigger, StringComparison.Ordinal) && text.Length > trigger.Length)
				return (trigger, silent);
		}
		return (null, false);
	}

	private bool IsChatAdmin(CCSPlayerController player, ChatSettings settings) =>
		AdminManager.PlayerHasPermissions(player, settings.Flags.Root) || AdminManager.PlayerHasPermissions(player, settings.Flags.Admin);

	/// <summary>
	/// Minimum gap between messages and no repeats within the duplicate window - admins aren't limited.
	/// </summary>
	private bool IsSpam(CCSPlayerController player, string text, ChatSettings settings)
	{
		var antiSpam = settings.AntiSpam;
		if (!antiSpam.Enabled || IsChatAdmin(player, settings))
			return false;

		int userId = player.UserId ?? 0;
		if (!_chatSpam.TryGetValue(userId, out var state))
			_chatSpam[userId] = state = new SpamState();

		var now = DateTime.UtcNow;
		if ((now - state.LastMessage).TotalSeconds < antiSpam.MinGapSeconds)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["chat_slow_down"]}");
			return true;
		}
		if (text.Equals(state.LastText, StringComparison.OrdinalIgnoreCase) && (now - state.LastTextAt).TotalSeconds < antiSpam.DuplicateWindowSeconds)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["chat_duplicate"]}");
			return true;
		}

		state.LastMessage = now;
		state.LastText = text;
		state.LastTextAt = now;
		return false;
	}

	/// <summary>
	/// Builds the line from the format and sends it to everyone (or the sender's team), and logs it.
	/// </summary>
	private void SendChatLine(CCSPlayerController sender, string text, bool team, ChatSettings settings)
	{
		playerList.TryGetValue(sender.UserId ?? 0, out var player);
		int? rank = player?.Profile.ServerRank;

		string rankColorName = rank switch
		{
			1 => settings.RankColors.First,
			2 => settings.RankColors.Second,
			3 => settings.RankColors.Third,
			<= 10 => settings.RankColors.Top10,
			null => settings.RankColors.Unranked,
			_ => settings.RankColors.Other,
		};
		string rankText = $"{ChatSettings.Color(rankColorName)}[{(rank != null ? $"#{rank}" : "-")}]{ChatColors.Default}";

		string nameColorName = AdminManager.PlayerHasPermissions(sender, settings.Flags.Root) ? settings.NameColors.Root
			: AdminManager.PlayerHasPermissions(sender, settings.Flags.Admin) ? settings.NameColors.Admin
			: AdminManager.PlayerHasPermissions(sender, settings.Flags.Vip) ? settings.NameColors.Vip
			: settings.NameColors.Default;
		string name = Sanitize(sender.PlayerName);
		string nameText = $"{ChatSettings.Color(nameColorName, sender.Team)}{name}{ChatColors.Default}";

		var prefixes = new List<string>();
		if (sender.Team == CsTeam.Spectator)
			prefixes.Add(settings.SpectatorPrefix);
		else if (!sender.PawnIsAlive)
			prefixes.Add(settings.DeadPrefix);
		if (team)
			prefixes.Add(settings.TeamPrefix);
		string prefix = string.Join(" ", prefixes.Where(p => p.Length > 0));
		string prefixText = prefix.Length > 0 ? $"{ChatColors.Grey}{prefix}{ChatColors.Default}" : "";

		// Country code from the GeoIP lookup on connect - "XX" (unknown) and "LL" (local network) aren't real ones
		string? countryCode = player?.Profile.Country;
		string country = countryCode is { Length: 2 } && countryCode != "XX" && countryCode != "LL"
			? countryCode.ToUpperInvariant()
			: settings.UnknownCountry;

		string teamText = sender.Team switch
		{
			CsTeam.CounterTerrorist => "CT",
			CsTeam.Terrorist => "T",
			CsTeam.Spectator => "SPEC",
			_ => "",
		};

		// Tokens are replaced once, so braces typed in the message stay text. Plain values ({country},
		// {team}, {points}, {ranknum}) have no color of their own - color them in the format, e.g. {grey}[{country}]
		string line = FormatToken.Replace(settings.Format, match => match.Groups[1].Value.ToLowerInvariant() switch
		{
			"rank" => rankText,
			"ranknum" => rank?.ToString() ?? "-",
			"points" => (player?.Profile.ServerPoints ?? 0).ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
			"name" => nameText,
			"message" => $"{ChatColors.Default}{text}",
			"prefix" => prefixText,
			"country" => country,
			"team" => teamText,
			string color when ChatSettings.Colors.TryGetValue(color, out char c) => c.ToString(),
			_ => match.Value,
		});
		if (prefixText.Length > 0 && !settings.Format.Contains("{prefix}", StringComparison.OrdinalIgnoreCase))
			line = $"{prefixText} {line}";

		// A leading space - CS2 ignores a color code at the very start of a chat line
		line = " " + line;

		var senderTeam = sender.Team;
		foreach (var recipient in playerList.Values)
		{
			var controller = recipient.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;
			if (team && controller.Team != senderTeam)
				continue;
			controller.PrintToChat(line);
		}

		_logger.LogInformation("[Chat] {Prefix}{Name}: {Message}", prefix.Length > 0 ? prefix + " " : "", name, text);
	}

	internal void ForgetChatSpam(int userId) => _chatSpam.Remove(userId);

	// ---- Ranks shown in chat ----

	private static int _rankRefreshQueued;

	/// <summary>
	/// Reloads the server rank of every online player (after points changed) - several requests within a
	/// second are merged into one query.
	/// </summary>
	internal static void QueueServerRankRefresh()
	{
		if (Interlocked.Exchange(ref _rankRefreshQueued, 1) == 1)
			return;

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(1000);
				Interlocked.Exchange(ref _rankRefreshQueued, 0);

				var online = OnlinePlayers.Where(p => p.Profile.ID > 0).ToList();
				if (online.Count == 0)
					return;

				var ranks = await PlayerRepository.GetServerRanksAsync(online.Select(p => p.Profile.ID).ToList());
				Server.NextFrame(() =>
				{
					foreach (var player in online)
						player.Profile.SetServerRank(ranks.TryGetValue(player.Profile.ID, out var standing) ? standing : null);
				});
			}
			catch (Exception ex)
			{
				Interlocked.Exchange(ref _rankRefreshQueued, 0);
				_instance?._logger.LogError(ex, "[Chat] Refreshing server ranks failed");
			}
		});
	}
}
