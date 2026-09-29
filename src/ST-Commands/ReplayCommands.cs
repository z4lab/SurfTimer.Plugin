using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

public partial class SurfTimer
{
	[ConsoleCommand("css_replay", "Open the replay selection menu")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void OpenReplayMenu(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var oPlayer))
			return;

		int style = oPlayer.Timer.Style;
		var replays = CurrentMap.ReplayManager;

		static string Time(int ticks) => PlayerHud.FormatTime(ticks, PlayerTimer.TimeFormatStyle.Full);

		HudMenuItem Wr(string text, ReplayPlayer template) =>
			new(text, p => _ = HandleWrReplaySelection(p, template), template.RecordPlayerName, () => Time(template.RecordRunTime));

		HudMenuItem Pb(string text, PersonalBest pb, int type, int number) =>
			new(text, p => _ = HandlePbReplaySelection(p, PbReplayRef.Of(pb), type, number, style), "", () => Time(pb.RunTime));

		static bool Playable(ReplayPlayer template) => template.MapTimeID != -1 && template.Frames.Count > 0;

		// Map: WR, the WR segments chained into one run, own PB
		var map = new List<HudMenuItem>();
		if (Playable(replays.MapWR))
			map.Add(Wr("Map WR", replays.MapWR));

		int? bestSegmentsTime = replays.BestSegmentsTime(style);
		if (bestSegmentsTime != null)
		{
			map.Add(new HudMenuItem(ReplayManager.BestSegmentsLabel, p =>
			{
				var template = CurrentMap.ReplayManager.BuildBestSegmentsReplay(style);
				if (template != null)
					ApplyReplayRequest(p, template, requestedByPlayerId: -1);
			}, "", () => Time(bestSegmentsTime.Value)));
		}

		if (oPlayer.Stats.PB[style].ID != -1)
			map.Add(Pb("Map PB", oPlayer.Stats.PB[style], 0, 0));

		// Stages (WRs), bonuses (WRs + own PBs), checkpoints (WRs), own stage/checkpoint PBs
		var stages = new List<HudMenuItem>();
		var bonuses = new List<HudMenuItem>();
		var checkpoints = new List<HudMenuItem>();
		var ownPbs = new List<HudMenuItem>();

		for (int stage = 1; stage <= CurrentMap.Stages; stage++)
		{
			if (Playable(replays.AllStageWR[stage][style]))
				stages.Add(Wr($"Stage {stage} WR", replays.AllStageWR[stage][style]));
			if (oPlayer.Stats.StagePB[stage][style].ID != -1)
				ownPbs.Add(Pb($"Stage {stage} PB", oPlayer.Stats.StagePB[stage][style], 2, stage));
		}

		for (int bonus = 1; bonus <= CurrentMap.Bonuses; bonus++)
		{
			if (Playable(replays.AllBonusWR[bonus][style]))
				bonuses.Add(Wr($"Bonus {bonus} WR", replays.AllBonusWR[bonus][style]));
			if (oPlayer.Stats.BonusPB[bonus][style].ID != -1)
				bonuses.Add(Pb($"Bonus {bonus} PB", oPlayer.Stats.BonusPB[bonus][style], 1, bonus));
		}

		for (int cp = 1; cp <= CurrentMap.CheckpointSegments; cp++)
		{
			if (Playable(replays.AllCheckpointWR[cp][style]))
				checkpoints.Add(Wr($"Checkpoint {cp} WR", replays.AllCheckpointWR[cp][style]));
			if (oPlayer.Stats.CheckpointPB[cp][style].ID != -1)
				ownPbs.Add(Pb($"Checkpoint {cp} PB", oPlayer.Stats.CheckpointPB[cp][style], 3, cp));
		}

		var menu = new HudMenu("Replays",
		[
			new HudMenuTab("Map", map),
			new HudMenuTab("Stages", stages),
			new HudMenuTab("Bonuses", bonuses),
			new HudMenuTab("Checkpoints", checkpoints),
			new HudMenuTab("Your PBs", ownPbs),
		]);

