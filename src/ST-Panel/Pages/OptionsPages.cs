using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

/// <summary>
/// !options - the player's own options, same popup as !surfadmin: visibility, HUD, compare, chat, gameplay and sound.
/// All saved (player_settings).
/// </summary>
public partial class SurfTimer
{
	[ConsoleCommand("css_options", "Your options: visibility, HUD, compare, chat, gameplay, sound.")]
	[ConsoleCommand("css_settings", "Your options: visibility, HUD, compare, chat, gameplay, sound.")]
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
		new("Compare", null, OptionsCompareRoot),
		new("Chat", null, OptionsChatRoot),
		new("Gameplay", null, OptionsGameplayRoot),
		new("Sound", null, OptionsSoundRoot),
	];

	// ---- Visibility ----

	private PanelPage OptionsVisibilityRoot() => new("Visibility", ctx =>
	{
		var player = ctx.Player;
		var options = player.Options;
		int overlays = OverlayKinds.All.Count(options.OverlayOn);
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
			ctx.Nav("Trails", "", "which trails you see, your trail color", OptionsTrailsPage),
			ctx.Act("Show zones", options.ZonesShow switch
			{
				ZoneDisplay.StartEnd => "start & end",
				ZoneDisplay.All => "all",
				_ => "off",
			}, "zone outlines - off / start & end / all", () =>
			{
				options.ZonesShow = (ZoneDisplay)(((int)options.ZonesShow + 1) % 3);
				ctx.Session.Status = options.ZonesShow == ZoneDisplay.Off ? "Zones hidden" : "Zones shown";
			}),
			ctx.Nav("Triggers & clips", overlays > 0 ? $"{overlays} / {OverlayKinds.All.Length} on" : "off",
				"map triggers and player clips - !showtriggers / !showclips", OptionsOverlayPage),
		];
	});

	// ---- Triggers & clips ----

	/// <summary>Triggers &amp; clips (MapOverlay.cs): every kind with whether it's shown and its color</summary>
	private PanelPage OptionsOverlayPage() => new("Triggers & clips", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = OverlayKinds.All.Select(kind => ctx.Nav(OverlayKinds.Label(kind),
			options.OverlayOn(kind) ? $"on · {TrailColors.Describe(OverlayKinds.ColorOf(options, kind))}" : "off",
			OverlayKinds.Sub(kind), () => OptionsOverlayKindPage(kind))).ToList();

		void All(bool on)
		{
			foreach (var kind in OverlayKinds.All)
				options.SetOverlayOn(kind, on);
			ctx.Session.Status = on ? "All shown" : "All hidden";
		}
		rows.Add(ctx.Act("All on", "", "", () => All(true)));
		rows.Add(ctx.Act("All off", "", "", () => All(false)));
		rows.Add(PanelContext.Info("Draw distance", $"{Config.OverlayDrawDistance} units", "around you - set by the server"));
		return rows;
	});

	/// <summary>One kind: shown or not, and its color - the default, a palette color or any hex color</summary>
	private PanelPage OptionsOverlayKindPage(OverlayKind kind) => new(OverlayKinds.Label(kind), ctx =>
	{
		var options = ctx.Player.Options;
		string label = OverlayKinds.Label(kind);
		string own = options.OverlayColor(kind);

		void Pick(string hex, string name)
		{
			options.SetOverlayColor(kind, hex);
			ctx.Session.Status = $"{label}: {name}";
		}

		var rows = new List<HudMenuItem>
		{
			ctx.Toggle("Show", options.OverlayOn(kind), OverlayKinds.Sub(kind), on =>
			{
				options.SetOverlayOn(kind, on);
				ctx.Session.Status = on ? $"{label} shown" : $"{label} hidden";
			}),
			ctx.Act("Default color", own.Length == 0 ? "current" : "", TrailColors.Describe(OverlayKinds.DefaultColor(kind)), () => Pick("", "default color")),
			ctx.Ask("Hex color", TrailColors.Describe(own).StartsWith('#') ? own : "", "e.g. #FF8800",
				"Type a color in chat as #RRGGBB, e.g. #FF8800", text =>
				{
					if (!TrailColors.IsHex(text))
						return "A color like #FF8800 (# and 6 hex digits)";
					Pick(text.ToUpperInvariant(), text.ToUpperInvariant());
					return null;
				}),
		};
		rows.AddRange(TrailColors.Palette.Select(c => ctx.Act(c.Name, own.Equals(c.Hex, StringComparison.OrdinalIgnoreCase) ? "current" : "", c.Hex,
			() => Pick(c.Hex, c.Name))));
		return rows;
	});

	// ---- Trails ----

	private PanelPage OptionsTrailsPage() => new("Trails", ctx =>
	{
		var player = ctx.Player;
		var options = player.Options;
		string? color = TrailColorFor(player);

		var rows = new List<HudMenuItem>
		{
			color != null
				? PanelContext.Info("Your trail", TrailColors.Describe(color), "top 100 / VIP / admin")
				: PanelContext.Info("Your trail", "none", "trails: top 100 and VIP / admin"),
		};

		if (color != null)
		{
			rows.Add(ctx.Toggle("My trail", options.TrailMine, "off = nobody sees it", on =>
			{
				options.TrailMine = on;
				ctx.Session.Status = on ? "Your trail is drawn" : "Your trail is off";
			}));
		}

		rows.Add(ctx.Toggle("Other players' trails", options.TrailsOthers, "", on => options.TrailsOthers = on));
		rows.Add(ctx.Toggle("My own trail", options.TrailsOwn, "see your trail yourself", on => options.TrailsOwn = on));
		rows.Add(ctx.Toggle("Spectated trail", options.TrailsSpectate, "trail of the player / bot you spectate", on => options.TrailsSpectate = on));
		rows.Add(ctx.Toggle("Replay bot trails", options.TrailsBots, "", on => options.TrailsBots = on));

		if (color != null && CanPickTrailColor(player))
			rows.Add(ctx.Nav("My trail color", options.TrailColor.Length > 0 ? TrailColors.Describe(options.TrailColor) : "group color", "top 3 / VIP / admin", OptionsTrailColorPage));
		return rows;
	});

	private PanelPage OptionsTrailColorPage() => new("Trail color", ctx =>
	{
		var options = ctx.Player.Options;

		void Pick(string value, string label)
		{
			options.TrailColor = value;
			ctx.Session.Status = value.Length == 0 ? "Your trail uses your group color" : $"Trail color: {label}";
		}

		var rows = new List<HudMenuItem>
		{
			ctx.Act("Group color", options.TrailColor.Length == 0 ? "current" : "", "your rank / role color", () => Pick("", "")),
			ctx.Act("Rainbow", options.TrailColor == TrailColors.Rainbow ? "current" : "", "cycles through all colors", () => Pick(TrailColors.Rainbow, "Rainbow")),
			ctx.Ask("Hex color", TrailColors.IsHex(options.TrailColor) && TrailColors.Describe(options.TrailColor).StartsWith('#') ? options.TrailColor : "", "e.g. #FF8800",
				"Type a color in chat as #RRGGBB, e.g. #FF8800", text =>
				{
					if (!TrailColors.IsHex(text))
						return "A color like #FF8800 (# and 6 hex digits)";
					Pick(text.ToUpperInvariant(), text.ToUpperInvariant());
					return null;
				}),
		};

		rows.AddRange(TrailColors.Palette.Select(c => ctx.Act(c.Name, options.TrailColor.Equals(c.Hex, StringComparison.OrdinalIgnoreCase) ? "current" : "", c.Hex,
			() => Pick(c.Hex, c.Name))));
		return rows;
	});

	// ---- HUD ----

	private static string DescribeHud(string fields) => string.Join(" | ", fields.Split('|')
		.Select(row => string.Join(", ", row.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(HudFieldCatalog.Label)))
		.Select(row => row.Length > 0 ? row : "-"));

	private static string SplitTargetLabel(SplitTarget target) => target switch
	{
		SplitTarget.Off => "off",
		SplitTarget.Pb => "PB",
		SplitTarget.Wr => "WR",
		SplitTarget.Top10 => "top 10",
		_ => $"G{target - SplitTarget.G1 + 1}",
	};

	/// <summary>What the splits panel compares the run against</summary>
	private PanelPage OptionsSplitTargetPage() => new("Splits panel", ctx =>
	{
		var options = ctx.Player.Options;
		int completions = CurrentMap?.MapCompletions.GetValueOrDefault(ctx.Style) ?? 0;

		HudMenuItem Row(SplitTarget target, string text, string sub) =>
			ctx.Act(text, options.HudSplitTarget == target ? "current" : "", sub, () =>
			{
				options.HudSplitTarget = target;
				PanelBackFrom(ctx.Session, ctx.Page, $"Splits panel: {SplitTargetLabel(target)}");
			});

		var rows = new List<HudMenuItem>
		{
			Row(SplitTarget.Off, "Off", "no splits panel"),
			Row(SplitTarget.Pb, "Personal best", "your own PB run"),
			Row(SplitTarget.Wr, "World record", ""),
			Row(SplitTarget.Top10, "Top 10", "#10, or one rank above you"),
		};
		for (int group = 1; group <= PointsCalculator.GroupCount; group++)
		{
			string range = completions < 11 ? "no groups on this map yet"
				: PointsCalculator.GroupFirstRank(completions, group) > completions ? "not reached on this map yet"
				: $"ranks {PointsCalculator.GroupFirstRank(completions, group)}-{Math.Min(PointsCalculator.GroupLastRank(completions, group), completions)} on this map";
			rows.Add(Row(SplitTarget.G1 + (group - 1), $"Group {group}", range));
		}
		return rows;
	});

	/// <summary>HUD: one page per part of the HUD, each with its position. What it compares against is the Compare tab.</summary>
	private PanelPage OptionsHudRoot() => new("HUD", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = new List<HudMenuItem>();

		if (!CustomHud.IsActive)
			rows.Add(PanelContext.Info("Custom HUD is off", "", "these options apply once the server enables it"));

		string? preset = PlayerOptions.HudPresets.FirstOrDefault(p => p.Fields == options.HudFields).Name;
		rows.Add(ctx.Nav("Bottom HUD", preset ?? "custom", "layout and position", OptionsBottomHudPage));
		rows.Add(ctx.Nav("Center HUD", options.CenterHud ? $"{options.CenterLines.Sum(l => l.Count)} fields" : "off",
			"fields around the crosshair", OptionsCenterHudPage));
		rows.Add(ctx.Nav("Top bar", options.HudTop ? "on" : "off", "rank, PB and WR", OptionsTopBarPage));
		rows.Add(ctx.Nav("Splits panel", SplitTargetLabel(options.HudSplitTarget), "left side", OptionsSplitsPanelPage));
		rows.Add(ctx.Nav("Spectator list", options.HudSpectators ? "on" : "off", "right side", OptionsSpectatorsPage));
		rows.Add(ctx.Nav("Speed", $"{Extensions.SpeedLabel(options.SpeedAxes)} · {SpeedColorLabel(options)}", "axes and color", OptionsSpeedPage));
		rows.Add(ctx.Nav("Share HUD", "", "your HUD as a code - !hudexport / !hudimport", OptionsShareHudPage));
		return rows;
	});

	/// <summary>Bottom HUD: a preset or a custom layout, and its height</summary>
	private PanelPage OptionsBottomHudPage() => new("Bottom HUD", ctx =>
	{
		var options = ctx.Player.Options;
		string? preset = PlayerOptions.HudPresets.FirstOrDefault(p => p.Fields == options.HudFields).Name;
		var rows = PlayerOptions.HudPresets.Select(p => ctx.Act(p.Name, preset == p.Name ? "current" : "", DescribeHud(p.Fields), () =>
		{
			options.TrySetHudFields(p.Fields);
			ctx.Session.Status = $"HUD layout: {p.Name}";
		})).ToList();
		rows.Add(ctx.Nav("Custom layout", preset ?? "custom", "fields, order and rows", () => OptionsLayoutPage(BottomLayout)));
		rows.Add(HudPositionRow(ctx, CustomHud.Center, "0-10 · 100-300px from the bottom"));
		return rows;
	});

	private PanelPage OptionsTopBarPage() => new("Top bar", ctx =>
	[
		ctx.Toggle("Top bar", ctx.Player.Options.HudTop, "rank, PB and WR", on => ctx.Player.Options.HudTop = on),
		HudPositionRow(ctx, CustomHud.Top, "0-10 · 40-240px from the top"),
	]);

	/// <summary>The splits panel's look - what it compares against is in the Compare tab</summary>
	private PanelPage OptionsSplitsPanelPage() => new("Splits panel", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = new List<HudMenuItem>
		{
			PanelContext.Info("Compares against", SplitTargetLabel(options.HudSplitTarget), "Compare tab › Splits panel"),
		};
		if (options.HudSplits)
			rows.Add(ctx.Toggle("Keep last splits", options.HudSplitsKeep, "after a fail / reset, until your next run starts",
				on => options.HudSplitsKeep = on));
		rows.Add(HudPositionRow(ctx, CustomHud.Left, "0-10 · 200-600px from the top"));
		return rows;
	});

	private PanelPage OptionsSpectatorsPage() => new("Spectator list", ctx =>
	[
		ctx.Toggle("Spectator list", ctx.Player.Options.HudSpectators, "right side", on => ctx.Player.Options.HudSpectators = on),
		HudPositionRow(ctx, CustomHud.Right, "0-10 · 200-600px from the top"),
	]);

	/// <summary>HUD codes (HudPresets.cs): all HUD and Compare settings as a code to share</summary>
	private PanelPage OptionsShareHudPage() => new("Share HUD", ctx =>
	[
		ctx.Act("Export HUD", "", "a code for all your HUD and Compare settings - !hudexport", () =>
		{
			ctx.Session.Status = "Making your HUD code…";
			ExportHud(ctx.Player, ctx.Session);
		}),
		ctx.Ask("Import HUD", "", "type a HUD code in chat - !hudimport <code>",
			LocalizationService.LocalizerNonNull["prompt_hud_code"], text =>
			{
				string? error = ImportHud(ctx.Player, text, ctx.Session);
				if (error == null)
					ctx.Session.Status = "Looking up the code…";
				return error;
			}),
	]);

	/// <summary>A HUD part's position: each click one step further down (top / sides) or up (bottom), after the last back to the default</summary>
	private static HudMenuItem HudPositionRow(PanelContext ctx, string slot, string sub)
	{
		var options = ctx.Player.Options;
		int? own = options.OwnHudPosition(slot);
		return ctx.Act("Position", own is int shift ? $"{shift}" : $"default ({CustomHud.SlotShift[slot]})", sub, () =>
		{
			int? next = own switch
			{
				null => 0,
				>= CustomHud.MaxShift => null,
				int value => value + 1,
			};
			options.SetHudPosition(slot, next);
			ctx.Session.Status = next is int n ? $"Position: {n}" : "Position: default";
		});
	}

	// ---- Compare ----

	/// <summary>Compare: what the run races against - the splits panel, pace, timer warning and the ghost</summary>
	private PanelPage OptionsCompareRoot() => new("Compare", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = new List<HudMenuItem>
		{
			ctx.Nav("Splits panel", SplitTargetLabel(options.HudSplitTarget), "left side - compare against", OptionsSplitTargetPage),
		};
		if (options.HudSplits)
		{
			rows.Add(ctx.Toggle("Pace", options.HudPace, "projected finish, live - under the splits", on => options.HudPace = on));
			if (options.HudPace)
				rows.Add(ctx.Toggle("Energy vs reference", options.HudPaceEnergy, "energy / speed vs the reference at the same spot",
					on => options.HudPaceEnergy = on));
		}
		rows.Add(ctx.Act("Timer warning", options.TimerWarning == TimerWarning.Wr ? "WR" : "PB", "the timer turns yellow once slower", () =>
		{
			options.TimerWarning = options.TimerWarning == TimerWarning.Wr ? TimerWarning.Pb : TimerWarning.Wr;
			ctx.Session.Status = options.TimerWarning == TimerWarning.Wr
				? "Timer turns yellow once slower than the WR"
				: "Timer turns yellow once slower than your PB";
		}));
		rows.Add(ctx.Nav("Ghost & path", GhostLabel(options), "a run's path ahead of you and a ghost racing you", OptionsGhostPage));
		return rows;
	});

	private static string SpeedColorLabel(SpeedColorMode mode, string staticColor) => mode switch
	{
		SpeedColorMode.GainLoss => "gain / loss",
		SpeedColorMode.Static => PlayerOptions.SpeedStaticColors.FirstOrDefault(c => c.Key == staticColor).Label ?? "static",
		_ => "gradient",
	};

	private static string SpeedColorLabel(PlayerOptions options) => SpeedColorLabel(options.SpeedColor, options.SpeedColorStatic);

	private static string CenterColorLabel(PlayerOptions options) => options.CenterSpeedColor is SpeedColorMode own
		? SpeedColorLabel(own, options.CenterSpeedColorStatic)
		: "as HUD speed";

	/// <summary>Center speed colour: the HUD speed's (default), or its own gradient / gain / loss / Nord colour</summary>
	private PanelPage OptionsCenterColorPage() => new("Center HUD color", ctx =>
	{
		var options = ctx.Player.Options;
		var own = options.CenterSpeedColor;
		var rows = new List<HudMenuItem>
		{
			ctx.Act("Same as HUD speed", own == null ? "current" : "", $"now: {SpeedColorLabel(options)}", () =>
			{
				options.CenterSpeedColor = null;
				ctx.Session.Status = "Center HUD color: as the HUD speed";
			}),
			ctx.Act("Gradient", own == SpeedColorMode.Gradient ? "current" : "", "slow blue to fast red", () =>
			{
				options.CenterSpeedColor = SpeedColorMode.Gradient;
				ctx.Session.Status = "Center HUD color: gradient";
			}),
			ctx.Act("Gain / loss", own == SpeedColorMode.GainLoss ? "current" : "", "white, green gaining, red losing", () =>
			{
				options.CenterSpeedColor = SpeedColorMode.GainLoss;
				ctx.Session.Status = "Center HUD color: gain / loss";
			}),
		};
		foreach (var (key, label) in PlayerOptions.SpeedStaticColors)
		{
			bool current = own == SpeedColorMode.Static && options.CenterSpeedColorStatic == key;
			rows.Add(ctx.Act(label, current ? "current" : "", "one color", () =>
			{
				options.CenterSpeedColorStatic = key;
				options.CenterSpeedColor = SpeedColorMode.Static;
				ctx.Session.Status = $"Center HUD color: {label}";
			}));
		}
		return rows;
	});

	/// <summary>
	/// Speed: its axes, and the color of the HUD's speed, prespeed and center speed: Nord gradient, gain / loss, or one
	/// Nord color
	/// </summary>
	private PanelPage OptionsSpeedPage() => new("Speed", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = new List<HudMenuItem>
		{
			ctx.Act("Axes", Extensions.SpeedLabel(options.SpeedAxes), "XY horizontal · XYZ 3D · Z vertical", () =>
			{
				options.SpeedAxes = (SpeedAxes)(((int)options.SpeedAxes + 1) % 3);
				ctx.Session.Status = options.SpeedAxes switch
				{
					SpeedAxes.XYZ => "Speed shows XYZ (3D)",
					SpeedAxes.Z => "Speed shows Z (vertical, negative = falling)",
					_ => "Speed shows XY (horizontal)",
				};
			}),
			ctx.Act("Gradient", options.SpeedColor == SpeedColorMode.Gradient ? "current" : "", "slow blue to fast red", () =>
			{
				options.SpeedColor = SpeedColorMode.Gradient;
				ctx.Session.Status = "Speed color: gradient";
			}),
			ctx.Act("Gain / loss", options.SpeedColor == SpeedColorMode.GainLoss ? "current" : "", "white, green gaining, red losing", () =>
			{
				options.SpeedColor = SpeedColorMode.GainLoss;
				ctx.Session.Status = "Speed color: gain / loss";
			}),
		};

		// One color: picking it switches to it
		foreach (var (key, label) in PlayerOptions.SpeedStaticColors)
		{
			bool current = options.SpeedColor == SpeedColorMode.Static && options.SpeedColorStatic == key;
			rows.Add(ctx.Act(label, current ? "current" : "", "one color", () =>
			{
				options.SpeedColorStatic = key;
				options.SpeedColor = SpeedColorMode.Static;
				ctx.Session.Status = $"Speed color: {label}";
			}));
		}
		return rows;
	});

	// ---- Ghost & path ----

	private static string GhostLabel(PlayerOptions options) =>
		options.GhostPath && options.GhostOrb ? "path + ghost" : options.GhostPath ? "path" : options.GhostOrb ? "ghost" : "off";

	private static string GhostTargetLabel(GhostTarget target) => target switch
	{
		GhostTarget.Splits => "splits target",
		GhostTarget.Pb => "PB",
		GhostTarget.Wr => "WR",
		GhostTarget.NextRank => "next rank",
		GhostTarget.Top10 => "top 10",
		_ => $"G{target - GhostTarget.G1 + 1}",
	};

	/// <summary>
	/// Ghost &amp; path (GhostPath.cs): the path of a run ahead of you (coloured by its speed, jump / crouch markers) and an
	/// orb racing you - only you and your spectators see them.
	/// </summary>
	private PanelPage OptionsGhostPage() => new("Ghost & path", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = new List<HudMenuItem>
		{
			ctx.Toggle("Path", options.GhostPath, "the run's path ahead, colored by its speed - in a start zone and runs",
				on => options.GhostPath = on),
		};
		if (options.GhostPath)
		{
			rows.Add(ctx.Toggle("Jump & crouch markers", options.GhostMarkers, "square: jump · triangle down / up: crouch / stand up",
				on => options.GhostMarkers = on));
			rows.Add(ctx.Act("Path length", $"{options.GhostSeconds}s", "of the run ahead of you - each press one step", () =>
			{
				var steps = PlayerOptions.GhostSecondSteps;
				options.GhostSeconds = steps[(Array.IndexOf(steps, options.GhostSeconds) + 1) % steps.Length];
				ctx.Session.Status = $"Path: {options.GhostSeconds}s ahead";
			}));
		}
		rows.Add(ctx.Toggle("Ghost", options.GhostOrb, "an orb where the run was at your time - while your timer runs",
			on => options.GhostOrb = on));
		rows.Add(ctx.Nav("Run", GhostTargetLabel(options.GhostTarget), "which run they follow", OptionsGhostTargetPage));
		rows.Add(PanelContext.Info("Following", GhostStatusOf(ctx.Player)));
		return rows;
	});

	private PanelPage OptionsGhostTargetPage() => new("Ghost run", ctx =>
	{
		var options = ctx.Player.Options;
		HudMenuItem Row(GhostTarget target, string text, string sub) =>
			ctx.Act(text, options.GhostTarget == target ? "current" : "", sub, () =>
			{
				options.GhostTarget = target;
				PanelBackFrom(ctx.Session, ctx.Page, $"Ghost & path follow: {GhostTargetLabel(target)}");
			});

		var rows = new List<HudMenuItem>
		{
			Row(GhostTarget.Splits, "Splits target", $"the splits panel's ({SplitTargetLabel(options.HudSplitTarget)})"),
			Row(GhostTarget.Pb, "Personal best", "your own PB run"),
			Row(GhostTarget.Wr, "World record", ""),
			Row(GhostTarget.NextRank, "Next rank", "the time one rank above yours"),
			Row(GhostTarget.Top10, "Top 10", "#10, or one rank above you"),
		};
		for (int group = 1; group <= PointsCalculator.GroupCount; group++)
			rows.Add(Row(GhostTarget.G1 + (group - 1), $"Group {group}", "its last rank"));
		rows.Add(PanelContext.Info("Stage / bonus mode", "", "that stage's / bonus's PB (Personal best) or WR (anything else)"));
		return rows;
	});

	/// <summary>
	/// A HUD layout the menus edit: the bottom HUD (2 rows of 4) or the center HUD (3 lines around the crosshair).
	/// </summary>
	private sealed record LayoutTarget(string Title, HudLayoutSpec Spec, Func<PlayerOptions, IReadOnlyList<IReadOnlyList<string>>> Rows,
		Func<PlayerOptions, string, bool> TrySet, string Default, Func<int, string> RowName, string FullError);

	private static readonly string[] CenterLineNames = ["above", "crosshair", "below"];

	private static readonly LayoutTarget BottomLayout = new("Custom layout", HudLayoutSpec.Bottom, o => o.HudRows,
		(o, fields) => o.TrySetHudFields(fields), PlayerOptions.DefaultHudFields, r => $"row {r + 1}",
		"That doesn't fit - a row holds 4 slots (the timer takes 2)");

	private static readonly LayoutTarget CenterLayout = new("Center fields", HudLayoutSpec.Center, o => o.CenterLines,
		(o, fields) => o.TrySetCenterFields(fields), PlayerOptions.DefaultCenterFields, r => $"line {r + 1} ({CenterLineNames[r]})",
		"That doesn't fit - a line holds 3 fields");

	/// <summary>Every field (the timer's and the addons'): where it is in this layout, each opens its page</summary>
	private PanelPage OptionsLayoutPage(LayoutTarget target) => new(target.Title, ctx =>
	{
		var options = ctx.Player.Options;
		var layout = target.Rows(options);
		var rows = HudFieldCatalog.All.Select(field =>
		{
			var at = HudLayoutSpec.Find(layout, field.Key);
			string where = at is (int row, int index) ? $"{target.RowName(row)} · #{index + 1}" : "hidden";
			string sub = target.Spec.Width(field.Key) > 1 ? $"{field.Description} · double width" : field.Description;
			return ctx.Nav(field.Label, where, sub, () => OptionsLayoutFieldPage(target, field.Key));
		}).ToList();

		rows.Add(ctx.Act("Reset to default", "", DescribeHud(target.Default), () =>
		{
			target.TrySet(options, target.Default);
			ctx.Session.Status = $"{target.Title}: reset";
		}));
		return rows;
	});

	/// <summary>One field in a layout: show / hide it, move it within and between rows</summary>
	private PanelPage OptionsLayoutFieldPage(LayoutTarget target, string key) => new(HudFieldCatalog.Label(key), ctx =>
	{
		var options = ctx.Player.Options;
		var spec = target.Spec;
		var at = HudLayoutSpec.Find(target.Rows(options), key);
		string label = HudFieldCatalog.Label(key);

		// Edits a copy of the layout; a result that doesn't fit the HUD is refused
		void Change(Func<List<List<string>>, string?> edit, string done)
		{
			var layout = target.Rows(options).Select(r => r.ToList()).ToList();
			string? error = edit(layout);
			if (error == null && !target.TrySet(options, spec.Format(layout)))
				error = target.FullError;
			ctx.Session.Status = error ?? done;
		}

		var rows = new List<HudMenuItem>
		{
			ctx.Toggle("Show", at != null, at is (int r, int i) ? $"{target.RowName(r)} · #{i + 1}" : "", on => Change(layout =>
			{
				if (!on)
				{
					layout.ForEach(row => row.Remove(key));
					return null;
				}

				// Into the first row with room, else a new row
				var free = layout.FirstOrDefault(row => row.Sum(spec.Width) + spec.Width(key) <= spec.Capacity);
				if (free != null)
					free.Add(key);
				else if (layout.Count < spec.Rows)
					layout.Add([key]);
				else
					return "No room - hide another field first";
				return null;
			}, on ? $"{label} shown" : $"{label} hidden")),
		};

		if (at is (int rowIndex, int index))
		{
			rows.Add(ctx.Act("Move earlier", "", $"left, or to the end of the {spec.RowWord} above", () => Change(layout =>
			{
				var row = layout[rowIndex];
				if (index > 0)
					(row[index - 1], row[index]) = (row[index], row[index - 1]);
				else if (rowIndex > 0)
				{
					row.RemoveAt(index);
					layout[rowIndex - 1].Add(key);
				}
				else
					return $"{label} is already first";
				return null;
			}, $"{label} moved earlier")));

			rows.Add(ctx.Act("Move later", "", $"right, or to the start of the {spec.RowWord} below", () => Change(layout =>
			{
				var row = layout[rowIndex];
				if (index < row.Count - 1)
					(row[index + 1], row[index]) = (row[index], row[index + 1]);
				else if (rowIndex < spec.Rows - 1)
				{
					row.RemoveAt(index);
					if (rowIndex + 1 >= layout.Count)
						layout.Add([]);
					layout[rowIndex + 1].Insert(0, key);
				}
				else
					return $"{label} is already last";
				return null;
			}, $"{label} moved later")));

			for (int other = 0; other < spec.Rows; other++)
			{
				if (other == rowIndex)
					continue;
				int to = other;
				rows.Add(ctx.Act($"Move to {target.RowName(to)}", "", "", () => Change(layout =>
				{
					layout[rowIndex].Remove(key);
					while (layout.Count <= to)
						layout.Add([]);
					layout[to].Add(key);
					return null;
				}, $"{label} moved to {target.RowName(to)}")));
			}
		}
		return rows;
	});

	/// <summary>
	/// Center HUD settings: fields in three lines around the crosshair (the speed above it by default) - per line size and
	/// height, labels, font and the speed's colour.
	/// </summary>
	private PanelPage OptionsCenterHudPage() => new("Center HUD", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = new List<HudMenuItem>();
		if (!CustomHud.IsActive)
			rows.Add(PanelContext.Info("Custom HUD is off", "", "the center HUD needs it"));
		rows.Add(ctx.Toggle("Center HUD", options.CenterHud, "fields around the crosshair", on => options.CenterHud = on));
		rows.Add(ctx.Nav("Fields", $"{options.CenterLines.Sum(l => l.Count)} shown", "which values, on which line", () => OptionsLayoutPage(CenterLayout)));
		for (int line = 0; line < CustomHud.MidSlots; line++)
		{
			int l = line;
			var fields = options.CenterLines.Count > l ? options.CenterLines[l] : [];
			string shown = fields.Count == 0 ? "empty" : string.Join(", ", fields.Select(HudFieldCatalog.Label));
			rows.Add(ctx.Nav($"Line {l + 1} ({CenterLineNames[l]})", $"size {options.CenterSize(l)}", shown, () => OptionsCenterLinePage(l)));
		}
		rows.Add(ctx.Toggle("Labels", options.CenterLabels, "\"Speed 1234\" instead of \"1234\"", on => options.CenterLabels = on));
		rows.Add(ctx.Toggle("Monospace", options.CenterSpeedMono, "digits keep their place", on => options.CenterSpeedMono = on));
		rows.Add(ctx.Nav("Speed color", CenterColorLabel(options), "same as the HUD speed, or its own", OptionsCenterColorPage));
		return rows;
	});

	/// <summary>One center line: its size and height</summary>
	private PanelPage OptionsCenterLinePage(int line) => new($"Line {line + 1} ({CenterLineNames[line]})", ctx =>
	{
		var options = ctx.Player.Options;
		string Position() => options.CenterOffset(line) switch
		{
			0 => "default",
			< 0 and var up => $"{-up} up",
			var down => $"{down} down",
		};

		return
		[
			ctx.Act("Size", $"{options.CenterSize(line)} / {PlayerOptions.CenterSizes}", "each press one step bigger, then the smallest", () =>
			{
				options.SetCenterSize(line, options.CenterSize(line) % PlayerOptions.CenterSizes + 1);
				ctx.Session.Status = $"Line {line + 1}: size {options.CenterSize(line)}";
			}),
			ctx.Act("Move up", Position(), $"up to {PlayerOptions.CenterMaxOffset} steps", () =>
			{
				options.SetCenterOffset(line, options.CenterOffset(line) - 1);
				ctx.Session.Status = $"Line {line + 1}: {Position()}";
			}),
			ctx.Act("Move down", Position(), $"up to {PlayerOptions.CenterMaxOffset} steps", () =>
			{
				options.SetCenterOffset(line, options.CenterOffset(line) + 1);
				ctx.Session.Status = $"Line {line + 1}: {Position()}";
			}),
			ctx.Act("Reset position", "", "its default height", () =>
			{
				options.SetCenterOffset(line, 0);
				ctx.Session.Status = $"Line {line + 1}: {Position()}";
			}),
		];
	});

	// ---- Chat ----

	/// <summary>The run messages, in the order of the Own / Others pages</summary>
	private static readonly (ChatEvent Event, string Label, string Sub)[] ChatEvents =
	[
		(ChatEvent.SplitMap, "Splits vs map PB / WR", "map run: your time at each CP / stage vs the map PB / WR"),
		(ChatEvent.SplitSegment, "Splits vs stage / CP PB / WR", "a stage / CP segment's time vs its own PB / WR"),
		(ChatEvent.MapFinish, "Map finish", "a finish without a PB"),
		(ChatEvent.MapPb, "Map PB", ""),
		(ChatEvent.MapWr, "Map WR", ""),
		(ChatEvent.StagePb, "Stage PB", ""),
		(ChatEvent.StageWr, "Stage WR", ""),
		(ChatEvent.CpPb, "CP PB", ""),
		(ChatEvent.CpWr, "CP WR", ""),
		(ChatEvent.BonusFinish, "Bonus finish", "a finish without a PB"),
		(ChatEvent.BonusPb, "Bonus PB", ""),
		(ChatEvent.BonusWr, "Bonus WR", ""),
	];

	private PanelPage OptionsChatRoot() => new("Chat", ctx =>
	{
		var options = ctx.Player.Options;
		int own = ChatEvents.Count(e => options.ChatOwn(e.Event));
		int others = ChatEvents.Count(e => options.ChatOthers(e.Event));
		string role = SurfTimerApiImpl.ChatTagTextOf(ctx.Player.Controller);
		bool roleColor = SurfTimerApiImpl.ChatNameColorOf(ctx.Player.Controller) != null;
		var rows = new List<HudMenuItem>
		{
			ctx.Nav("Own messages", $"{own} / {ChatEvents.Length} on", "your runs - and whoever you spectate (players and replay bots)",
				() => OptionsChatEventsPage(own: true)),
			ctx.Nav("Others' messages", $"{others} / {ChatEvents.Length} on", "other players' runs", () => OptionsChatEventsPage(own: false)),
			ctx.Toggle("Compact messages", options.ChatCompact, "short run messages, e.g. \"CP1 WR 11.609 [1/6] -00.046\"",
				on => options.ChatCompact = on),
			ctx.Toggle("Connect messages", options.ChatConnects, "players joining", on => options.ChatConnects = on),
			ctx.Toggle("Saveloc messages", options.ChatSaveloc, "\"Teleported to #N\" when loading a saveloc", on => options.ChatSaveloc = on),
		};
		// Only for players with a role (e.g. [Admin] / a name color from the admin plugin's groups)
		if (role.Length > 0 || roleColor)
		{
			string sub = role.Length > 0 ? $"{role} before your name{(roleColor ? " and your name color" : "")}" : "your role's name color";
			rows.Add(ctx.Toggle("Show my role", !options.HideRoleTag, sub, on => options.HideRoleTag = !on));
		}
		return rows;
	});

	/// <summary>Every run message on / off - for your own runs (and whoever you spectate) or for other players'</summary>
	private PanelPage OptionsChatEventsPage(bool own) => new(own ? "Own messages" : "Others' messages", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = ChatEvents.Select(e => ctx.Toggle(e.Label, own ? options.ChatOwn(e.Event) : options.ChatOthers(e.Event), e.Sub, on =>
		{
			if (own)
				options.SetChatOwn(e.Event, on);
			else
				options.SetChatOthers(e.Event, on);
		})).ToList();

		void All(bool on)
		{
			foreach (var e in ChatEvents)
			{
				if (own)
					options.SetChatOwn(e.Event, on);
				else
					options.SetChatOthers(e.Event, on);
			}
			ctx.Session.Status = on ? "All on" : "All off";
		}
		rows.Add(ctx.Act("All on", "", "", () => All(true)));
		rows.Add(ctx.Act("All off", "", "", () => All(false)));
		return rows;
	});

	// ---- Gameplay ----

	private PanelPage OptionsGameplayRoot() => new("Gameplay", ctx =>
	{
		var options = ctx.Player.Options;
		return
		[
			ctx.Toggle("Confirm !r", options.ConfirmReset, "during a run: !r twice within 3 s to reset", on => options.ConfirmReset = on),
			ctx.Act("Noclip teleports", options.NoclipTeleports switch
			{
				NoclipTeleports.Normal => "normal",
				NoclipTeleports.Disabled => "disabled",
				_ => "set back",
			}, "map teleports in noclip: work / you're put back / ignored", () =>
			{
				options.NoclipTeleports = (NoclipTeleports)(((int)options.NoclipTeleports + 1) % 3);
				if (options.NoclipTeleports == NoclipTeleports.Disabled)
					EnsureTeleportTriggerHook();
				ctx.Session.Status = options.NoclipTeleports switch
				{
					NoclipTeleports.Normal => "Map teleports work in noclip",
					NoclipTeleports.Disabled => "Map teleports are ignored in noclip",
					_ => "Map teleports are undone in noclip",
				};
			}),
		];
	});

	// ---- Sound ----

	/// <summary>
	/// Sound tab: the main switch (also !quake), the volume, and each kind of sound - the timer's, then the addons'.
	/// </summary>
	private PanelPage OptionsSoundRoot() => new("Sound", ctx =>
	{
		var options = ctx.Player.Options;
		var rows = new List<HudMenuItem>
		{
			ctx.Toggle("Sounds", options.SoundsEnabled, "all timer sounds · same as !quake", on => options.SoundsEnabled = on),
		};
		if (!options.SoundsEnabled)
			return rows;

		// Each press one step louder, after 100% back to off - plays a sample at the new volume
		rows.Add(ctx.Act("Volume", options.SoundVolume == 0 ? "off" : $"{options.SoundVolume}%", "every sound below", () =>
		{
			int next = PlayerOptions.SoundVolumeSteps.FirstOrDefault(v => v > options.SoundVolume, 0);
			options.SoundVolume = next;
			Sounds.Play(ctx.Player, Sounds.Pb.Event, 1f, null);
			ctx.Session.Status = next == 0 ? "Volume: off" : $"Volume: {next}%";
		}));

		foreach (var category in Sounds.Categories)
		{
			var kind = category;
			rows.Add(ctx.Toggle(kind.Label, options.SoundOn(kind), kind.Sub, on =>
			{
				options.SetSoundOn(kind, on);
				if (on)
					Sounds.Play(ctx.Player, kind.Event, kind.Pitch, null); // A sample
			}));
		}
		return rows;
	});
}
