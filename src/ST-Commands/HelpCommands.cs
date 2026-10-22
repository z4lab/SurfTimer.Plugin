using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using SurfTimer.Api;

namespace SurfTimer;

/// <summary>
/// !help: every command in a popup, a tab per group from basic to advanced (the timer's own and the ones addons register
/// through SurfTimer.Api), each with a details page; !help &lt;command&gt; prints one command's details in chat. New
/// players get a hint once (player_settings help_hint).
/// </summary>
public partial class SurfTimer
{
	/// <param name="Visible">Who sees it - null: everyone</param>
	private sealed record HelpItem(HelpEntry Entry, Func<CCSPlayerController, bool>? Visible);

	private static readonly Dictionary<string, HelpItem> HelpItems = new(StringComparer.OrdinalIgnoreCase);

	private const string KeyHelpHint = "help_hint";
	private const int HelpHintDelaySeconds = 8;
	private const int HelpLineLength = 60;

	private static readonly (HelpGroup Group, string Tab)[] HelpTabs =
	[
		(HelpGroup.Basics, "Basics"),
		(HelpGroup.Practice, "Practice"),
		(HelpGroup.Records, "Records"),
		(HelpGroup.Spectate, "Spectate"),
		(HelpGroup.Options, "Options"),
		(HelpGroup.Admin, "Admin"),
	];

