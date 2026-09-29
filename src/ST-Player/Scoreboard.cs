using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// Scoreboard score = -server rank, so the scoreboard (sorted by score, highest first) lists players by
/// rank: #1 has -1 and is on top. Players without points and replay bots go to the bottom.
/// Clan tags can be turned off server-wide (!admin - Server - Timer settings).
/// </summary>
public partial class SurfTimer
{
	private const int UnrankedScore = -99999;
	private const int ReplayBotScore = -100000;

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

	/// <summary>
	/// Clan tags off (clan_tags_enabled): a player's tag is taken off and remembered - also a new one set
	/// meanwhile - and put back once tags are enabled again. Once per second from OnTick.
	/// </summary>
	private void UpdateClanTags()
	{
		foreach (var player in playerList.Values)
		{
			var controller = player.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;

			string clan = controller.Clan ?? "";
			if (!Config.ClanTagsEnabled)
			{
				if (clan.Length == 0)
					continue;
				player.HiddenClanTag = clan;
				SetClan(controller, "");
			}
			else if (player.HiddenClanTag != null)
			{
				if (clan.Length == 0)
					SetClan(controller, player.HiddenClanTag);
				player.HiddenClanTag = null;
			}
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
