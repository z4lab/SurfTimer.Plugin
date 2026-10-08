using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// CS2's round timer (top centre of the game HUD) stands still at 13:37 (timer setting round_timer_freeze). Surf maps
/// have no rounds that end - and a round time that runs out makes CS2 kick every player, and everyone joining, as idle
/// until the map changes (a map running longer than mp_roundtime). Frozen it can't run out.
/// The timer shows round start + round time - now, rounded up: both are set so it shows 13:36.95 and set again before
/// it reaches 13:36.0 (about once a second) - so it always reads 13:37.
/// </summary>
public partial class SurfTimer
{
	private const int FrozenRoundSeconds = 13 * 60 + 37;
	/// <summary>Set to this far below 13:37 - the display rounds up</summary>
	private const float FrozenRoundOffset = 0.05f;

	private CCSGameRulesProxy? _rulesProxy;
	private bool _roundTimerFailed;

	private void TickRoundTimer()
	{
		if (!Config.RoundTimerFreeze || _roundTimerFailed)
			return;

		try
		{
			if (_rulesProxy is not { IsValid: true })
				_rulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
			var rules = _rulesProxy?.GameRules;
			if (rules == null)
				return;

			float now = Server.CurrentTime;
			float shown = rules.RoundStartTime + rules.RoundTime - now;
			if (shown > FrozenRoundSeconds - 1 + 0.05f && shown <= FrozenRoundSeconds)
				return;

			rules.RoundTime = FrozenRoundSeconds;
			rules.RoundStartTime = now - FrozenRoundOffset;
			Utilities.SetStateChanged(_rulesProxy!, "CCSGameRulesProxy", "m_pGameRules");
		}
		catch (Exception ex)
		{
			// E.g. schema offsets after a CS2 update - the round timer then simply counts down again
			_roundTimerFailed = true;
			_logger.LogError(ex, "[{Prefix}] Freezing the round timer failed - off until the plugin is reloaded", Config.PluginName);
		}
	}
}
