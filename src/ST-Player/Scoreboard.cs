using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// Scoreboard score = -server rank, so the scoreboard (sorted by score, highest first) lists players by
/// rank: #1 has -1 and is on top. Players without points and replay bots go to the bottom.
/// Clan tags: the player's country and / or own tag, both switchable (!surfadmin - Server - Timer settings).
/// </summary>
public partial class SurfTimer
{
	private const int UnrankedScore = -99998;
	private const int ReplayBotScore = -99999;

	/// <summary>
	/// Sets everyone's score (once per second from OnTick) - the game changes it on kills / round
	/// restarts, so it's kept in line instead of set once.
	/// </summary>
	private void UpdateScoreboard()
	{
		foreach (var player in playerList.Values)
		{
			var controller = player.Controller;
			if (controller.IsValid && !controller.IsBot)
				SetScore(controller, player.Profile.ServerRank is int rank ? -rank : UnrankedScore);
		}

		foreach (var slot in CurrentMap?.ReplayManager?.Pool ?? [])
		{
			if (slot.Controller != null && slot.Controller.IsValid)
				SetScore(slot.Controller, ReplayBotScore);
		}
	}

	// m_szClan holds 32 bytes including the terminator
	private const int MaxClanTagLength = 31;

	/// <summary>
	/// The scoreboard clan tag from the two settings: the country (country_clan_tag) followed by the
	/// player's own tag (clan_tags_enabled) - shown as [DE][z4lab], [DE], [z4lab] or nothing. The player's own tag
	/// is remembered, so it comes back when clan tags are turned on again. Once per second from OnTick.
	/// </summary>
	private void UpdateClanTags()
	{
		foreach (var player in playerList.Values)
		{
			var controller = player.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;

			// Anything but what we wrote last came from the player (connect / changed their clan)
			string current = controller.Clan ?? "";
			if (current != player.AppliedClanTag)
				player.UserClanTag = current;

			// The game shows the clan tag in brackets itself - "DE" shows as [DE], "DE][z4lab" as [DE][z4lab]
			string? country = player.Profile.Country;
			string countryTag = Config.CountryClanTag && country is { Length: 2 } && country != "XX" && country != "LL"
				? country.ToUpperInvariant()
				: "";
			string userTag = Config.ClanTagsEnabled ? player.UserClanTag : "";
			string wanted = string.Join("] [", new[] { countryTag, userTag }.Where(t => t.Length > 0));
			if (wanted.Length > MaxClanTagLength)
				wanted = wanted[..MaxClanTagLength];

			if (current != wanted)
				SetClan(controller, wanted);
			player.AppliedClanTag = wanted;
		}
	}

	private static void SetClan(CCSPlayerController controller, string clan)
	{
		controller.Clan = clan;
		Utilities.SetStateChanged(controller, "CCSPlayerController", "m_szClan");
	}

	private static void SetScore(CCSPlayerController controller, int score)
	{
		if (controller.Score == score)
			return;

		controller.Score = score;
		Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iScore");
	}
}