	/// <summary>The timer's own commands, from basic to advanced within each tab</summary>
	private static void RegisterOwnHelp()
	{
		int order = 0;
		void Add(HelpGroup group, string command, string text, string usage = "", string[]? aliases = null, string detail = "",
			Func<CCSPlayerController, bool>? visible = null, string? flag = null) =>
			HelpItems[command] = new HelpItem(new HelpEntry(command, text, group, usage, aliases, detail, flag, order++), visible);

		// Basics
		Add(HelpGroup.Basics, "r", "back to the map start",
			detail: "Back to the map start, also out of spectator. During a run type it twice within 3 seconds - the first one only warns (Options › Gameplay › Confirm !r).");
		Add(HelpGroup.Basics, "rs", "back to the start of your stage / bonus", aliases: ["back"],
			detail: "Back to the start of the stage or bonus you're on, otherwise the map start. From spectator: back to where you were, a paused run goes on.");
		Add(HelpGroup.Basics, "s", "teleport to a stage", "[n]", ["stage"],
			"Teleports you to stage n and resets your timer (stage mode). Staged maps only. Without a number: a picker with every stage and its tier.");
		Add(HelpGroup.Basics, "b", "teleport to a bonus", "[n]", ["bonus"],
			"Teleports you to bonus n - you stay on it until !r, !s or !b. Without a number: bonus 1, or a picker when the map has several.");
		Add(HelpGroup.Basics, "end", "teleport to the end of your course",
			detail: "From the start zone of your course (map, bonus, or the stage in stage mode): teleports you to its end. The timer stops and doesn't finish there. Several end zones: pick one.");
		Add(HelpGroup.Basics, "options", "your settings: HUD, visibility, chat, sound", aliases: ["settings"],
			detail: "HUD layout (bottom and center fields), speed colors, splits panel, hiding players / bots, trails, chat messages, sounds.");
		Add(HelpGroup.Basics, "help", "this list", "[command]",
			detail: "Without a command: every command, by tab. With a command (e.g. !help saveloc): its details in chat.");

		// Practice
		Add(HelpGroup.Practice, "saveloc", "save your position", aliases: ["sl"],
			detail: "Saves your position, view, speed and run as a server-wide #id. While spectating it saves who you watch. In a start zone you must stand still. Loaded runs are practice.");
		Add(HelpGroup.Practice, "tele", "back to your saveloc, or load one", "[#id]", ["tp"],
			"No id: back to your current saveloc. With an id: loads that saveloc, also someone else's (it's added to your set).");
		Add(HelpGroup.Practice, "teleprev", "previous saveloc in your set");
		Add(HelpGroup.Practice, "telenext", "next saveloc in your set");
		Add(HelpGroup.Practice, "saveloclist", "saveloc menu", aliases: ["slm"],
			detail: "Tabs: My set, Recent (everyone's latest), Import, Info.");
		Add(HelpGroup.Practice, "noclip", "fly - turns your timer off until !r", aliases: ["nc"],
			detail: "Noclip on / off. Your run is reset and the timer stays off (no zones, no savelocs) until !r or !rs, which also end noclip. !noclip again lands you where you are. Map teleports: Options › Gameplay › Noclip teleports (normal / set back / disabled).");
		Add(HelpGroup.Practice, "repeat", "repeat mode on / off",
			detail: "After each stage you go back to that stage's start. Staged maps only, off every time you join.");

		// Records
		Add(HelpGroup.Records, "rank", "your rank on this map");
		Add(HelpGroup.Records, "map", "map panel: info, records, stages, bonuses", "[map]", ["mapinfo", "mi", "tier", "difficulty"],
			"The map panel for this map or another one. Tabs: Map, Records, Stages, Checkpoints, Bonuses.");
		Add(HelpGroup.Records, "maptop", "map records", "[map]", ["mtop"]);
		Add(HelpGroup.Records, "stagetop", "stage records", "[map] [n]", ["stop"], "With a number: that stage's board.");
		Add(HelpGroup.Records, "btop", "bonus records", "[map] [n]", ["bonustop"]);
		Add(HelpGroup.Records, "cptop", "checkpoint records", "[map] [n]", ["ctop"]);
		Add(HelpGroup.Records, "profile", "your profile or another player's", "[name]", ["p"],
			"Online or offline players. Tabs: Overview, Points, Records, Recent, This map, Tiers.");
		Add(HelpGroup.Records, "top", "top players by server rank",
			detail: "Normal style. A player opens their profile.");

		// Spectate
		Add(HelpGroup.Spectate, "spec", "spectate a player or bot", "[name]",
			detail: "By (partial) name, or a picker without one. Your run is paused - !back continues it.");
		Add(HelpGroup.Spectate, "replay", "replay menu",
			detail: "Tabs: Map (WR, best segments, your PB), Stages, Bonuses, Checkpoints, Your PBs.");
		Add(HelpGroup.Spectate, "rbpause", "pause the replay you watch", aliases: ["replaybotpause"]);
		Add(HelpGroup.Spectate, "rbplay", "restart the replay you watch");
		Add(HelpGroup.Spectate, "rbflip", "play the replay you watch backwards / forwards", aliases: ["replaybotflip"]);

		// Options
		Add(HelpGroup.Options, "hide", "hide other players on / off");
		Add(HelpGroup.Options, "hidebots", "hide replay bots on / off");
		Add(HelpGroup.Options, "hideself", "hide your own legs on / off");
		Add(HelpGroup.Options, "quake", "all timer sounds on / off");
		Add(HelpGroup.Options, "hudexport", "a code for all your HUD settings",
			detail: "Every !options - HUD setting as an 8 character code. Others take it with !hudimport <code>. Codes stay forever.");
		Add(HelpGroup.Options, "hudimport", "take someone's HUD settings", "<code>",
			detail: "Takes every HUD setting of a code from !hudexport - settings the code doesn't have go back to their defaults.");
		Add(HelpGroup.Options, "cursor", "mouse cursor for popup menus on / off",
			detail: "Click the popup with the mouse, or move with it open. Bindable: bind <key> css_cursor.");
		Add(HelpGroup.Options, "close", "close the popup menu");
		Add(HelpGroup.Options, "knife", "get a knife");
		Add(HelpGroup.Options, "pistol", "your team's default pistol", aliases: ["gun"]);
		Add(HelpGroup.Options, "usp", "get a USP-S");
		Add(HelpGroup.Options, "p2000", "get a P2000");
		Add(HelpGroup.Options, "glock", "get a Glock");

		// Admin
		Add(HelpGroup.Admin, "surfadmin", "the timer's admin panel", aliases: ["timeradmin"],
			detail: "Maps, zones, map settings, records, players, server settings - each tab needs its permission.",
			visible: AdminPermissions.CanOpen);
		Add(HelpGroup.Admin, "changemap", "change the map", "[map]", flag: AdminPermissions.ChangeMap,
			detail: "From the workshop collection - without a name it opens the map list.");
	}

