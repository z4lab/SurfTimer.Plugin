using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Scoreboard score = -server rank, so the scoreboard (sorted by score, highest first) lists players by
/// rank: #1 has -1 and is on top. Players without points and replay bots go to the bottom.
/// Clan tags: the player's country and / or own tag, both switchable (!surfadmin - Server - Timer settings).
/// Rank badges (rank_badges): the Premier rating badge shows the player's server points - the game colors it by the
/// number (grey under 5,000 ... gold from 30,000). Unranked players and replay bots show 0.
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
		// CounterStrikeSharp's FollowCS2ServerGuidelines (core.json) blocks the rank fields - then they aren't touched
		bool rankFields = !CoreConfig.FollowCS2ServerGuidelines;
		if (Config.RankBadges && !rankFields && !_rankBadgesBlockedLogged)
		{
			_rankBadgesBlockedLogged = true;
			_logger.LogWarning("[CS2 Surf] rank_badges is on, but CounterStrikeSharp's FollowCS2ServerGuidelines (core.json) blocks rank badges - they stay off");
		}
		bool badges = Config.RankBadges && rankFields;
		foreach (var player in playerList.Values)
		{
			var controller = player.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;
			SetScore(controller, player.Profile.ServerRank is int rank ? -rank : UnrankedScore);
			if (rankFields)
				SetRankBadge(controller, badges, (int)Math.Min(player.Profile.ServerPoints, int.MaxValue));
		}

		foreach (var slot in CurrentMap?.ReplayManager?.Pool ?? [])
		{
			if (slot.Controller != null && slot.Controller.IsValid)
			{
				SetScore(slot.Controller, ReplayBotScore);
				if (rankFields)
					SetRankBadge(slot.Controller, badges, 0);
			}
		}

		// Clients only show the ranks of their Steam friends - this lets them show everyone's. The game forgets it
		// (map load, round restarts), so it's sent again every few seconds.
		if (badges && ++_rankRevealSeconds >= RankRevealIntervalSeconds)
		{
			_rankRevealSeconds = 0;
			RevealRanks();
		}
	}

	// ---- Rank badges ----

	private const sbyte PremierRankType = 11;
	// The scoreboard shows a rank only from this many wins on
	private const int RankBadgeWins = 10;
	private const int RankRevealIntervalSeconds = 5;
	private int _rankRevealSeconds = RankRevealIntervalSeconds;
	private bool _rankBadgesBlockedLogged;

	/// <summary>The Premier badge with the given rating - off: no badge (the game's default)</summary>
	private static void SetRankBadge(CCSPlayerController controller, bool on, int rating)
	{
		sbyte type = on ? PremierRankType : (sbyte)0;
		int ranking = on ? rating : 0;
		int wins = on ? RankBadgeWins : 0;
		if (controller.CompetitiveRankType == type && controller.CompetitiveRanking == ranking && controller.CompetitiveWins == wins)
			return;

		controller.CompetitiveRankType = type;
		controller.CompetitiveRanking = ranking;
		controller.CompetitiveWins = wins;
		Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iCompetitiveRankType");
		Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iCompetitiveRanking");
		Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iCompetitiveWins");
	}

	/// <summary>Lets every client show everyone's rank badge (CCSUsrMsg_ServerRankRevealAll)</summary>
	private void RevealRanks()
	{
		try
		{
			var message = UserMessage.FromPartialName("ServerRankRevealAll");
			message.Recipients.AddAllPlayers();
			message.Send();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "[CS2 Surf] ServerRankRevealAll failed");
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