		if (menu.IsEmpty)
		{
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["replay_none"]}");
			return;
		}

		MenuPresenter.Show(oPlayer, menu);
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
	/// Handles picking a PB replay - PB replays aren't loaded up front, so this fetches and decodes
	/// the replay by its id before requesting a pool slot.
	/// </summary>
	/// <param name="ownerName">Whose PB it is - the viewer's own when null (!profile shows other players' PBs)</param>
	/// <param name="ownerId">Profile ID of that player - the viewer's when null</param>
	private async Task HandlePbReplaySelection(CCSPlayerController player, PbReplayRef pb, int type, int stage, int style,
		string? ownerName = null, int? ownerId = null)
	{
		List<ReplayFrame> frames = [];
		try
		{
			if (pb.ReplayId is int replayId)
			{
				var data = await TimeRepository.GetReplayDataAsync(replayId);
				if (data != null)
					frames = await Task.Run(() => ReplayCodec.Decode(data));
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[{ClassName}] Loading PB replay {ReplayId} (time {TimeId}) failed", nameof(SurfTimer), pb.ReplayId, pb.TimeId);
		}

		// Back on the main thread - chat and the replay pool may only be touched there
		Server.NextFrame(() =>
		{
			try
			{
				if (!player.IsValid || !playerList.TryGetValue(player.UserId ?? 0, out var oPlayer))
					return;

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
					MapTimeID = pb.TimeId,
					ReplayId = pb.ReplayId,
					RecordRank = pb.Rank,
					RecordPlayerName = ownerName ?? oPlayer.Profile.Name ?? "N/A",
					RecordRunTime = pb.RunTime,
					Frames = frames,
				};

				ApplyReplayRequest(player, template, requestedByPlayerId: ownerId ?? oPlayer.Profile.ID);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[{ClassName}] Starting PB replay {ReplayId} (time {TimeId}) failed", nameof(SurfTimer), pb.ReplayId, pb.TimeId);
			}
		});
	}

	/// <summary>The PB a replay is picked for - its time, replay and what the bot shows</summary>
	private sealed record PbReplayRef(int TimeId, int? ReplayId, int RunTime, int Rank)
	{
		internal static PbReplayRef Of(PersonalBest pb) => new(pb.ID, pb.ReplayId, pb.RunTime, pb.Rank);
	}

	/// <summary>
	/// Runs a content template through ReplayManager.RequestReplay and acts on the result -
	/// spectate an already-playing/reclaimed slot immediately, tell the requester to wait for a
	/// fresh spawn, or refuse if the pool is full.
	/// </summary>
	private void ApplyReplayRequest(CCSPlayerController player, ReplayPlayer contentTemplate, int requestedByPlayerId)
	{
		var (result, slot) = CurrentMap.ReplayManager.RequestReplay(contentTemplate, requestedByPlayerId, Config.ReplayRepeatCount, player.UserId);

		switch (result)
		{
			case ReplayReuseResult.AlreadyPlaying:
				SpectateTarget(player, slot!.Controller!);
				break;

			case ReplayReuseResult.Spawning:
				slot!.PendingSpectatorUserId = player.UserId;
				player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["replay_spawning"]}");
				SpawnReplayBotDirectly(slot);
				break;

			case ReplayReuseResult.ReclaimedIdle:
				RestartIdleReplayBot(slot!);
				SpectateTarget(player, slot!.Controller!);
				break;

			case ReplayReuseResult.Replaced:
				player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["replay_replaced"]}");
				if (slot!.Controller == null)
				{
					// Its bot is still on the way - it spawns with the new content
					slot.PendingSpectatorUserId = player.UserId;
					break;
				}

				if (slot.Controller.PawnIsAlive)
				{
					slot.Start();
					slot.FormatBotName();
				}
				else
				{
					RestartIdleReplayBot(slot);
				}
				SpectateTarget(player, slot.Controller);
				break;

			case ReplayReuseResult.CapReached:
				player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["replay_pool_full", Config.ReplayPoolCap]}");
				break;
		}
	}

	/// <summary>
	/// Idle bots are dead on Spectator (GoIdle) - rejoin and respawn so there's a live pawn, then play.
	/// </summary>
	private void RestartIdleReplayBot(ReplayPlayer slot)
	{
		slot.Controller!.PendingTeamNum = 1; // CS2 kicks bots without it when a player joins
		slot.Controller.ChangeTeam(CsTeam.Terrorist);
		slot.Controller.Respawn();
		AddTimer(1.5f, () =>
		{
			if (slot.Controller == null || !slot.Controller.IsValid)
				return;

			slot.Controller.RemoveWeapons();
			slot.Start();
			slot.FormatBotName();
		});
	}

	/// <summary>
	/// Creates the bot for a slot awaiting one through CreateBot, so it also works on maps without a
	/// nav mesh. The bot is claimed by the slot in OnPlayerSpawn like any other bot. If direct creation
	/// isn't available, bot_quota (raised in OnTick) still asks the engine for a bot as before.
	/// </summary>
	private void SpawnReplayBotDirectly(ReplayPlayer slot)
	{
		// Quota first: with more bots than bot_quota the bot manager kicks one (normal mode), and with
		// the quota already matching it doesn't add a bot of its own on maps that have a nav mesh
		ConVar.Find("bot_quota")?.SetValue(CurrentMap.ReplayManager.Pool.Count);

		var bot = ReplayBotSpawner.TryCreate();
		if (bot == null)
			return;

		// Normally it spawns by itself (OnPlayerTeam respawns bots awaited by a slot) - make sure it does
		AddTimer(0.5f, () =>
		{
			if (!bot.IsValid || bot.PawnIsAlive || slot.Controller != null)
				return;

			bot.ChangeTeam(CsTeam.Terrorist);
			bot.Respawn();
		});
	}
}
