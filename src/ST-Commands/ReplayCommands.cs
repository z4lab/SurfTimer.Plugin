using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

public partial class SurfTimer
{
	[ConsoleCommand("css_replay", "Open the replay selection menu")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void OpenReplayMenu(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null)
			return;

		Player oPlayer = playerList[player.UserId ?? 0];
		int style = oPlayer.Timer.Style;

		ChatMenu menu = new ChatMenu("Replays");

		// Map WR
		if (CurrentMap.ReplayManager.MapWR.MapTimeID != -1 && CurrentMap.ReplayManager.MapWR.Frames.Count > 0)
		{
			var template = CurrentMap.ReplayManager.MapWR;
			menu.AddMenuOption($"Map WR - {template.RecordPlayerName} - {PlayerHud.FormatTime(template.RecordRunTime)}",
				(p, o) => _ = HandleWrReplaySelection(p, template));
		}

		// Stage WRs
		if (CurrentMap.Stages > 0)
		{
			for (int stage = 1; stage <= CurrentMap.Stages; stage++)
			{
				var template = CurrentMap.ReplayManager.AllStageWR[stage][style];
				if (template.MapTimeID == -1 || template.Frames.Count == 0)
					continue;

				menu.AddMenuOption($"Stage {stage} WR - {template.RecordPlayerName} - {PlayerHud.FormatTime(template.RecordRunTime)}",
					(p, o) => _ = HandleWrReplaySelection(p, template));
			}
		}

		// Bonus WRs
		for (int bonus = 1; bonus <= CurrentMap.Bonuses; bonus++)
		{
			var template = CurrentMap.ReplayManager.AllBonusWR[bonus][style];
			if (template.MapTimeID == -1 || template.Frames.Count == 0)
				continue;

			menu.AddMenuOption($"Bonus {bonus} WR - {template.RecordPlayerName} - {PlayerHud.FormatTime(template.RecordRunTime)}",
				(p, o) => _ = HandleWrReplaySelection(p, template));
		}

		// Checkpoint WRs (non-staged maps only)
		if (CurrentMap.Stages == 0 && CurrentMap.TotalCheckpoints > 0)
		{
			for (int cp = 1; cp <= CurrentMap.TotalCheckpoints; cp++)
			{
				var template = CurrentMap.ReplayManager.AllCheckpointWR[cp][style];
				if (template.MapTimeID == -1 || template.Frames.Count == 0)
					continue;

				menu.AddMenuOption($"Checkpoint {cp} WR - {template.RecordPlayerName} - {PlayerHud.FormatTime(template.RecordRunTime)}",
					(p, o) => _ = HandleWrReplaySelection(p, template));
			}
		}

		// Own PBs - only for segments actually completed
		if (oPlayer.Stats.PB[style].ID != -1)
		{
			var pb = oPlayer.Stats.PB[style];
			menu.AddMenuOption($"Your PB - Map - {PlayerHud.FormatTime(pb.RunTime)}",
				(p, o) => _ = HandlePbReplaySelection(p, pb, 0, 0, style));
		}

		if (CurrentMap.Stages > 0)
		{
			for (int stage = 1; stage <= CurrentMap.Stages; stage++)
			{
				if (oPlayer.Stats.StagePB[stage][style].ID == -1)
					continue;

				var pb = oPlayer.Stats.StagePB[stage][style];
				menu.AddMenuOption($"Your PB - Stage {stage} - {PlayerHud.FormatTime(pb.RunTime)}",
					(p, o) => _ = HandlePbReplaySelection(p, pb, 2, stage, style));
			}
		}

		for (int bonus = 1; bonus <= CurrentMap.Bonuses; bonus++)
		{
			if (oPlayer.Stats.BonusPB[bonus][style].ID == -1)
				continue;

			var pb = oPlayer.Stats.BonusPB[bonus][style];
			menu.AddMenuOption($"Your PB - Bonus {bonus} - {PlayerHud.FormatTime(pb.RunTime)}",
				(p, o) => _ = HandlePbReplaySelection(p, pb, 1, bonus, style));
		}

		if (CurrentMap.Stages == 0 && CurrentMap.TotalCheckpoints > 0)
		{
			for (int cp = 1; cp <= CurrentMap.TotalCheckpoints; cp++)
			{
				if (oPlayer.Stats.CheckpointPB[cp][style].ID == -1)
					continue;

				var pb = oPlayer.Stats.CheckpointPB[cp][style];
				menu.AddMenuOption($"Your PB - Checkpoint {cp} - {PlayerHud.FormatTime(pb.RunTime)}",
					(p, o) => _ = HandlePbReplaySelection(p, pb, 3, cp, style));
			}
		}

		menu.Open(player);
	}