	/// <summary>An addon's command (SurfTimer.Api RegisterHelp)</summary>
	internal static void RegisterAddonHelp(HelpEntry entry)
	{
		string command = entry.Command.TrimStart('!').Replace("css_", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
		if (command.Length == 0)
			return;
		HelpItems[command] = new HelpItem(entry with { Command = command }, null);
	}

	internal static void UnregisterAddonHelp(string command) =>
		HelpItems.Remove(command.TrimStart('!').Replace("css_", "", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Whether a player sees an entry: its own rule, else its flag (root has all) - admin entries without a flag need any
	/// admin permission.
	/// </summary>
	private static bool HelpVisible(HelpItem item, CCSPlayerController player)
	{
		if (item.Visible != null)
			return item.Visible(player);
		if (item.Entry.Flag is string flag)
			return AdminPermissions.Has(player, flag);
		return item.Entry.Group != HelpGroup.Admin
			|| AdminManager.PlayerHasPermissions(player, "@css/generic") || AdminPermissions.CanOpen(player);
	}

	private static IEnumerable<HelpItem> HelpFor(CCSPlayerController player, HelpGroup group) =>
		HelpItems.Values.Where(i => i.Entry.Group == group && HelpVisible(i, player))
			.OrderBy(i => i.Entry.Order).ThenBy(i => i.Entry.Command);

	private static string HelpUsage(HelpEntry entry) => entry.Usage.Length > 0 ? $"!{entry.Command} {entry.Usage}" : $"!{entry.Command}";

	private static string HelpAliases(HelpEntry entry) =>
		entry.Aliases is { Length: > 0 } aliases ? string.Join(" ", aliases.Select(a => "!" + a)) : "";

	/// <summary>A command or one of its aliases (with or without "!")</summary>
	private static HelpItem? FindHelp(string name)
	{
		name = name.TrimStart('!').ToLowerInvariant();
		return HelpItems.GetValueOrDefault(name)
			?? HelpItems.Values.FirstOrDefault(i => i.Entry.Aliases?.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)) == true);
	}

	[ConsoleCommand("css_help", "Every command - !help <command> for one command's details")]
	[CommandHelper(usage: "[command]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void HelpCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var p))
			return;

		if (command.ArgCount > 1)
		{
			string name = command.ArgByIndex(1);
			var item = FindHelp(name);
			if (item == null || !HelpVisible(item, player))
			{
				player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["help_unknown", name.TrimStart('!')]}");
				return;
			}

			var entry = item.Entry;
			string aliases = HelpAliases(entry);
			player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["help_detail", HelpUsage(entry), entry.Text]}");
			if (aliases.Length > 0)
				player.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["help_aliases", aliases]}");
			if (entry.Detail.Length > 0)
				player.PrintToChat($"{Config.PluginPrefix} {entry.Detail}");
			return;
		}

		// Built fresh every time - the tabs and admin entries depend on the player's permissions and loaded addons
		var sections = HelpTabs
			.Where(t => HelpFor(player, t.Group).Any())
			.Select(t => new PanelSection(t.Tab, null, () => HelpTabPage(t.Group, t.Tab)))
			.ToList();
		var session = new PanelSession("Help", p, sections);
		PanelReopen(session);
	}

	private PanelPage HelpTabPage(HelpGroup group, string tab) => new(tab, ctx =>
		HelpFor(ctx.Player.Controller, group)
			.Select(item => ctx.Nav(HelpUsage(item.Entry), HelpAliases(item.Entry), item.Entry.Text, () => HelpDetailPage(item.Entry)))
			.ToList());

	/// <summary>One command: usage, aliases, its details - and Run for commands that need no arguments</summary>
	private PanelPage HelpDetailPage(HelpEntry entry) => new($"!{entry.Command}", ctx =>
	{
		var rows = new List<HudMenuItem> { PanelContext.Info(HelpUsage(entry), "", entry.Text) };
		string aliases = HelpAliases(entry);
		if (aliases.Length > 0)
			rows.Add(PanelContext.Info("Aliases", aliases));
		rows.AddRange(WrapHelp(entry.Detail).Select(line => PanelContext.Info(line)));

		// Optional arguments only ([...]) - it runs as typed without them
		if (!entry.Usage.Contains('<'))
		{
			var controller = ctx.Player.Controller;
			rows.Add(ctx.Act("Run", HelpUsage(entry).Split(' ')[0], "", () =>
				Server.NextFrame(() =>
				{
					if (controller.IsValid)
						controller.ExecuteClientCommandFromServer($"css_{entry.Command}");
				}), closes: true));
		}
		return rows;
	});

	/// <summary>Detail text in popup rows of about HelpLineLength characters</summary>
	private static List<string> WrapHelp(string text)
	{
		var lines = new List<string>();
		var line = "";
		foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (line.Length > 0 && line.Length + 1 + word.Length > HelpLineLength)
			{
				lines.Add(line);
				line = "";
			}
			line = line.Length == 0 ? word : $"{line} {word}";
		}
		if (line.Length > 0)
			lines.Add(line);
		return lines;
	}

	/// <summary>First spawn: players who never saw it get "type !help" once, a few seconds in</summary>
	private void ShowHelpHint(Player p)
	{
		if (p.Controller.IsBot || p.Profile.Settings.ContainsKey(KeyHelpHint))
			return;

		p.Profile.SetSetting(KeyHelpHint, "1");
		var controller = p.Controller;
		AddTimer(HelpHintDelaySeconds, () =>
		{
			if (controller.IsValid)
				controller.PrintToChat($"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["help_hint"]}");
		}, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
	}
}
