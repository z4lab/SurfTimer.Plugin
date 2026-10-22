using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// Chat messages players can turn off in !options (Chat):
/// - run messages (ChatEvent: splits, finishes, PBs, records): the runner and whoever spectates them get them by their
///   "own" toggles, everyone else by their "others" toggles. Records and PBs also play their sounds (Sounds.cs);
/// - connect messages.
/// Run messages come full or compact, by each player's Compact toggle.
/// Main thread only.
/// </summary>
internal static class ChatAnnounce
{
	internal enum Kind
	{
		Connect,
	}

	/// <param name="subject">Who it's about (always gets the message) - null for none</param>
	internal static void Send(Kind kind, Player? subject, string message)
	{
		foreach (var player in SurfTimer.OnlinePlayers)
		{
			var controller = player.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;

			if (!ReferenceEquals(player, subject) && !player.Options.ChatConnects)
				continue;

			controller.PrintToChat(message);
		}
	}

	/// <summary>
	/// A run message in both forms - each player gets the one of their !options - Chat - Compact toggle. Without the
	/// prefix (Run adds it).
	/// </summary>
	internal readonly record struct Text(string Full, string Compact)
	{
		internal string For(Player player) => $"{Config.PluginPrefix} {(player.Options.ChatCompact ? Compact : Full)}";

		/// <summary>"(Practice) " in front</summary>
		internal Text Practice() =>
			new($"({ChatColors.Grey}Practice{ChatColors.Default}) {Full}", $"({ChatColors.Grey}Prac{ChatColors.Default}) {Compact}");
	}

	/// <summary>A run message from its lang key: the full text and "key_compact", with the same arguments</summary>
	internal static Text Msg(string key, params object[] args) =>
		new(LocalizationService.LocalizerNonNull[key, args], LocalizationService.LocalizerNonNull[key + "_compact", args]);

	/// <summary>
	/// A run message: ownText to the runner, othersText to their spectators (ownText when null - it's "you"), both by
	/// their own toggle; othersText to everyone else by their others toggle (nobody else when null, e.g. practice).
	/// </summary>
	internal static void Run(ChatEvent ev, Player subject, Text ownText, Text? othersText)
	{
		var runner = subject.Controller;
		foreach (var player in SurfTimer.OnlinePlayers)
		{
			var controller = player.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;

			if (ReferenceEquals(player, subject))
			{
				if (player.Options.ChatOwn(ev))
					controller.PrintToChat(ownText.For(player));
			}
			else if (runner.IsValid && player.IsSpectating(runner))
			{
				if (player.Options.ChatOwn(ev))
					controller.PrintToChat((othersText ?? ownText).For(player));
			}
			else if (othersText is Text others && player.Options.ChatOthers(ev))
			{
				controller.PrintToChat(others.For(player));
			}
		}
	}

	/// <summary>A record (WR) - the record sound, the same text for everyone</summary>
	internal static void Record(ChatEvent ev, Player subject, Text text)
	{
		Sounds.PlayRecord(subject);
		Run(ev, subject, text, text);
	}

	/// <summary>A personal best - the PB sound for the runner</summary>
	internal static void Pb(ChatEvent ev, Player subject, Text ownText, Text othersText)
	{
		Sounds.Play(subject, Sounds.Pb);
		Run(ev, subject, ownText, othersText);
	}

	/// <summary>"Name · " in front of a message about someone else's run ("Name " when compact)</summary>
	internal static Text About(Player subject, Text body)
	{
		string name = $"{ChatColors.Lime}{subject.Controller.PlayerName}{ChatColors.Default}";
		return new($"{name} · {body.Full}", $"{name} {body.Compact}");
	}
}
