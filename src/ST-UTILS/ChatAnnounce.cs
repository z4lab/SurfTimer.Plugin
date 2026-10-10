namespace SurfTimer;

/// <summary>
/// Server-wide chat announcements that players can turn off in !options (Chat): records, PBs and
/// connect messages. The player an announcement is about always gets it. Records and PBs also play their sounds
/// (Sounds.cs). Main thread only.
/// </summary>
internal static class ChatAnnounce
{
	internal enum Kind
	{
		Record,
		Pb,
		Connect,
	}

	/// <param name="subject">Who set the time (always gets the message) - null for none</param>
	internal static void Send(Kind kind, Player? subject, string message)
	{
		// Records and PBs come with a sound (each player's !options - Sound)
		if (subject != null && kind == Kind.Record)
			Sounds.PlayRecord(subject);
		else if (subject != null && kind == Kind.Pb)
			Sounds.Play(subject, Sounds.Pb);

		foreach (var player in SurfTimer.OnlinePlayers)
		{
			var controller = player.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;

			if (!ReferenceEquals(player, subject) && !Wants(player.Options, kind))
				continue;

			controller.PrintToChat(message);
		}
	}

	private static bool Wants(PlayerOptions options, Kind kind) => kind switch
	{
		Kind.Record => options.ChatOthersRecords,
		Kind.Pb => options.ChatOthersPb,
		Kind.Connect => options.ChatConnects,
		_ => true,
	};
}
