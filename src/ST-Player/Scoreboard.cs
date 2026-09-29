using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SurfTimer;

/// <summary>
/// Scoreboard score = -server rank, so the scoreboard (sorted by score, highest first) lists players by
/// rank: #1 has -1 and is on top. Players without points and replay bots go to the bottom.
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

	private static void SetScore(CCSPlayerController controller, int score)
	{
		if (controller.Score == score)
			return;

		controller.Score = score;
		Utilities.SetStateChanged(controller, "CCSPlayerController", "m_iScore");
	}
}
