using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Admin panel - Players tab: online players and search, a player's page (stats, names, sessions),
/// timer bans and wiping their times.
/// </summary>
public partial class SurfTimer
{
	private PanelPage AdminPlayersRoot() => new("Players", ctx =>
	{
		var rows = new List<HudMenuItem>
		{
			ctx.Ask("Search", "", "online and offline, by name", LocalizationService.LocalizerNonNull["prompt_player_search"], text =>
			{
				if (text.Length < 2 || text.Length > 64)
					return "Type 2 to 64 characters of the name";
				ctx.Session.Stack.Add(AdminSearchPage(text));
				return null;
			}),
		};

		foreach (var player in playerList.Values.Where(p => p.Controller.IsValid && !p.Controller.IsBot).OrderBy(p => p.Controller.PlayerName))
		{
			string sub = player.Profile.IsBanned ? "timer banned" : player.Controller.Team == CsTeam.Spectator ? "spectating" : "";
			int id = player.Profile.ID;
			string name = player.Controller.PlayerName;
			rows.Add(ctx.Nav(name, "online", sub, () => AdminPlayerPage(id, name)));
		}
		return rows;
	});

	private sealed class SearchData(List<PlayerProfileEntity> players)
	{
		internal List<PlayerProfileEntity> Players { get; } = players;
	}

	private PanelPage AdminSearchPage(string search) => new($"\"{search}\"", ctx =>
	{
		var result = ctx.Load("search", async () => new SearchData(await PlayerRepository.SearchAsync(search, 40)));
		if (result == null)
			return [PanelContext.LoadingRow()];
		if (result.Players.Count == 0)
			return [PanelContext.Info("No players found", "", search)];

		return result.Players.Select(p =>
		{
			bool online = playerList.Values.Any(o => o.Profile.ID == p.ID && o.Controller.IsValid);
			string seen = online ? "online" : AdminFormat.Ago(DateTimeOffset.FromUnixTimeSeconds(p.LastSeen).UtcDateTime);
			string name = p.Name ?? "?";
			return ctx.Nav(name, seen, p.SteamID.ToString(), () => AdminPlayerPage(p.ID, name));
		}).ToList();
	});

	private sealed class PlayerData
	{
		internal PlayerProfileEntity? Profile { get; init; }
		internal PlayerRepository.SummaryRow Summary { get; init; } = new();
		internal PlayerRepository.BanRow? Ban { get; init; }
	}

