using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

/// <summary>
/// !options - the player's own options, same popup as !admin: visibility, HUD layout, chat messages and
/// repeat mode. Everything but repeat mode is saved (player_settings).
/// </summary>
public partial class SurfTimer
{
	[ConsoleCommand("css_options", "Your options: visibility, HUD, chat messages, repeat mode.")]
	[ConsoleCommand("css_settings", "Your options: visibility, HUD, chat messages, repeat mode.")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void OptionsCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || !playerList.TryGetValue(player.UserId ?? 0, out var oPlayer))
			return;

		// Kept while connected - reopens on the tab / page it was left on
		var session = oPlayer.OptionsPanel ??= new PanelSession("Options", oPlayer, OptionsSections());
		session.Status = "";
		PanelReopen(session);
	}

	private List<PanelSection> OptionsSections() =>
	[
		new("Visibility", null, OptionsVisibilityRoot),
		new("HUD", null, OptionsHudRoot),
		new("Chat", null, OptionsChatRoot),
		new("Gameplay", null, OptionsGameplayRoot),
	];

	// ---- Visibility ----

	private PanelPage OptionsVisibilityRoot() => new("Visibility", ctx =>
	{
		var player = ctx.Player;
		var options = player.Options;
		return
		[
			ctx.Toggle("Hide own legs", options.HideLegs, "first person only - others still see you", on =>
			{
				options.HideLegs = on;
				player.ApplySelfVisibility();
				ctx.Session.Status = on ? "Your legs are hidden" : "Your legs are visible";
			}),
			ctx.Toggle("Hide players", options.HidePlayers, "only for you - spectated players stay", on =>
			{
				options.HidePlayers = on;
				ctx.Session.Status = on ? "Other players are hidden" : "Other players are visible";
			}),
			ctx.Toggle("Hide replay bots", options.HideBots, "only for you - spectated bots stay", on =>
			{
				options.HideBots = on;
				ctx.Session.Status = on ? "Replay bots are hidden" : "Replay bots are visible";
			}),
		];
	});

	// ---- HUD ----

	private static readonly Dictionary<HudFieldKind, string> HudFieldLabels = new()
	{
		[HudFieldKind.Timer] = "Timer",
		[HudFieldKind.Speed] = "Speed",
		[HudFieldKind.Prespeed] = "Prespeed",
		[HudFieldKind.Keys] = "Keys",
		[HudFieldKind.Sync] = "Sync",
	};

	private static string DescribeHud(string fields) => string.Join(" | ", fields.Split('|')
		.Select(row => string.Join(", ", row.Split(',').Select(f => Enum.TryParse(f, true, out HudFieldKind kind) ? HudFieldLabels[kind] : f))));

	private PanelPage OptionsHudRoot() => new("HUD", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = new List<HudMenuItem>();

		if (!CustomHud.IsActive)
			rows.Add(PanelContext.Info("Custom HUD is off", "", "these options apply once the server enables it"));

		string? preset = PlayerOptions.HudPresets.FirstOrDefault(p => p.Fields == options.HudFields).Name;
		foreach (var (name, fields) in PlayerOptions.HudPresets)
		{
			rows.Add(ctx.Act(name, preset == name ? "current" : "", DescribeHud(fields), () =>
			{
				options.TrySetHudFields(fields);
				ctx.Session.Status = $"HUD layout: {name}";
			}));
		}

		rows.Add(ctx.Nav("Custom layout", preset ?? "custom", "fields, order and rows", OptionsHudFieldsPage));
		rows.Add(ctx.Toggle("Top bar", options.HudTop, "rank, PB and WR", on => options.HudTop = on));
		rows.Add(ctx.Toggle("Splits panel", options.HudSplits, "left side", on => options.HudSplits = on));
		rows.Add(ctx.Toggle("Spectator list", options.HudSpectators, "right side", on => options.HudSpectators = on));
		return rows;
	});

	/// <summary>Where a field is in the player's layout: (row, position) or null when hidden</summary>
	private static (int Row, int Index)? FindHudField(IReadOnlyList<IReadOnlyList<HudFieldKind>> rows, HudFieldKind field)
	{
		for (int r = 0; r < rows.Count; r++)
		{
			int i = rows[r].ToList().IndexOf(field);
			if (i >= 0)
				return (r, i);
		}
		return null;
	}

	private PanelPage OptionsHudFieldsPage() => new("Custom layout", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = Enum.GetValues<HudFieldKind>().Select(field =>
		{
			var at = FindHudField(options.HudRows, field);
			string where = at is (int row, int index) ? $"row {row + 1} · #{index + 1}" : "hidden";
			return ctx.Nav(HudFieldLabels[field], where, field == HudFieldKind.Timer ? "double width" : "", () => OptionsHudFieldPage(field));
		}).ToList();

		rows.Add(ctx.Act("Reset to default", "", DescribeHud(PlayerOptions.DefaultHudFields), () =>
		{
			options.TrySetHudFields(PlayerOptions.DefaultHudFields);
			ctx.Session.Status = "HUD layout reset";
		}));
		return rows;
	});

	private PanelPage OptionsHudFieldPage(HudFieldKind field) => new(HudFieldLabels[field], ctx =>
	{
		var options = ctx.Player.Options;
		var at = FindHudField(options.HudRows, field);
		string label = HudFieldLabels[field];

		// Edits a copy of the layout; a result that doesn't fit the HUD is refused
		void Change(Func<List<List<HudFieldKind>>, string?> edit, string done)
		{
			var layout = options.HudRows.Select(r => r.ToList()).ToList();
			string? error = edit(layout);
			if (error == null && !options.TrySetHudFields(PlayerOptions.Format(layout)))
				error = "That doesn't fit - a row holds 4 slots (the timer takes 2)";
			ctx.Session.Status = error ?? done;
		}

		var rows = new List<HudMenuItem>
		{
			ctx.Toggle("Show", at != null, at is (int r, int i) ? $"row {r + 1} · #{i + 1}" : "", on => Change(layout =>
			{
				if (!on)
				{
					layout.ForEach(row => row.Remove(field));
					return null;
				}

				// Into the first row with room, else a new row
				var free = layout.FirstOrDefault(row => row.Sum(PlayerOptions.Width) + PlayerOptions.Width(field) <= PlayerOptions.RowCapacity);
				if (free != null)
					free.Add(field);
				else if (layout.Count(row => row.Count > 0) < PlayerOptions.MaxRows)
					layout.Add([field]);
				else
					return "No room - hide another field first";
				return null;
			}, on ? $"{label} shown" : $"{label} hidden")),
		};

		if (at is (int rowIndex, int index))
		{
			rows.Add(ctx.Act("Move earlier", "", "left, or to the end of the row above", () => Change(layout =>
			{
				var row = layout[rowIndex];
				if (index > 0)
					(row[index - 1], row[index]) = (row[index], row[index - 1]);
				else if (rowIndex > 0)
				{
					row.RemoveAt(index);
					layout[rowIndex - 1].Add(field);
				}
				else
					return $"{label} is already first";
				return null;
			}, $"{label} moved earlier")));

			rows.Add(ctx.Act("Move later", "", "right, or to the start of the row below", () => Change(layout =>
			{
				var row = layout[rowIndex];
				if (index < row.Count - 1)
					(row[index + 1], row[index]) = (row[index], row[index + 1]);
				else if (rowIndex < PlayerOptions.MaxRows - 1)
				{
					row.RemoveAt(index);
					if (rowIndex + 1 >= layout.Count)
						layout.Add([]);
					layout[rowIndex + 1].Insert(0, field);
				}
				else
					return $"{label} is already last";
				return null;
			}, $"{label} moved later")));

			int otherRow = rowIndex == 0 ? 1 : 0;
			rows.Add(ctx.Act($"Move to row {otherRow + 1}", "", "", () => Change(layout =>
			{
				layout[rowIndex].Remove(field);
				while (layout.Count <= otherRow)
					layout.Add([]);
				layout[otherRow].Add(field);
				return null;
			}, $"{label} moved to row {otherRow + 1}")));
		}
		return rows;
	});

	// ---- Chat ----

	private PanelPage OptionsChatRoot() => new("Chat", ctx =>
	{
		var options = ctx.Player.Options;
		return
		[
			ctx.Toggle("Own split messages", options.ChatSplits, "checkpoint / stage comparisons", on => options.ChatSplits = on),
			ctx.Toggle("Others' PBs", options.ChatOthersPb, "personal bests of other players", on => options.ChatOthersPb = on),
			ctx.Toggle("Others' records", options.ChatOthersRecords, "WRs of other players", on => options.ChatOthersRecords = on),
			ctx.Toggle("Connect messages", options.ChatConnects, "players joining", on => options.ChatConnects = on),
		];
	});

	// ---- Gameplay ----

	private PanelPage OptionsGameplayRoot() => new("Gameplay", ctx =>
	{
		var player = ctx.Player;
		bool staged = CurrentMap != null && CurrentMap.Stages > 0;
		return
		[
			ctx.Toggle("Repeat mode", player.IsRepeatMode, staged ? "back to the stage start after each stage · not saved" : "staged maps only", on =>
			{
				string result = SetRepeat(player, on);
				ctx.Session.Status = result switch
				{
					"repeat_enabled" => "Repeat mode on",
					"not_staged" => "Repeat mode only works on staged maps",
					_ => "Repeat mode off",
				};
			}),
		];
	});
}