	/// <summary>
	/// Handles picking a WR replay - the content template already has its Frames loaded
	/// (from map load), so this can run synchronously.
	/// </summary>
	private Task HandleWrReplaySelection(CCSPlayerController player, ReplayPlayer contentTemplate)
	{
		ApplyReplayRequest(player, contentTemplate, requestedByPlayerId: -1);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Handles picking a "Your PB" replay - PB replay frames aren't loaded up front, so this
	/// fetches them on demand before requesting a pool slot.
	/// </summary>
	private async Task HandlePbReplaySelection(CCSPlayerController player, PersonalBest pb, int type, int stage, int style)
	{
		Player oPlayer = playerList[player.UserId ?? 0];

		await pb.LoadPlayerSpecificMapTimeData(oPlayer);

		if (pb.ReplayFrames == null)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["replay_no_pb"]}");
			return;
		}

		List<ReplayFrame> frames = ReplayFrame.Deserialize(pb.ReplayFrames);
		if (frames.Count == 0)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["replay_no_pb"]}");
			return;
		}

		var template = new ReplayPlayer
		{
			Type = type,
			Stage = stage,
			Style = style,
			MapID = CurrentMap.ID,
			MapTimeID = pb.ID,
			RecordRank = pb.Rank,
			RecordPlayerName = oPlayer.Profile.Name ?? "N/A",
			RecordRunTime = pb.RunTime,
			Frames = frames,
		};

		Server.NextFrame(() => ApplyReplayRequest(player, template, requestedByPlayerId: oPlayer.Profile.ID));
	}

	/// <summary>
	/// Runs a content template through ReplayManager.RequestReplay and acts on the result -
	/// spectate an already-playing/reclaimed slot immediately, tell the requester to wait for a
	/// fresh spawn, or refuse if the pool is full.
	/// </summary>
	private void ApplyReplayRequest(CCSPlayerController player, ReplayPlayer contentTemplate, int requestedByPlayerId)
	{
		var (result, slot) = CurrentMap.ReplayManager.RequestReplay(contentTemplate, requestedByPlayerId, Config.ReplayRepeatCount);

		switch (result)
		{
			case ReplayReuseResult.AlreadyPlaying:
				SpectateTarget(player, slot!.Controller!);
				break;

			case ReplayReuseResult.Spawning:
				slot!.PendingSpectatorUserId = player.UserId;
				player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["replay_spawning"]}");
				break;

			case ReplayReuseResult.ReclaimedIdle:
				// Idle bots are dead on Spectator (GoIdle) - rejoin and respawn so there's a live pawn.
				slot!.Controller!.ChangeTeam(CsTeam.Terrorist);
				slot.Controller.Respawn();
				AddTimer(1.5f, () =>
				{
					if (slot.Controller == null || !slot.Controller.IsValid)
						return;

					slot.Controller.RemoveWeapons();
					slot.Start();
					slot.FormatBotName();
				});
				SpectateTarget(player, slot.Controller);
				break;

			case ReplayReuseResult.CapReached:
				player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["replay_pool_full", Config.ReplayPoolCap]}");
				break;
		}
	}
}