	private PanelPage AdminPlayerPage(int playerId, string name) => new(name, ctx =>
	{
		int style = ctx.Style;
		var data = ctx.Load("player", async () => new PlayerData
		{
			Profile = await PlayerRepository.GetAsync(playerId),
			Summary = await PlayerRepository.GetSummaryAsync(playerId, style),
			Ban = await PlayerRepository.GetActiveBanAsync(playerId),
		});
		if (data == null)
			return [PanelContext.LoadingRow()];
		if (data.Profile == null)
			return [PanelContext.Info("Player not found")];

		var profile = data.Profile;
		var online = playerList.Values.FirstOrDefault(p => p.Profile.ID == playerId && p.Controller.IsValid);
		var summary = data.Summary;
		DateTime firstSeen = DateTimeOffset.FromUnixTimeSeconds(profile.JoinDate).UtcDateTime;
		DateTime lastSeen = DateTimeOffset.FromUnixTimeSeconds(profile.LastSeen).UtcDateTime;

		var rows = new List<HudMenuItem>
		{
			PanelContext.Info("Points", AdminFormat.Number(summary.Points), $"rank #{summary.Rank}"),
			PanelContext.Info("Playtime", AdminFormat.Duration(summary.PlayTime), $"{profile.Connections} visits"),
			PanelContext.Info("Last seen", online != null ? "online now" : AdminFormat.Ago(lastSeen) + " ago", $"first seen {AdminFormat.Date(firstSeen)}"),
			PanelContext.Info("Times", AdminFormat.Number(summary.Times), summary.HiddenTimes > 0 ? $"{summary.HiddenTimes} hidden" : ""),
			ctx.Act("Open profile", "", "", () => OpenProfileById(ctx.Player.Controller, playerId), closes: true),
		};

		if (online != null && IsSpectatable(online.Controller) && !online.Controller.Equals(ctx.Player.Controller))
		{
			var target = online.Controller;
			rows.Add(ctx.Act("Spectate", "", "you leave your run", () => SpectateTarget(ctx.Player.Controller, target), closes: true));
		}

		rows.Add(ctx.Nav("Names", "", "name history", () => AdminNamesPage(playerId, name)));
		rows.Add(ctx.Nav("Sessions", profile.Connections.ToString(), "latest visits", () => AdminSessionsPage(playerId, name)));

		if (data.Ban != null)
		{
			var ban = data.Ban;
			string until = ban.ExpiresAt is DateTime expires ? $"until {AdminFormat.Date(expires)}" : "permanent";
			rows.Add(PanelContext.Info("Timer banned", until, $"{ban.Reason} · by {ban.AdminName ?? "console"}"));
			rows.Add(ctx.Act("Unban", "", "shows their times again", () => AdminUnban(ctx, playerId, name)));
		}
		else
		{
			rows.Add(ctx.Nav("Timer ban", "", "runs not saved, times hidden", () => AdminBanPage(playerId, name)));
		}

		if (CurrentMap != null && CurrentMap.ID > 0)
		{
			rows.Add(ctx.Danger("Wipe times on this map", CurrentMap.Name ?? "", () => AdminWipeConfirm("Wipe times on this map",
				new TimeRepository.WipeScope(MapId: CurrentMap.ID, PlayerId: playerId), $"{name} on {CurrentMap.Name}")));
		}
		rows.Add(ctx.Danger("Wipe all times", "every map", () => AdminWipeConfirm("Wipe all times",
			new TimeRepository.WipeScope(PlayerId: playerId), $"all times of {name}")));
		rows.Add(ctx.Act("Reset settings", "", "!options back to defaults", () =>
		{
			if (online != null)
			{
				online.Options.ResetToDefaults();
				online.ApplySelfVisibility();
			}
			ctx.Audit("reset settings", "player", playerId, name);
			ctx.Run("Resetting settings…", async () =>
			{
				int removed = await PlayerRepository.ResetSettingsAsync(playerId);
				return $"Removed {removed} setting(s) of {name}";
			});
		}));
		return rows;
	});

	private sealed class NamesData(List<PlayerRepository.NameRow> names)
	{
		internal List<PlayerRepository.NameRow> Names { get; } = names;
	}

	private PanelPage AdminNamesPage(int playerId, string name) => new("Names", ctx =>
	{
		var data = ctx.Load("names", async () => new NamesData(await PlayerRepository.GetNamesAsync(playerId)));
		if (data == null)
			return [PanelContext.LoadingRow()];
		return data.Names.Select(n => PanelContext.Info(n.Name, AdminFormat.Ago(n.LastUsedAt) + " ago", $"since {AdminFormat.Date(n.FirstUsedAt)}")).ToList();
	});

	private sealed class SessionsData(List<PlayerRepository.SessionRow> sessions)
	{
		internal List<PlayerRepository.SessionRow> Sessions { get; } = sessions;
	}

	private PanelPage AdminSessionsPage(int playerId, string name) => new("Sessions", ctx =>
	{
		var data = ctx.Load("sessions", async () => new SessionsData(await PlayerRepository.GetRecentSessionsAsync(playerId, 40)));
		if (data == null)
			return [PanelContext.LoadingRow()];
		return data.Sessions.Select(s =>
		{
			long seconds = (long)((s.LeftAt ?? s.LastHeartbeatAt) - s.JoinedAt).TotalSeconds;
			return PanelContext.Info(AdminFormat.Date(s.JoinedAt), s.LeftAt == null ? "open" : AdminFormat.Duration(Math.Max(0, seconds)), s.MapName ?? "");
		}).ToList();
	});

	// ---- Timer bans ----

