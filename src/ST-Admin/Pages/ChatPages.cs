using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Admin panel - Server - Chat: the chat processor's settings (chat_settings.json). Changes apply at once.
/// </summary>
public partial class SurfTimer
{
	private PanelPage AdminChatPage() => new("Chat", ctx =>
	{
		var settings = ChatSettings.Current;
		return
		[
			ctx.Toggle("Chat processor", settings.Enabled, "off = normal engine chat", on =>
			{
				settings.Enabled = on;
				SaveChatSettings(ctx, "enabled", on ? "on" : "off");
			}),
			ctx.Ask("Format", settings.Format, "type in chat",
				"Type the chat format in chat - it needs {name} and {message}. Also: {rank} {ranknum} {points} {country} {team} {prefix} and colors like {grey}. e.g. {grey}[{country}] {rank} ~ {name}: {message}", text =>
				{
					if (!ChatSettings.IsValidFormat(text))
						return "The format needs {name} and {message} (max 128 characters)";
					settings.Format = text;
					SaveChatSettings(ctx, "format", text);
					return null;
				}),
			PanelContext.Info("Placeholders", "", "{rank} {ranknum} {points} {name} {message} {country} {team} {prefix} {grey} ..."),
			ctx.Act("Reset format", "", ChatSettings.DefaultFormat, () =>
			{
				settings.Format = ChatSettings.DefaultFormat;
				SaveChatSettings(ctx, "format", ChatSettings.DefaultFormat);
			}),
			ctx.Nav("Rank colors", "", "tag colors by server rank", AdminChatRankColorsPage),
			ctx.Nav("Name colors", "", "root, admin, VIP", AdminChatNameColorsPage),
			ctx.Nav("Anti-spam", settings.AntiSpam.Enabled ? "on" : "off", "admins aren't limited", AdminChatAntiSpamPage),
		];
	});

	private PanelPage AdminChatRankColorsPage() => new("Rank colors", ctx =>
	{
		var colors = ChatSettings.Current.RankColors;
		return
		[
			ColorRow(ctx, "#1", () => colors.First, v => colors.First = v, allowTeam: false),
			ColorRow(ctx, "#2", () => colors.Second, v => colors.Second = v, allowTeam: false),
			ColorRow(ctx, "#3", () => colors.Third, v => colors.Third = v, allowTeam: false),
			ColorRow(ctx, "#4 - #10", () => colors.Top10, v => colors.Top10 = v, allowTeam: false),
			ColorRow(ctx, "Other ranks", () => colors.Other, v => colors.Other = v, allowTeam: false),
			ColorRow(ctx, "No points", () => colors.Unranked, v => colors.Unranked = v, allowTeam: false),
		];
	});

	private PanelPage AdminChatNameColorsPage() => new("Name colors", ctx =>
	{
		var settings = ChatSettings.Current;
		var colors = settings.NameColors;
		return
		[
			ColorRow(ctx, "Root", () => colors.Root, v => colors.Root = v, allowTeam: true, sub: settings.Flags.Root),
			ColorRow(ctx, "Admin", () => colors.Admin, v => colors.Admin = v, allowTeam: true, sub: settings.Flags.Admin),
			ColorRow(ctx, "VIP", () => colors.Vip, v => colors.Vip = v, allowTeam: true, sub: settings.Flags.Vip),
			ColorRow(ctx, "Everyone else", () => colors.Default, v => colors.Default = v, allowTeam: true),
		];
	});

	/// <summary>A row showing a color setting - opens the color picker</summary>
	private HudMenuItem ColorRow(PanelContext ctx, string label, Func<string> get, Action<string> set, bool allowTeam, string sub = "") =>
		ctx.Nav(label, get(), sub, () => AdminChatColorPicker(label, get, set, allowTeam));

	private PanelPage AdminChatColorPicker(string label, Func<string> get, Action<string> set, bool allowTeam) => new(label, ctx =>
	{
		var names = ChatSettings.Colors.Keys.ToList();
		if (allowTeam)
			names.Insert(0, ChatSettings.TeamColor);

		return names.Select(name => ctx.Act(name, name.Equals(get(), StringComparison.OrdinalIgnoreCase) ? "current" : "",
			name == ChatSettings.TeamColor ? "the player's team color" : "", () =>
			{
				set(name);
				SaveChatSettings(ctx, $"color {label}", name);
				PanelBackFrom(ctx.Session, ctx.Page, $"{label}: {name}");
			})).ToList();
	});

	private PanelPage AdminChatAntiSpamPage() => new("Anti-spam", ctx =>
	{
		var antiSpam = ChatSettings.Current.AntiSpam;

		void SetGap(double value)
		{
			antiSpam.MinGapSeconds = Math.Clamp(Math.Round(value, 1), 0, 30);
			SaveChatSettings(ctx, "antispam gap", $"{antiSpam.MinGapSeconds:0.0}s");
		}

		void SetWindow(double value)
		{
			antiSpam.DuplicateWindowSeconds = Math.Clamp(Math.Round(value), 0, 600);
			SaveChatSettings(ctx, "antispam duplicate window", $"{antiSpam.DuplicateWindowSeconds:0}s");
		}

		return
		[
			ctx.Toggle("Anti-spam", antiSpam.Enabled, "admins aren't limited", on =>
			{
				antiSpam.Enabled = on;
				SaveChatSettings(ctx, "antispam", on ? "on" : "off");
			}),
			PanelContext.Info("Gap between messages", $"{antiSpam.MinGapSeconds:0.0} s"),
			ctx.Act("Gap +0.5 s", "", "", () => SetGap(antiSpam.MinGapSeconds + 0.5)),
			ctx.Act("Gap -0.5 s", "", "", () => SetGap(antiSpam.MinGapSeconds - 0.5)),
			PanelContext.Info("Same message blocked for", $"{antiSpam.DuplicateWindowSeconds:0} s"),
			ctx.Act("Window +5 s", "", "", () => SetWindow(antiSpam.DuplicateWindowSeconds + 5)),
			ctx.Act("Window -5 s", "", "", () => SetWindow(antiSpam.DuplicateWindowSeconds - 5)),
		];
	});

	/// <summary>
	/// Writes chat_settings.json after a change from the panel (the in-memory settings already changed).
	/// </summary>
	private void SaveChatSettings(PanelContext ctx, string what, string value)
	{
		try
		{
			ChatSettings.Save();
			ctx.Audit("chat setting", "server", null, $"{what} = {value}");
			ctx.Session.Status = $"Chat {what}: {value}";
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[Admin] Saving chat_settings.json failed");
			ctx.Session.Status = $"Saving chat_settings.json failed: {ex.Message}";
		}
	}
}
