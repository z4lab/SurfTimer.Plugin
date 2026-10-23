using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// HUD codes (hud_presets): every HUD option of a player as an 8 character code - !hudexport / !options - HUD - Share HUD - Export
/// gives yours, !hudimport &lt;code&gt; takes someone's. Codes stay forever; the same setup always gets the same code.
/// </summary>
internal static class HudPresets
{
	// Crockford base32 - no I, L, O, U (typed codes map I / L to 1 and O to 0)
	private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
	internal const int CodeLength = 8;

	/// <summary>The player's HUD settings as stored (keys without a value are the defaults), sorted - the same setup is the same text</summary>
	internal static SortedDictionary<string, string> Collect(PlayerProfile profile) =>
		new(PlayerOptions.HudKeys
			.Where(key => profile.Settings.TryGetValue(key, out var value) && value.Length > 0)
			.ToDictionary(key => key, key => profile.Settings[key]), StringComparer.Ordinal);

	/// <summary>A typed code in its stored form - null when it can't be one</summary>
	internal static string? Normalize(string text)
	{
		var code = new StringBuilder();
		foreach (char c in text.Trim().ToUpperInvariant())
		{
			char mapped = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
			if (mapped == '-' || mapped == ' ')
				continue;
			if (!Alphabet.Contains(mapped))
				return null;
			code.Append(mapped);
		}
		return code.Length == CodeLength ? code.ToString() : null;
	}

	/// <summary>The code of a setup: an existing one for the same settings, else a new one (from its hash)</summary>
	internal static async Task<string> ExportAsync(SortedDictionary<string, string> settings, int playerId)
	{
		string json = JsonSerializer.Serialize(settings);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
		string hashHex = Convert.ToHexString(hash).ToLowerInvariant();

		var existing = await SurfTimer.DB.QueryFirstOrDefaultAsync<string>(
			"SELECT `code` FROM `{p}hud_presets` WHERE `settings_hash` = @Hash", new { Hash = hashHex });
		if (existing != null)
			return existing;

		// Same settings, same code - another code only if that one is taken by different settings (practically never)
		for (int attempt = 0; attempt < 8; attempt++)
		{
			string code = attempt == 0 ? CodeOf(hash) : CodeOf(RandomNumberGenerator.GetBytes(5));
			int inserted = await SurfTimer.DB.ExecuteAsync(@"
				INSERT IGNORE INTO `{p}hud_presets` (`code`, `settings`, `settings_hash`, `created_by`, `created_at`)
				VALUES (@Code, @Settings, @Hash, @PlayerId, UTC_TIMESTAMP(3))",
				new { Code = code, Settings = json, Hash = hashHex, PlayerId = playerId > 0 ? (int?)playerId : null });
			if (inserted > 0)
				return code;

			existing = await SurfTimer.DB.QueryFirstOrDefaultAsync<string>(
				"SELECT `code` FROM `{p}hud_presets` WHERE `settings_hash` = @Hash", new { Hash = hashHex });
			if (existing != null)
				return existing; // Stored by someone else meanwhile
		}
		throw new InvalidOperationException("no free HUD code");
	}

	/// <summary>The settings of a code - null when there's no such code (or its settings can't be read)</summary>
	internal static async Task<Dictionary<string, string>?> ImportAsync(string code)
	{
		var json = await SurfTimer.DB.QueryFirstOrDefaultAsync<string>(
			"SELECT `settings` FROM `{p}hud_presets` WHERE `code` = @Code", new { Code = code });
		if (json == null)
			return null;

		await SurfTimer.DB.ExecuteAsync("UPDATE `{p}hud_presets` SET `imports` = `imports` + 1 WHERE `code` = @Code", new { Code = code });
		try
		{
			return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string CodeOf(byte[] bytes)
	{
		// The first 40 bits, 5 bits per character
		ulong bits = 0;
		for (int i = 0; i < 5; i++)
			bits = (bits << 8) | bytes[i];
		var code = new char[CodeLength];
		for (int i = CodeLength - 1; i >= 0; i--)
		{
			code[i] = Alphabet[(int)(bits & 31)];
			bits >>= 5;
		}
		return new string(code);
	}
}

public partial class SurfTimer
{
	[ConsoleCommand("css_hudexport", "A code for your HUD settings - others take them with !hudimport <code>")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void HudExportCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null && playerList.TryGetValue(player.UserId ?? 0, out var p))
			ExportHud(p, null);
	}

	[ConsoleCommand("css_hudimport", "Take the HUD settings of a code from !hudexport")]
	[CommandHelper(minArgs: 1, usage: "<code>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void HudImportCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null && playerList.TryGetValue(player.UserId ?? 0, out var p))
			ImportHud(p, command.ArgString, null);
	}

	/// <summary>Creates / finds the code of the player's HUD settings and tells them (chat, and the panel's status line)</summary>
	internal void ExportHud(Player p, PanelSession? session)
	{
		var settings = HudPresets.Collect(p.Profile);
		int playerId = p.Profile.ID;
		var controller = p.Controller;
		_ = Task.Run(async () =>
		{
			string? code = null;
			try
			{
				code = await HudPresets.ExportAsync(settings, playerId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[HudPresets] Exporting the HUD of player {PlayerId} failed", playerId);
			}

			Server.NextFrame(() =>
			{
				if (!controller.IsValid)
					return;
				string message = code != null
					? LocalizationService.LocalizerNonNull["hud_export", code]
					: LocalizationService.LocalizerNonNull["hud_export_failed"];
				controller.PrintToChat($"{Config.PluginPrefix} {message}");
				if (session != null)
				{
					session.Status = code != null ? $"Your HUD code: {code} (also in chat)" : "Export failed";
					PanelRefresh(session);
				}
			});
		});
	}

	/// <summary>
	/// Takes the HUD settings of a code: every HUD option becomes the code's, options it doesn't have go back to their
	/// defaults. Only HUD keys are taken. Returns an error for a code that can't be one (null: looking it up).
	/// </summary>
	internal string? ImportHud(Player p, string text, PanelSession? session)
	{
		var controller = p.Controller;
		string? code = HudPresets.Normalize(text);
		if (code == null)
		{
			string error = LocalizationService.LocalizerNonNull["hud_import_invalid", HudPresets.CodeLength];
			controller.PrintToChat($"{Config.PluginPrefix} {error}");
			return $"A code has {HudPresets.CodeLength} characters";
		}

		_ = Task.Run(async () =>
		{
			Dictionary<string, string>? settings = null;
			try
			{
				settings = await HudPresets.ImportAsync(code);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[HudPresets] Importing HUD code {Code} failed", code);
			}

			Server.NextFrame(() =>
			{
				if (!controller.IsValid)
					return;
				if (settings == null)
				{
					controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["hud_import_unknown", code]}");
					if (session != null)
					{
						session.Status = $"No HUD code {code}";
						PanelRefresh(session);
					}
					return;
				}

				p.Options.ApplyHud(settings);
				controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["hud_imported", code]}");
				if (session != null)
				{
					session.Status = $"HUD code {code} taken";
					PanelRefresh(session);
				}
			});
		});
		return null;
	}
}