	private PanelPage AdminBanPage(int playerId, string name) => new("Timer ban", ctx =>
	{
		HudMenuItem Length(string label, TimeSpan? length) => ctx.Ask(label, "", "reason in chat",
			LocalizationService.LocalizerNonNull["prompt_ban_reason", name], reason =>
			{
				if (reason.Length > 255)
					return "Up to 255 characters";
				AdminBan(ctx, playerId, name, reason, length);
				return null;
			});

		return
		[
			PanelContext.Info("While banned", "", "they can play, runs aren't saved, times are hidden"),
			Length("1 day", TimeSpan.FromDays(1)),
			Length("7 days", TimeSpan.FromDays(7)),
			Length("30 days", TimeSpan.FromDays(30)),
			Length("Permanent", null),
		];
	});

	private void AdminBan(PanelContext ctx, int playerId, string name, string reason, TimeSpan? length)
	{
		DateTime? expires = length.HasValue ? DateTime.UtcNow + length.Value : null;
		int adminId = ctx.Player.Profile.ID;
		string until = expires.HasValue ? $"until {AdminFormat.Date(expires.Value)}" : "permanently";

		var online = playerList.Values.FirstOrDefault(p => p.Profile.ID == playerId && p.Controller.IsValid);
		if (online != null)
		{
			online.Profile.Ban = new PlayerRepository.BanRow
			{
				PlayerId = playerId,
				AdminName = ctx.Player.Profile.Name,
				Reason = reason,
				CreatedAt = DateTime.UtcNow,
				ExpiresAt = expires,
			};
			online.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["timer_banned", until, reason]}");
		}

		ctx.Audit("ban", "player", playerId, $"{name} {until}: {reason}");
		// Back on the player page once it's done (the ban page is left)
		ctx.Session.Stack.RemoveAt(ctx.Session.Stack.Count - 1);
		var playerPage = new PanelContext(this, ctx.Session, ctx.Session.Top);
		playerPage.Run($"Banning {name}…", async () =>
		{
			await PlayerRepository.BanAsync(playerId, adminId, reason, expires);
			var affected = await TimeRepository.SetHiddenForPlayerAsync(playerId, hidden: true);
			await AdminAfterTimesChangedAsync(affected);
			return $"{name} is timer banned {until} - {affected.Times} time(s) hidden";
		}, status =>
		{
			playerPage.Reload("player");
			playerPage.Done(status);
		});
	}

	private void AdminUnban(PanelContext ctx, int playerId, string name)
	{
		int adminId = ctx.Player.Profile.ID;
		var online = playerList.Values.FirstOrDefault(p => p.Profile.ID == playerId && p.Controller.IsValid);
		if (online != null)
		{
			online.Profile.Ban = null;
			online.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["timer_unbanned"]}");
		}

		ctx.Audit("unban", "player", playerId, name);
		ctx.Run($"Unbanning {name}…", async () =>
		{
			await PlayerRepository.UnbanAsync(playerId, adminId);
			var affected = await TimeRepository.SetHiddenForPlayerAsync(playerId, hidden: false);
			await AdminAfterTimesChangedAsync(affected);
			return $"{name} is unbanned - {affected.Times} time(s) visible again";
		}, status =>
		{
			ctx.Reload("player");
			ctx.Done(status);
		});
	}

	/// <summary>
	/// Timer bans that ran out: times shown again, online players told. Plugin load and hourly.
	/// </summary>
	internal void LiftExpiredBans()
	{
		_ = Task.Run(async () =>
		{
			try
			{
				var players = await PlayerRepository.LiftExpiredBansAsync();
				foreach (int playerId in players)
				{
					var affected = await TimeRepository.SetHiddenForPlayerAsync(playerId, hidden: false);
					await AdminAfterTimesChangedAsync(affected);
				}

				if (players.Count > 0)
				{
					Server.NextFrame(() =>
					{
						foreach (var online in playerList.Values.Where(p => players.Contains(p.Profile.ID) && p.Controller.IsValid))
						{
							online.Profile.Ban = null;
							online.Controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["timer_unbanned"]}");
						}
					});
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[Admin] Lifting expired timer bans failed");
			}
		});
	}
}
