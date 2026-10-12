namespace SurfTimer;

/// <summary>
/// Chat messages players can turn off in !options (Chat):
/// - run messages (ChatEvent: splits, finishes, PBs, records): the runner and whoever spectates them get them by their
///   "own" toggles, everyone else by their "others" toggles. Records and PBs also play their sounds (Sounds.cs);
/// - connect messages.
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
	/// A run message: ownText to the runner, othersText to their spectators (ownText when null - it's "you"), both by
	/// their own toggle; othersText to everyone else by their others toggle (nobody else when null, e.g. practice).
	/// </summary>
	internal static void Run(ChatEvent ev, Player subject, string ownText, string? othersText)
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
					controller.PrintToChat(ownText);
			}
			else if (runner.IsValid && player.IsSpectating(runner))
			{
				if (player.Options.ChatOwn(ev))
					controller.PrintToChat(othersText ?? ownText);
			}
			else if (othersText != null && player.Options.ChatOthers(ev))
			{
				controller.PrintToChat(othersText);
			}
		}
	}

	/// <summary>A record (WR) - the record sound, the same text for everyone</summary>
	internal static void Record(ChatEvent ev, Player subject, string text)
	{
		Sounds.PlayRecord(subject);
		Run(ev, subject, text, text);
	}

	/// <summary>A personal best - the PB sound for the runner</summary>
	internal static void Pb(ChatEvent ev, Player subject, string ownText, string othersText)
	{
		Sounds.Play(subject, Sounds.Pb);
		Run(ev, subject, ownText, othersText);
	}

	/// <summary>"Name · " in front of a message about someone else's run</summary>
	internal static string About(Player subject, string body) =>
		$"{Config.PluginPrefix} {CounterStrikeSharp.API.Modules.Utils.ChatColors.Lime}{subject.Controller.PlayerName}{CounterStrikeSharp.API.Modules.Utils.ChatColors.Default} · {body}";
}
