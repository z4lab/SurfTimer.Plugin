using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Admin panel - Server - Trails: trails on/off, their look and the color per group (trail_settings.json).
/// Changes apply at once.
/// </summary>
public partial class SurfTimer
{
	private PanelPage AdminTrailsPage() => new("Trails", ctx =>
	{
		var settings = TrailSettings.Current;

		void Change(string what, string value, Action apply)
		{
			apply();
			settings.Clamp();
			SaveTrailSettings(ctx, what, value);
		}

		return
		[
			ctx.Toggle("Trails", settings.Enabled, "top 100, VIP / admin / root and replay bots", on =>
				Change("enabled", on ? "on" : "off", () => settings.Enabled = on)),
			PanelContext.Info("Length", $"{settings.LengthSeconds:0.0} s", $"{settings.SegmentCount} segments per trail"),
			ctx.Act("Length +0.5 s", "", "", () => Change("length", $"{settings.LengthSeconds + 0.5:0.0}s", () => settings.LengthSeconds += 0.5)),
			ctx.Act("Length -0.5 s", "", "", () => Change("length", $"{settings.LengthSeconds - 0.5:0.0}s", () => settings.LengthSeconds -= 0.5)),
			PanelContext.Info("Width", $"{settings.Width:0}"),
			ctx.Act("Width +1", "", "", () => Change("width", $"{settings.Width + 1:0}", () => settings.Width += 1)),
			ctx.Act("Width -1", "", "", () => Change("width", $"{settings.Width - 1:0}", () => settings.Width -= 1)),
			PanelContext.Info("Segment every", $"{settings.SegmentTicks} ticks", "smaller = smoother, more entities"),
			ctx.Act("Segments more often", "", "-2 ticks", () => Change("segment ticks", $"{settings.SegmentTicks - 2}", () => settings.SegmentTicks -= 2)),
			ctx.Act("Segments less often", "", "+2 ticks", () => Change("segment ticks", $"{settings.SegmentTicks + 2}", () => settings.SegmentTicks += 2)),
			ctx.Nav("Colors", "", "per rank group / role", AdminTrailColorsPage),
		];
	});

	private PanelPage AdminTrailColorsPage() => new("Trail colors", ctx =>
	{
		var colors = TrailSettings.Current.Colors;
		HudMenuItem Row(string label, Func<string> get, Action<string> set) =>
			ctx.Nav(label, TrailColors.Describe(get()), "", () => AdminTrailColorPicker(label, get, set));

		return
		[
			Row("#1", () => colors.First, v => colors.First = v),
			Row("#2", () => colors.Second, v => colors.Second = v),
			Row("#3", () => colors.Third, v => colors.Third = v),
			Row("Top 10", () => colors.Top10, v => colors.Top10 = v),
			Row("Top 50", () => colors.Top50, v => colors.Top50 = v),
			Row("Top 100", () => colors.Top100, v => colors.Top100 = v),
			Row("VIP", () => colors.Vip, v => colors.Vip = v),
			Row("Admin", () => colors.Admin, v => colors.Admin = v),
			Row("Root", () => colors.Root, v => colors.Root = v),
			Row("Replay bots", () => colors.Replay, v => colors.Replay = v),
		];
	});

	private PanelPage AdminTrailColorPicker(string label, Func<string> get, Action<string> set) => new(label, ctx =>
	{
		void Pick(string value)
		{
			set(value);
			SaveTrailSettings(ctx, $"color {label}", value);
			PanelBackFrom(ctx.Session, ctx.Page, $"{label}: {TrailColors.Describe(value)}");
		}

		var rows = new List<HudMenuItem>
		{
			ctx.Act("Rainbow", get() == TrailColors.Rainbow ? "current" : "", "", () => Pick(TrailColors.Rainbow)),
			ctx.Ask("Hex color", "", "e.g. #FF8800", "Type a color in chat as #RRGGBB, e.g. #FF8800", text =>
			{
				if (!TrailColors.IsHex(text))
					return "A color like #FF8800 (# and 6 hex digits)";
				set(text.ToUpperInvariant());
				SaveTrailSettings(ctx, $"color {label}", text.ToUpperInvariant());
				return null;
			}),
		};
		rows.AddRange(TrailColors.Palette.Select(c => ctx.Act(c.Name, get().Equals(c.Hex, StringComparison.OrdinalIgnoreCase) ? "current" : "", c.Hex,
			() => Pick(c.Hex))));
		return rows;
	});

	private void SaveTrailSettings(PanelContext ctx, string what, string value)
	{
		try
		{
			TrailSettings.Save();
			ctx.Audit("trail setting", "server", null, $"{what} = {value}");
			ctx.Session.Status = $"Trails {what}: {value}";
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[Admin] Saving trail_settings.json failed");
			ctx.Session.Status = $"Saving trail_settings.json failed: {ex.Message}";
		}
	}
}
