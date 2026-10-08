using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Globalization;
using System.Net;
using SurfTimer.Api;

namespace SurfTimer;

public class PlayerHud
{
	private readonly Player _player;
	private readonly string TimerColor = "#4FC3F7";
	private readonly string TimerColorPractice = "#BA68C8";
	private readonly string TimerColorActive = "#43A047";
	private readonly string RankColorPb = "#7986CB";
	private readonly string RankColorWr = "#FFD700";
	private readonly string SpectatorColor = "#9E9E9E";
	private readonly string SlowerColor = "#E53935";

	internal PlayerHud(Player Player)
	{
		_player = Player;
	}

	private static string FormatHUDElementHTML(
		string title,
		string body,
		string color,
		string size = "m"
	)
	{
		if (title != "")
		{
			if (size == "m")
				return $"{title}: <font color='{color}'>{body}</font>";
			else
				return $"<font class='fontSize-{size.ToLower()}'>{title}: <font color='{color}'>{body}</font></font>";
		}
		else
		{
			if (size == "m")
				return $"<font color='{color}'>{body}</font>";
			else
				return $"<font class='fontSize-{size.ToLower()}' color='{color}'>{body}</font>";
		}
	}

	/// <summary>
	/// Formats the given time in ticks into a readable time string.
	/// Unless specified differently, the default formatting will be `Compact`.
	/// Check <see cref="PlayerTimer.TimeFormatStyle"/> for all formatting types.
	/// </summary>
	public static string FormatTime(
		int ticks,
		PlayerTimer.TimeFormatStyle style = PlayerTimer.TimeFormatStyle.Compact
	)
	{
		TimeSpan time = TimeSpan.FromSeconds(ticks / 64.0);
		int millis = (int)(ticks % 64 * (1000.0 / 64.0));

		switch (style)
		{
			case PlayerTimer.TimeFormatStyle.Compact:
				return time.TotalMinutes < 1
					? $"{time.Seconds:D2}.{millis:D3}"
					: $"{time.Minutes:D1}:{time.Seconds:D2}.{millis:D3}";
			case PlayerTimer.TimeFormatStyle.Full:
				return time.TotalHours < 1
					? $"{time.Minutes:D2}:{time.Seconds:D2}.{millis:D3}"
					: $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{millis:D3}";
			case PlayerTimer.TimeFormatStyle.Verbose:
				return $"{time.Hours}h {time.Minutes}m {time.Seconds}s {millis}ms";
			default:
				throw new ArgumentException("Invalid time format style");
		}
	}

	/// <summary>
	/// One HUD value, independent of how it's drawn: rendered as HTML for the classic center HUD, or
	/// as plain label segments (title / body / suffix) with colour classes for the custom HUD.
	/// </summary>
	/// <param name="Color">Body colour as hex (HTML HUD)</param>
	/// <param name="ColorClass">Body colour class for the custom HUD - derived from Color when null</param>
	/// <param name="Size">Value size - the HTML HUD only distinguishes small from the rest</param>
	/// <param name="Label">Draw the body as a label (small caps, muted) in the custom HUD, e.g. headers</param>
	internal readonly record struct HudElement(string Title, string Body, string Color, string Suffix = "",
		HudSize Size = HudSize.Medium, string? ColorClass = null, bool Label = false);

	internal enum HudSize { Small, Medium, Large, XLarge }

	// Values can contain player names, so they're escaped for the HTML HUD (the custom HUD is plain text)
	private static string ToHtml(HudElement e) =>
		FormatHUDElementHTML(WebUtility.HtmlEncode(e.Title), WebUtility.HtmlEncode(e.Body), e.Color, e.Size == HudSize.Small ? "s" : "m")
		+ WebUtility.HtmlEncode(e.Suffix);

	// ---- Center notifications (zone messages) ----

	/// <summary>
	/// Zone info messages ("Map Start", ...). Shown in CS2's center print with the classic HUD; the
	/// custom HUD drops them - its top bar already shows where the player is.
	/// </summary>
	internal void Notify(string text)
	{
		if (!CustomHud.IsActive)
			_player.Controller.PrintToCenter(text);
	}

	/// <summary>
	/// Prespeed when leaving a start zone - kept for the custom HUD's prespeed field, and center printed
	/// with the classic HUD.
	/// </summary>
	/// <param name="context">"Stage 2", "Checkpoint 3", ... or empty</param>
	internal void NotifyPrespeed(string context, VectorT velocity)
	{
		_player.LastPrespeed = velocity;

		if (!CustomHud.IsActive)
		{
			var axes = _player.Options.SpeedAxes;
			_player.Controller.PrintToCenter($"{(context != "" ? context + " - " : "")}Prespeed {Extensions.SpeedLabel(axes)}: {Extensions.Speed(velocity, axes):0} u/s");
		}
	}

	/// <summary>
	/// Timer colour: idle, running or running in practice mode.
	/// </summary>
	private string TimerColorOf(Player p)
	{
		if (!p.Timer.IsRunning)
			return TimerColor;
		return p.Timer.IsPracticeMode ? TimerColorPractice : TimerColorActive;
	}

	/// <summary>
	/// The timer with a prefix based on mode ([P], [B#], [S#])
	/// </summary>
	private HudElement TimerElement(Player p)
	{
		string prefix = "";

		if (p.Timer.IsPracticeMode)
			prefix += "[P] ";

		if (p.Timer.IsBonusMode)
			prefix += $"[B{p.Timer.Bonus}] ";
		else if (p.Timer.IsStageMode)
			prefix += $"[S{p.Timer.Stage}] ";

		return new HudElement("", prefix + FormatTime(p.Timer.Ticks), TimerColorOf(p), Size: HudSize.XLarge);
	}

	private static HudElement SpeedElement(float velocity, SpeedAxes axes) =>
		new($"Speed {Extensions.SpeedLabel(axes)}", velocity.ToString("0"), Extensions.GetSpeedColorGradient(velocity), " u/s",
			Size: HudSize.Large, ColorClass: CustomHud.SpeedColorClass(velocity));

	// The HUD runs every tick, so a bonus/stage index that's unset (0) or has no data must fall back to
	// the map values instead of throwing - one exception here aborts the whole tick for everyone.
	private static bool HasBonusData(Player p, int style) =>
		HasEntry(p.Stats.BonusPB, p.Timer.Bonus, style)
		&& HasEntry(SurfTimer.CurrentMap.BonusWR, p.Timer.Bonus, style)
		&& HasEntry(SurfTimer.CurrentMap.BonusCompletions, p.Timer.Bonus, style);

	private static bool HasStageData(Player p, int style) =>
		HasEntry(p.Stats.StagePB, p.Timer.Stage, style)
		&& HasEntry(SurfTimer.CurrentMap.StageWR, p.Timer.Stage, style)
		&& HasEntry(SurfTimer.CurrentMap.StageCompletions, p.Timer.Stage, style);

	private static bool HasEntry<T>(Dictionary<int, T>[] byIndex, int index, int style) =>
		index > 0 && index < byIndex.Length && byIndex[index] != null && byIndex[index].ContainsKey(style);

	/// <summary>
	/// Rank for the current mode (map / stage / bonus)
	/// </summary>
	private HudElement RankElement(Player p)
	{
		int style = p.Timer.Style;
		var map = SurfTimer.CurrentMap;

		if (p.Timer.IsBonusMode && HasBonusData(p, style))
			return RankOf(p.Stats.BonusPB[p.Timer.Bonus][style], map.BonusWR[p.Timer.Bonus][style].ID, map.BonusCompletions[p.Timer.Bonus][style]);

		if (p.Timer.IsStageMode && HasStageData(p, style))
			return RankOf(p.Stats.StagePB[p.Timer.Stage][style], map.StageWR[p.Timer.Stage][style].ID, map.StageCompletions[p.Timer.Stage][style]);

		return RankOf(p.Stats.PB[style], map.WR[style].ID, map.MapCompletions[style]);
	}

	// "rank/completions", "-/completions" without a PB, "N/A" without any record
	private HudElement RankOf(PersonalBest pb, int wrId, int completions)
	{
		if (wrId == -1)
			return new HudElement("Rank", "N/A", RankColorPb);
		return new HudElement("Rank", pb.ID != -1 ? $"{pb.Rank}/{completions}" : $"-/{completions}", RankColorPb);
	}

	/// <summary>
	/// PB for the current mode (map / stage / bonus)
	/// </summary>
	private HudElement PbElement(Player p, PlayerTimer.TimeFormatStyle timeFormat = PlayerTimer.TimeFormatStyle.Compact)
	{
		int style = p.Timer.Style;

		int runTime = p.Stats.PB[style].RunTime;
		if (p.Timer.IsBonusMode && HasBonusData(p, style)) // Show corresponding bonus values
			runTime = p.Stats.BonusPB[p.Timer.Bonus][style].RunTime;
		else if (p.Timer.IsStageMode && HasStageData(p, style)) // Show corresponding stage values
			runTime = p.Stats.StagePB[p.Timer.Stage][style].RunTime;

		return new HudElement("PB", runTime > 0 ? FormatTime(runTime, timeFormat) : "N/A", RankColorPb);
	}

	/// <summary>
	/// WR for the current mode (map / stage / bonus)
	/// </summary>
	private HudElement WrElement(Player p, PlayerTimer.TimeFormatStyle timeFormat = PlayerTimer.TimeFormatStyle.Compact)
	{
		int style = p.Timer.Style;
		var map = SurfTimer.CurrentMap;

		int runTime = map.WR[style].RunTime;
		if (p.Timer.IsBonusMode && HasBonusData(p, style)) // Show corresponding bonus values
			runTime = map.BonusWR[p.Timer.Bonus][style].RunTime;
		else if (p.Timer.IsStageMode && HasStageData(p, style)) // Show corresponding stage values
			runTime = map.StageWR[p.Timer.Stage][style].RunTime;

		return new HudElement("WR", runTime > 0 ? FormatTime(runTime, timeFormat) : "N/A", RankColorWr);
	}

	/// <summary>
	/// Displays the HUD for the client - through the custom HUD slots when that's active, otherwise
	/// as the classic center HTML HUD.
	/// </summary>
	/// <param name="allPlayers">Everyone on the server - used to list who's spectating this player</param>
	internal void Display(ICollection<Player> allPlayers)
	{
		if (!_player.Controller.IsValid)
			return;

		if (CustomHud.IsActive)
		{
			if (Server.TickCount % CustomHudUpdateTicks == 0)
				DisplayCustomHud(allPlayers);
			return;
		}

		string hud = BuildCenterHud();
		if (!string.IsNullOrEmpty(hud))
			_player.Controller.PrintToCenterHtml(hud);
	}

	/// <summary>
	/// The classic center HTML HUD as rows of elements: timer, speed, PB + rank and WR while alive, or
	/// the replay info while spectating a replay bot.
	/// </summary>
	private List<List<HudElement>> CenterRows()
	{
		if (_player.Controller.PawnIsAlive)
		{
			return
			[
				[TimerElement(_player)],
				[SpeedElement(Extensions.SpeedOf(_player.Controller, _player.Options.SpeedAxes), _player.Options.SpeedAxes)],
				[PbElement(_player), RankElement(_player)],
				[WrElement(_player)],
			];
		}

		if (_player.Controller.Team == CsTeam.Spectator)
		{
			ReplayPlayer? specReplay = SurfTimer.CurrentMap.ReplayManager.Pool.Find(x =>
				x.Controller != null && _player.IsSpectating(x.Controller)
			);
			if (specReplay != null)
				return ReplayRows(specReplay);
		}

		return [];
	}

	private string BuildCenterHud() =>
		string.Join("<br>", CenterRows().Select(row => string.Join(" | ", row.Select(ToHtml))));

	/// <summary>
	/// The spectator HUD for whichever replay a pool slot is currently playing - covers
	/// Map/Stage/Bonus/Checkpoint content, both WR and a specific player's PB.
	/// </summary>
	/// <param name="specReplay">Pool slot to use</param>
	private List<List<HudElement>> ReplayRows(ReplayPlayer specReplay)
	{
		string replayType = ReplayTypeLabel(specReplay);
		if (replayType == "")
			return []; // Invalid type

		var axes = _player.Options.SpeedAxes;
		float velocity = Extensions.SpeedOf(specReplay.Controller!, axes);
		string timerColor = ReplayTimerColor(specReplay);

		return
		[
			[new HudElement("", replayType, SpectatorColor, Label: true)],
			[new HudElement("", specReplay.RecordPlayerName ?? "", RankColorWr)],
			[new HudElement("", $"{FormatTime(specReplay.ReplayCurrentRunTime)} / {FormatTime(specReplay.RecordRunTime)}", timerColor, Size: HudSize.Large)],
			[SpeedElement(velocity, axes)],
			[new HudElement("Cycle", $"{specReplay.RepeatCount}", SpectatorColor, Size: HudSize.Small)],
		];
	}

	/// <summary>
	/// "Map WR Replay", "Stage 3 PB Replay", ... - empty for an unknown replay type.
	/// </summary>
	internal static string ReplayTypeLabel(ReplayPlayer replay)
	{
		string kind = replay.RequestedByPlayerId == -1 ? "WR" : "PB";
		return replay.Type switch
		{
			0 => $"Map {kind} Replay",
			1 => $"Bonus {replay.Stage} {kind} Replay",
			2 => $"Stage {replay.Stage} {kind} Replay",
			3 => $"Checkpoint {replay.Stage} {kind} Replay",
			ReplayManager.BestSegmentsType => $"{ReplayManager.BestSegmentsLabel} Replay",
			_ => "",
		};
	}

	// Green while playing, gold when idle at the end
	private string ReplayTimerColor(ReplayPlayer replay) =>
		replay.ReplayCurrentRunTime > 0 ? TimerColorActive : RankColorWr;

	// ---- Custom HUD (custom_hud_layout) ----
	// The layout's labels only take plain text (no HTML), so each slot is a grid of rows x segments:
	// every segment has its own text variable and colour class (see the z4lab-custom-ui repo and CustomHud.cs).

	/// <param name="Kind">lbl (small caps label), val (value) or unit - null for an unused segment</param>
	/// <param name="Size">sm / md / lg / xl - null for an unused segment</param>
	private readonly record struct HudSegment(string Text, string? ColorClass, string? Kind, string? Size);

	// Slot updates are sent 16x/s at most, and each segment's text/class only when it changed
	private const int CustomHudUpdateTicks = 4;
	private const int MaxSplitLines = 6;
	private const int MaxSpectatorLines = 8;
	private int _customHudGeneration = -1;
	private readonly Dictionary<string, string> _sentText = new();
	private readonly Dictionary<(string Id, string Class), bool> _sentClass = new();
	private readonly Dictionary<(string Id, string Group), string?> _sentExclusive = new();

	/// <summary>
	/// New HUD entity or forced resend (e.g. a player joined) - the client may have lost everything,
	/// so forget what was sent.
	/// </summary>
	private void SyncCustomHudGeneration()
	{
		if (_customHudGeneration == CustomHud.Generation)
			return;

		_sentText.Clear();
		_sentClass.Clear();
		_sentExclusive.Clear();
		_sentInputCapture = null;
		_customHudGeneration = CustomHud.Generation;
#if DEBUG
		CustomHud.LogDebug("[CustomHud] Full HUD send for {Player} (slot {Slot}, generation {Generation})",
			_player.Controller.PlayerName, _player.Controller.Slot, CustomHud.Generation);
#endif
	}

	private void DisplayCustomHud(ICollection<Player> allPlayers)
	{
		SyncCustomHudGeneration();

		// Whose data to show: our own while alive, otherwise whoever we spectate (player or replay bot)
		var (subject, replay) = ResolveSubject(allPlayers);

		// Panels the viewer turned off in !options (HUD) are sent empty, which collapses them
		var options = _player.Options;
		SendCenter(subject, replay);
		SendSlot(CustomHud.Top, options.HudTop ? TopRows(subject, replay) : []);
		// Left: the run's splits - or, spectating the best segments bot, every segment WR it chains
		var leftRows = subject != null ? SplitRowsKept(subject, options)
			: replay?.Type == ReplayManager.BestSegmentsType ? BestSegmentRows(replay)
			: [];
		SendSlot(CustomHud.Left, options.HudSplits ? leftRows : []);
		// Spectators of whoever this HUD shows - ourselves while alive, else the watched player / replay bot
		var watched = subject?.Controller ?? replay?.Controller;
		SendSlot(CustomHud.Right, options.HudSpectators ? SpectatorRows(allPlayers, watched) : []);
		SendMenu(); // Refreshes live values (e.g. !spec times) - clicks re-render right away

#if DEBUG
		if (Server.TickCount >= _debugNextSummaryTick)
		{
			_debugNextSummaryTick = Server.TickCount + 64 * 5;
			try // Diagnostics must never break the tick
			{
				string shown = subject != null ? subject.Controller.PlayerName : replay != null ? $"replay ({replay.RecordPlayerName})" : "(nothing)";
				CustomHud.LogDebug("[CustomHud] {Player} (slot {Slot}): sent {Texts} texts / {Classes} class changes in the last 5s | showing: {Shown} | entity: {State}",
					_player.Controller.PlayerName, _player.Controller.Slot, _debugTextsSent, _debugClassesSent, shown, CustomHud.DescribeEntityState());
			}
			catch (Exception ex)
			{
				CustomHud.LogDebug("[CustomHud] Debug summary failed: {Error}", ex.Message);
			}
			_debugTextsSent = 0;
			_debugClassesSent = 0;
		}
#endif
	}

#if DEBUG
	private int _debugNextSummaryTick;
	private int _debugTextsSent;
	private int _debugClassesSent;
#endif

	/// <summary>
	/// The player whose HUD data is shown: ourselves while alive, otherwise the player or replay bot
	/// we're spectating. (null, null) when there's nothing to show.
	/// </summary>
	private (Player? Player, ReplayPlayer? Replay) ResolveSubject(ICollection<Player> allPlayers)
	{
		if (_player.Controller.PawnIsAlive)
			return (_player, null);

		var target = _player.Controller.ObserverPawn.Value?.ObserverServices?.ObserverTarget;
		if (target == null || !target.IsValid)
			return (null, null);

		uint targetRaw = target.Raw;

		var replay = SurfTimer.CurrentMap.ReplayManager.Pool.Find(x =>
			x.Controller != null && x.Controller.IsValid && x.Controller.PlayerPawn.Raw == targetRaw);
		if (replay != null)
			return (null, replay);

		var spectated = allPlayers.FirstOrDefault(p =>
			p != _player && p.Controller.IsValid && p.Controller.PawnIsAlive && p.Controller.PlayerPawn.Raw == targetRaw);
		return (spectated, null);
	}

	/// <summary>
	/// One value segment in a center field.
	/// </summary>
	/// <param name="Dim">Released key</param>
	/// <param name="Key">Keyboard letter (spacing)</param>
	/// <param name="Mono">Monospace font (changing numbers)</param>
	private readonly record struct FieldSegment(string Text, string? ColorClass = null, bool Dim = false, bool Key = false, bool Mono = false);

	private static readonly (PlayerButtons Button, string Letter)[] Keys =
	[
		(PlayerButtons.Forward, "W"),
		(PlayerButtons.Moveleft, "A"),
		(PlayerButtons.Back, "S"),
		(PlayerButtons.Moveright, "D"),
		(PlayerButtons.Jump, "J"),
		(PlayerButtons.Duck, "C"),
	];

	private static readonly FieldSegment NotAvailable = new("N/A", "col-grey");

	/// <summary>
	/// One center field: a small caps label over its value segments.
	/// </summary>
	/// <param name="Wide">Spans the width of two fields</param>
	private readonly record struct HudField(string Label, List<FieldSegment> Segments, bool Wide = false);

	/// <summary>
	/// Bottom center, as rows of fields in the viewer's layout (!options - HUD; by default timer (wide) and
	/// speed, then prespeed, keys and sync).
	/// </summary>
	private void SendCenter(Player? subject, ReplayPlayer? replay)
	{
		string slotId = CustomHud.SlotId(CustomHud.Center);
		bool visible = (subject != null || (replay != null && ReplayTypeLabel(replay) != ""))
			&& _player.Options.HudRows.Count > 0; // A layout without fields hides the block

		SendClass(slotId, "hidden", !visible);
		SendExclusive(slotId, "shift", $"shift-{_player.Options.HudPosition(CustomHud.Center)}");
		if (!visible)
			return; // Keep the last texts - they're hidden anyway

		// The same values from a live player or a replay - the fields below don't care which
		int ticks;
		string timerColor;
		float velocity;
		float? prespeedSpeed;
		PlayerButtons? buttons;
		float? syncPercent;
		// Speeds on the viewer's axes (!options - HUD), also for whoever they spectate
		var axes = _player.Options.SpeedAxes;
		if (subject != null)
		{
			ticks = subject.Timer.Ticks;
			timerColor = TimerColorOf(subject);
			velocity = Extensions.SpeedOf(subject.Controller, axes);
			// Prespeed: live while in a start zone, then the exit speed
			prespeedSpeed = subject.IsTouchingAnyStartZone ? velocity
				: subject.LastPrespeed is VectorT exit ? Extensions.Speed(exit, axes) : null;
			buttons = subject.Controller.Buttons;
			syncPercent = subject.SyncPercent;
		}
		else
		{
			ticks = replay!.ReplayCurrentRunTime;
			timerColor = ReplayTimerColor(replay);
			velocity = Extensions.SpeedOf(replay.Controller!, axes);
			prespeedSpeed = replay.Prespeed(velocity, axes);
			// Replays recorded before buttons were stored: keys stay released, sync N/A
			buttons = replay.CurrentButtons();
			syncPercent = replay.CurrentSync();
		}

		var timer = new HudField("Timer",
			[new FieldSegment(FormatTime(ticks, PlayerTimer.TimeFormatStyle.Full), CustomHud.ColorClass(timerColor), Mono: true)], Wide: true);
		var speed = new HudField($"Speed {Extensions.SpeedLabel(axes)}",
			[new FieldSegment(velocity.ToString("0", CultureInfo.InvariantCulture), CustomHud.SpeedColorClass(velocity), Mono: true)]);

		var prespeed = new HudField("Prespeed",
			[prespeedSpeed != null
				? new FieldSegment(prespeedSpeed.Value.ToString("0", CultureInfo.InvariantCulture), CustomHud.SpeedColorClass(prespeedSpeed.Value), Mono: true)
				: NotAvailable]);

		var held = buttons ?? 0;
		var keys = new HudField("Keys",
			Keys.Select(k => new FieldSegment(k.Letter, Dim: !held.HasFlag(k.Button), Key: true)).ToList());

		var sync = new HudField("Sync",
			[syncPercent != null
				? new FieldSegment(syncPercent.Value.ToString("00.00", CultureInfo.InvariantCulture) + "%", Mono: true)
				: NotAvailable]);

		HudField FieldOf(HudFieldKind kind) => kind switch
		{
			HudFieldKind.Timer => timer,
			HudFieldKind.Speed => speed,
			HudFieldKind.Prespeed => prespeed,
			HudFieldKind.Keys => keys,
			_ => sync,
		};

		var rows = _player.Options.HudRows.Select(row => row.Select(FieldOf).ToList()).ToList();

		for (int r = 0; r < CustomHud.FieldRows; r++)
		{
			List<HudField> row = r < rows.Count ? rows[r] : [];
			SendClass(CustomHud.FieldRowId(r), "hidden", row.Count == 0);

			for (int f = 0; f < CustomHud.FieldsPerRow; f++)
				SendField(r, f, f < row.Count ? row[f] : null);
		}
	}

	/// <param name="field">null hides the field</param>
	private void SendField(int row, int index, HudField? field)
	{
		string fieldId = CustomHud.FieldId(row, index);
		SendClass(fieldId, "hidden", field == null);
		if (field == null)
			return;

		SendClass(fieldId, "wide", field.Value.Wide);
		SendText(CustomHud.FieldLabelId(row, index), field.Value.Label);

		var segments = field.Value.Segments;
		for (int s = 0; s < CustomHud.FieldSegments; s++)
		{
			string id = CustomHud.FieldSegmentId(row, index, s);
			FieldSegment segment = s < segments.Count ? segments[s] : new FieldSegment("");

			SendText(id, segment.Text);
			SendClass(id, "hidden", segment.Text.Length == 0);
			if (segment.Text.Length == 0)
				continue;

			SendExclusive(id, "color", segment.ColorClass);
			SendExclusive(id, "font", segment.Mono ? CustomHud.MonoFontClass : null);
			SendClass(id, "dim", segment.Dim);
			SendClass(id, "key", segment.Key);
		}
	}

	private static string SizeClass(HudSize size) => size switch
	{
		HudSize.Small => "sm",
		HudSize.Large => "lg",
		HudSize.XLarge => "xl",
		_ => "md",
	};

	/// <summary>
	/// Title, body and suffix of each element become separate segments, styled like CS2's own HUD:
	/// the title as a small caps label, the body as the (coloured) value, the suffix as a unit.
	/// </summary>
	private static List<HudSegment> ToSegments(List<HudElement> row)
	{
		var segments = new List<HudSegment>();
		foreach (var e in row)
		{
			if (e.Title != "")
				segments.Add(new HudSegment(e.Title, null, "lbl", "sm"));
			segments.Add(new HudSegment(e.Body, e.ColorClass ?? CustomHud.ColorClass(e.Color),
				e.Label ? "lbl" : "val", e.Label ? "sm" : SizeClass(e.Size)));
			if (e.Suffix.Trim() != "")
				segments.Add(new HudSegment(e.Suffix.Trim(), null, "unit", "sm"));
		}
		return segments;
	}

	private void SendSlot(string slot, List<List<HudElement>> rows)
	{
		var (maxRows, maxSegments) = CustomHud.Grid[slot];
		string slotId = CustomHud.SlotId(slot);

		// Empty slots collapse
		SendClass(slotId, "hidden", rows.Count == 0);
		SendExclusive(slotId, "shift", $"shift-{_player.Options.HudPosition(slot)}");

		for (int r = 0; r < maxRows; r++)
		{
			List<HudSegment> segments = r < rows.Count ? ToSegments(rows[r]) : [];
			string rowId = CustomHud.RowId(slot, r);
			SendClass(rowId, "hidden", segments.Count == 0);
			// A row of only labels is a header (no row background in the side panels)
			SendClass(rowId, "hdr", segments.Count > 0 && segments.All(s => s.Kind == "lbl"));
			// Top bar: every row gets its own fading band, rows after the first are smaller
			if (slot == CustomHud.Top)
			{
				SendClass(rowId, "band", segments.Count > 0);
				SendClass(rowId, "sub", r > 0);
			}

			for (int s = 0; s < maxSegments; s++)
			{
				string id = CustomHud.SegmentId(slot, r, s);
				HudSegment segment = s < segments.Count ? segments[s] : new HudSegment("", null, null, null);

				SendText(id, segment.Text);
				SendClass(id, "hidden", segment.Text.Length == 0);
				if (segment.Text.Length == 0)
					continue; // Hidden - keep its old style classes instead of sending more changes

				SendExclusive(id, "kind", segment.Kind);
				SendExclusive(id, "size", segment.Size);
				SendExclusive(id, "color", segment.ColorClass);
			}
		}
	}

	private void SendText(string id, string text)
	{
		if (_sentText.TryGetValue(id, out var last) && last == text)
			return;

		_sentText[id] = text;
		CustomHud.SetText(_player.Controller, id, text);
#if DEBUG
		_debugTextsSent++;
#endif
	}

	private void SendClass(string id, string cssClass, bool enabled)
	{
		if (_sentClass.TryGetValue((id, cssClass), out var last) && last == enabled)
			return;

		_sentClass[(id, cssClass)] = enabled;
		CustomHud.SetClass(_player.Controller, id, cssClass, enabled);
#if DEBUG
		_debugClassesSent++;
#endif
	}

	// One class out of a group (colour, kind, size) per segment: switch the old one off and the new
	// one on. null = no class from that group (e.g. default colour).
	private void SendExclusive(string id, string group, string? cssClass)
	{
		var key = (id, group);
		_sentExclusive.TryGetValue(key, out var last);
		if (_sentExclusive.ContainsKey(key) && last == cssClass)
			return;

		if (last != null)
			CustomHud.SetClass(_player.Controller, id, last, false);
		if (cssClass != null)
			CustomHud.SetClass(_player.Controller, id, cssClass, true);
		_sentExclusive[key] = cssClass;
#if DEBUG
		_debugClassesSent++;
#endif
	}

	/// <summary>
	/// Row 0: map, tier and where the shown player is (stage / bonus), plus mode flags and addon values.
	/// Row 1 (smaller): PB, rank and WR for the current mode - or what replay is playing - plus addon values.
	/// Rows 2 and 3 (smaller): addon values only (SurfTimer.Api - row 2 is the map chooser's). Empty rows collapse.
	/// </summary>
	private List<List<HudElement>> TopRows(Player? subject, ReplayPlayer? replay)
	{
		var map = SurfTimer.CurrentMap;
		var row = new List<HudElement>
		{
			new("", map.Name ?? "", ""),
			new("Tier", $"{map.Tier}", RankColorWr),
		};

		if (subject == null)
		{
			row.AddRange(SurfTimerApiImpl.TopHudFor(_player.Controller, HudRow.Main));
			var sub = new List<HudElement>();
			if (replay != null && ReplayTypeLabel(replay) != "")
			{
				sub.Add(new("", ReplayTypeLabel(replay), SpectatorColor, Label: true));
				// The best segments replay has no single player - its label already says what it is
				sub.Add(new("", replay.Type == ReplayManager.BestSegmentsType ? "" : replay.RecordPlayerName ?? "", "", Size: HudSize.Small));
				sub.Add(new("", FormatTime(replay.RecordRunTime, PlayerTimer.TimeFormatStyle.Full), RankColorWr, Size: HudSize.Small));
				sub.Add(new("Cycle", $"{replay.RepeatCount}", SpectatorColor, Size: HudSize.Small));
			}
			sub.AddRange(SurfTimerApiImpl.TopHudFor(_player.Controller, HudRow.Second));
			return WithAddonRows(row, sub);
		}

		if (subject.Timer.IsBonusMode && subject.Timer.Bonus > 0)
			row.Add(new("Bonus", $"{subject.Timer.Bonus}/{map.Bonuses}", TimerColor));
		else if (map.Stages > 0)
			row.Add(new("Stage", $"{Math.Max((short)1, subject.Timer.Stage)}/{map.Stages}", TimerColor));

		var flags = new List<string>();
		if (subject.Timer.IsPracticeMode)
			flags.Add("Practice");
		if (subject.IsRepeatMode)
			flags.Add("Repeat");
		if (flags.Count > 0)
			row.Add(new("", string.Join(" · ", flags), TimerColorPractice, Label: true));
		// Addons' values (SurfTimer.Api)
		row.AddRange(SurfTimerApiImpl.TopHudFor(_player.Controller, HudRow.Main));

		var records = new List<HudElement>
		{
			PbElement(subject, PlayerTimer.TimeFormatStyle.Full) with { Size = HudSize.Small },
			RankElement(subject) with { Size = HudSize.Small },
			WrElement(subject, PlayerTimer.TimeFormatStyle.Full) with { Size = HudSize.Small },
		};
		records.AddRange(SurfTimerApiImpl.TopHudFor(_player.Controller, HudRow.Second));

		return WithAddonRows(row, records);
	}

	/// <summary>The first two top rows plus the addon rows 2 and 3 - trailing empty rows left out</summary>
	private List<List<HudElement>> WithAddonRows(List<HudElement> main, List<HudElement> second)
	{
		var rows = new List<List<HudElement>>
		{
			main,
			second,
			SurfTimerApiImpl.TopHudFor(_player.Controller, HudRow.Third).ToList(),
			SurfTimerApiImpl.TopHudFor(_player.Controller, HudRow.Fourth).ToList(),
		};
		while (rows.Count > 1 && rows[^1].Count == 0)
			rows.RemoveAt(rows.Count - 1);
		return rows;
	}

	// The splits panel as last shown during a map run, and whose run it was (!options - Keep last splits)
	private List<List<HudElement>>? _keptSplitRows;
	private Player? _keptSplitSubject;

	/// <summary>
	/// The live splits while a map run has some. Once the run ends (fail, reset, finish) the last ones stay -
	/// marked "Last run" - until that player starts their next run, unless the viewer turned that off.
	/// </summary>
	private List<List<HudElement>> SplitRowsKept(Player subject, PlayerOptions options)
	{
		var live = SplitRows(subject, options.HudSplitTarget);
		if (live.Count > 0)
		{
			_keptSplitRows = live;
			_keptSplitSubject = subject;
			return live;
		}

		// A new run (map, stage or bonus) started, or another player is shown
		if (subject.Timer.IsRunning || !ReferenceEquals(_keptSplitSubject, subject) || !options.HudSplitsKeep || _keptSplitRows == null)
		{
			_keptSplitRows = null;
			_keptSplitSubject = null;
			return [];
		}

		var kept = new List<List<HudElement>>(_keptSplitRows);
		if (kept.Count > 0 && kept[0].Count > 0)
			kept[0] = [kept[0][0] with { Body = "Last run · " + kept[0][0].Body.Replace("Splits ", "") }];
		return kept;
	}

	/// <summary>
	/// The current map run's last stage/checkpoint splits, compared to the run the viewer picked in !options
	/// (PB, WR, #10 / the rank above, a group's last rank).
	/// </summary>
	private List<List<HudElement>> SplitRows(Player p, SplitTarget target)
	{
		if (!p.Controller.PawnIsAlive || !p.Timer.IsRunning
			|| p.Timer.IsStageMode || p.Timer.IsBonusMode
			|| p.Stats.ThisRun.Checkpoints.Count == 0)
			return [];

		string label = SurfTimer.CurrentMap.Stages > 0 ? "Stage" : "CP";
		var (header, targetSplits) = ResolveSplitTarget(p, target);

		var rows = new List<List<HudElement>> { new() { new("", header, SpectatorColor, Label: true) } };
		foreach (var cp in p.Stats.ThisRun.Checkpoints.OrderByDescending(cp => cp.Key).Take(MaxSplitLines).OrderBy(cp => cp.Key))
		{
			var row = new List<HudElement>
			{
				new($"{label} {cp.Key}", FormatTime(cp.Value.RunTime), ""),
			};

			if (targetSplits != null && targetSplits.TryGetValue(cp.Key, out var targetSplit) && targetSplit.RunTime > 0)
			{
				int diff = cp.Value.RunTime - targetSplit.RunTime;
				row.Add(new("", $"{(diff <= 0 ? "-" : "+")}{FormatTime(Math.Abs(diff))}",
					diff <= 0 ? TimerColorActive : SlowerColor, Size: HudSize.Small));
			}

			rows.Add(row);
		}

		return rows;
	}

	/// <summary>
	/// The splits panel's header and the splits of the run it compares against - null splits = no diffs
	/// (no such run, or its splits are still loading).
	/// </summary>
	private static (string Header, Dictionary<int, CheckpointEntity>? Splits) ResolveSplitTarget(Player p, SplitTarget target)
	{
		var map = SurfTimer.CurrentMap;
		int style = p.Timer.Style;
		var wr = map.WR.GetValueOrDefault(style);
		var wrSplits = wr != null && wr.RunTime > 0 ? wr.Checkpoints : null;
		var pb = p.Stats.PB.GetValueOrDefault(style);

		if (target == SplitTarget.Wr)
			return ("Splits vs WR", wrSplits);
		if (target is not (SplitTarget.Top10 or >= SplitTarget.G1 and <= SplitTarget.G5))
			return ("Splits vs PB", pb?.Checkpoints);

		int completions = map.MapCompletions.GetValueOrDefault(style);
		int rank;
		string name;
		if (target == SplitTarget.Top10)
		{
			int pbRank = pb != null && pb.ID != -1 && pb.RunTime > 0 ? pb.Rank : 0;
			if (pbRank == 1)
				return ("Splits vs WR", wrSplits);
			rank = pbRank is >= 2 and <= 10 ? pbRank - 1 : Math.Min(10, completions);
			name = rank > 0 ? $"#{rank}" : "#10";
		}
		else
		{
			int group = target - SplitTarget.G1 + 1;
			if (completions < 11)
				return ($"Splits vs G{group} · none yet", null);
			rank = Math.Min(PointsCalculator.GroupLastRank(completions, group), completions);
			name = $"G{group} · #{rank}";
		}

		string header = $"Splits vs {name}";
		if (rank < 1)
			return (header, null);
		if (rank == 1)
			return (header, wrSplits); // Already in memory
		return (header, map.SplitTargets.Get(map.CourseId(0, 0), style, rank)?.Splits);
	}

	/// <summary>
	/// The best segments replay's parts: every stage / checkpoint WR with its holder and time, the one
	/// playing highlighted. More parts than lines: the list moves along, keeping the playing one centered.
	/// </summary>
	private List<List<HudElement>> BestSegmentRows(ReplayPlayer replay)
	{
		var parts = replay.BestSegmentParts;
		if (parts == null || parts.Count == 0)
			return [];

		int frame = replay.PlayedFrameIndex;
		int active = Math.Max(0, parts.FindLastIndex(p => p.StartFrame <= frame));
		int first = Math.Clamp(active - MaxSplitLines / 2, 0, Math.Max(0, parts.Count - MaxSplitLines));
		string label = SurfTimer.CurrentMap.Stages > 0 ? "S" : "CP";

		var rows = new List<List<HudElement>> { new() { new("", ReplayManager.BestSegmentsLabel, SpectatorColor, Label: true) } };
		for (int i = first; i < Math.Min(parts.Count, first + MaxSplitLines); i++)
		{
			var part = parts[i];
			bool playing = i == active;
			string holder = part.Holder.Length > 16 ? part.Holder[..15] + "…" : part.Holder;
			rows.Add(
			[
				new($"{label}{part.Number}", FormatTime(part.Time), playing ? RankColorWr : ""),
				new("", holder, playing ? RankColorWr : SpectatorColor, Size: HudSize.Small),
			]);
		}
		return rows;
	}

	/// <summary>
	/// Everyone currently spectating the watched player (this player while alive, or whoever / whichever
	/// replay bot they spectate - then they're on the list themselves).
	/// </summary>
	private List<List<HudElement>> SpectatorRows(ICollection<Player> allPlayers, CCSPlayerController? watched)
	{
		if (watched == null || !watched.IsValid || !watched.PawnIsAlive)
			return [];

		var spectators = allPlayers
			.Where(p => p.Controller.IsValid && !p.Controller.Equals(watched) && p.IsSpectating(watched))
			.Select(p => p.Controller.PlayerName)
			.ToList();

		if (spectators.Count == 0)
			return [];

		var rows = new List<List<HudElement>> { new() { new("", $"Spectators · {spectators.Count}", SpectatorColor, Label: true) } };
		rows.AddRange(spectators.Take(MaxSpectatorLines).Select(name => new List<HudElement> { new("", name, "", Size: HudSize.Small) }));
		return rows;
	}

	// ---- Popup menu (st_menu) ----
	// Opened through MenuPresenter. Opening it puts the player in cursor mode (input capture), and
	// clicks arrive in OnMenuClick. !cursor toggles cursor mode (with or without a menu); E closes the
	// menu, and so does starting to move while the cursor is on. Rendered through the same cached
	// senders as the HUD.

	private HudMenu? _menu;
	private int _menuTab;
	private int _menuPage;
	private bool? _sentInputCapture;
	private bool _cursor;
	private PlayerButtons _lastButtons;

	private const PlayerButtons MoveButtons =
		PlayerButtons.Forward | PlayerButtons.Back | PlayerButtons.Moveleft | PlayerButtons.Moveright | PlayerButtons.Jump;

	internal bool IsCursorOn => _cursor;

	internal bool IsMenuOpen => _menu != null;

	/// <summary>The open menu (null when closed)</summary>
	internal HudMenu? Menu => _menu;

	internal void OpenMenu(HudMenu menu)
	{
		_menu = menu;
		_menuTab = Math.Clamp(menu.ActiveTab, 0, Math.Max(0, menu.Tabs.Count - 1));
		_menuPage = 0;
		_cursor = true;
		// Keys already held when it opens don't close it - only new presses
		_lastButtons = _player.Controller.Buttons;
		SendMenu();
	}

	/// <summary>
	/// Replaces the open menu's content (e.g. after an action) - stays on the page unless resetPage.
	/// </summary>
	internal void UpdateMenu(HudMenu menu, bool resetPage)
	{
		_menu = menu;
		_menuTab = Math.Clamp(menu.ActiveTab, 0, Math.Max(0, menu.Tabs.Count - 1));
		if (resetPage)
			_menuPage = 0;
		SendMenu(); // Clamps the page
	}

	internal void CloseMenu()
	{
		if (_menu == null && !_cursor)
			return;

		_menu = null;
		_cursor = false;
		SendMenu();
	}

	/// <summary>
	/// Cursor mode on / off (!cursor) - for the open menu (move with it shown, click again later) or without one.
	/// Returns the new state.
	/// </summary>
	internal bool ToggleCursor()
	{
		_cursor = !_cursor;
		_lastButtons = _player.Controller.Buttons;
		SendMenu();
		return _cursor;
	}

	/// <summary>
	/// Every tick: E closes the menu; a movement key pressed while the cursor is on closes the menu (or turns the
	/// cursor off) - the player wants to move. Only presses, not keys held since the menu opened.
	/// </summary>
	internal void TickMenuKeys()
	{
		if (_menu == null && !_cursor)
			return;

		var buttons = _player.Controller.Buttons;
		var pressed = buttons & ~_lastButtons;
		_lastButtons = buttons;

		if ((_menu != null && (pressed & PlayerButtons.Use) != 0) || (_cursor && (pressed & MoveButtons) != 0))
			CloseMenu();
	}

	/// <summary>
	/// A button of our layout was clicked by this player. Only buttons that exist on the current
	/// page / tab do anything.
	/// </summary>
	internal void OnMenuClick(string buttonId)
	{
		if (_menu == null)
			return;

		var items = _menu.Tabs[_menuTab].Items;
		int pages = PageCount(items.Count);

		if (buttonId == CustomHud.MenuCloseId)
		{
			CloseMenu();
		}
		else if (buttonId == CustomHud.MenuBackId)
		{
			_menu.OnBack?.Invoke(_player.Controller);
		}
		else if (buttonId == CustomHud.MenuPrevId)
		{
			if (_menuPage > 0)
			{
				_menuPage--;
				SendMenu();
			}
		}
		else if (buttonId == CustomHud.MenuNextId)
		{
			if (_menuPage < pages - 1)
			{
				_menuPage++;
				SendMenu();
			}
		}
		else if (buttonId.StartsWith(CustomHud.MenuTabPrefix) && int.TryParse(buttonId[CustomHud.MenuTabPrefix.Length..], out int tab))
		{
			if (tab >= 0 && tab < _menu.Tabs.Count && tab != _menuTab)
			{
				_menuTab = tab;
				_menuPage = 0;
				SendMenu();
				_menu.OnTabChanged?.Invoke(_player.Controller, tab);
			}
		}
		else if (buttonId.StartsWith(CustomHud.MenuItemPrefix) && int.TryParse(buttonId[CustomHud.MenuItemPrefix.Length..], out int row))
		{
			int index = _menuPage * CustomHud.MenuItemCount + row;
			if (row < 0 || row >= CustomHud.MenuItemCount || index >= items.Count)
				return;

			var item = items[index];
			if (item.OnSelect == null)
				return; // Info row

			var onSelect = item.OnSelect;
			var controller = _player.Controller;

			// The action updates the open menu itself (toggles, steppers, sub-pages)
			if (item.KeepOpen)
			{
				onSelect(controller);
				return;
			}

			// Movement back first, then the action (which may e.g. move the player to spectator)
			CloseMenu();
			Server.NextFrame(() =>
			{
				if (controller.IsValid)
					onSelect(controller);
			});
		}
	}

	private static int PageCount(int items) => Math.Max(1, (items + CustomHud.MenuItemCount - 1) / CustomHud.MenuItemCount);

	/// <summary>
	/// Sends the popup's state - only what changed, like the rest of the HUD.
	/// </summary>
	private void SendMenu()
	{
		if (!CustomHud.IsActive || !_player.Controller.IsValid)
			return;

		SyncCustomHudGeneration(); // Also called outside the HUD tick (open / clicks)

		bool open = _menu != null;
		SendClass(CustomHud.MenuId, "hidden", !open);
		SendInputCapture(_cursor);
		if (!open)
			return;

		var menu = _menu!;
		SendText(CustomHud.MenuTitleId, menu.Title);
		SendClass(CustomHud.MenuBackId, "hidden", menu.OnBack == null);
		SendClass(CustomHud.MenuStatusId, "hidden", menu.Status.Length == 0);
		SendText(CustomHud.MenuStatusId, menu.Status);

		// Tabs - no tab row for a single tab
		bool showTabs = menu.Tabs.Count > 1;
		SendClass(CustomHud.MenuTabsId, "hidden", !showTabs);
		for (int t = 0; t < CustomHud.MenuTabCount; t++)
		{
			string tabId = CustomHud.MenuTabId(t);
			bool exists = showTabs && t < menu.Tabs.Count;
			SendClass(tabId, "hidden", !exists);
			if (!exists)
				continue;

			SendText(CustomHud.MenuTabLabelId(t), menu.Tabs[t].Name);
			SendClass(tabId, "on", t == _menuTab);
		}

		// Rows of the current page
		var items = menu.Tabs[_menuTab].Items;
		int pages = PageCount(items.Count);
		_menuPage = Math.Clamp(_menuPage, 0, pages - 1);

		for (int row = 0; row < CustomHud.MenuItemCount; row++)
		{
			int index = _menuPage * CustomHud.MenuItemCount + row;
			bool exists = index < items.Count;
			SendClass(CustomHud.MenuItemId(row), "hidden", !exists);
			if (!exists)
				continue;

			var item = items[index];
			SendClass(CustomHud.MenuItemId(row), "info", item.OnSelect == null); // No hover, not clickable
			SendClass(CustomHud.MenuItemId(row), "danger", item.Style == HudMenuItemStyle.Danger);
			SendClass(CustomHud.MenuItemId(row), "on", item.Style == HudMenuItemStyle.On);
			SendClass(CustomHud.MenuItemId(row), "off", item.Style == HudMenuItemStyle.Off);
			SendText(CustomHud.MenuItemPartId(row, "num"), (row + 1).ToString());
			SendText(CustomHud.MenuItemPartId(row, "text"), item.Text);
			SendText(CustomHud.MenuItemPartId(row, "sub"), item.Sub);
			SendText(CustomHud.MenuItemPartId(row, "right"), item.RightText());
		}

		SendText(CustomHud.MenuPageId, $"Page {_menuPage + 1} / {pages}");
		SendClass(CustomHud.MenuPrevId, "off", _menuPage == 0);
		SendClass(CustomHud.MenuNextId, "off", _menuPage >= pages - 1);
	}

	private void SendInputCapture(bool enabled)
	{
		if (_sentInputCapture == enabled)
			return;

		_sentInputCapture = enabled;
		CustomHud.SetInputCapture(_player.Controller, enabled);
	}

	/// <summary>
	/// Displays checkpoints comparison messages in player chat.
	/// Only calculates if the player has a PB, otherwise it will display N/A
	/// </summary>
	internal void DisplayCheckpointMessages()
	{
		// !options - Chat: own split messages
		if (!_player.Options.ChatSplits)
			return;

		int pbTime;
		int wrTime = -1;
		float pbSpeed;
		float wrSpeed = -1.0f;
		int style = _player.Timer.Style;
		int playerCurrentCheckpoint = _player.Timer.Checkpoint;
		int currentTime = _player.Timer.Ticks;
		var axes = _player.Options.SpeedAxes;
		float currentSpeed = Extensions.SpeedOf(_player.Controller!, axes);

		// Default values for the PB and WR differences in case no calculations can be made
		string strPbDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";
		string strWrDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";

		// Get PB checkpoint data if available
		CheckpointEntity? pbCheckpoint = null;
		_player.Stats.PB[style].Checkpoints?.TryGetValue(playerCurrentCheckpoint, out pbCheckpoint);
		if (pbCheckpoint != null)
		{
			pbTime = pbCheckpoint.RunTime;
			pbSpeed = Extensions.Speed(pbCheckpoint.StartVelX, pbCheckpoint.StartVelY, pbCheckpoint.StartVelZ, axes);
		}
		else
		{
			// We assign default values to pbTime and pbSpeed
			pbTime = -1; // This determines if we will calculate differences or not!!!
			pbSpeed = 0.0f;
		}

		// Calculate differences in PB (PB - Current)
		if (pbTime != -1)
		{
#if DEBUG
			Console.WriteLine(
				$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> Starting PB difference calculation... (pbTime != -1)"
			);
#endif
			// Reset the string
			strPbDifference = string.Empty;

			// Calculate the time difference
			if (pbTime - currentTime < 0.0)
			{
				strPbDifference += ChatColors.Red + "+" + FormatTime((pbTime - currentTime) * -1); // We multiply by -1 to get the positive value
			}
			else if (pbTime - currentTime >= 0.0)
			{
				strPbDifference += ChatColors.Green + "-" + FormatTime(pbTime - currentTime);
			}
			strPbDifference += ChatColors.Default + " ";

			// Calculate the speed difference
			if (pbSpeed - currentSpeed <= 0.0)
			{
				strPbDifference +=
					"(" + ChatColors.Green + "+" + ((pbSpeed - currentSpeed) * -1).ToString("0"); // We multiply by -1 to get the positive value
			}
			else if (pbSpeed - currentSpeed > 0.0)
			{
				strPbDifference +=
					"(" + ChatColors.Red + "-" + (pbSpeed - currentSpeed).ToString("0");
			}
			strPbDifference += ChatColors.Default + ")";
		}

		if (SurfTimer.CurrentMap.WR[style].RunTime > 0)
		{
			// Calculate differences in WR (WR - Current)
#if DEBUG
			Console.WriteLine(
				$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> Starting WR difference calculation... (SurfTimer.CurrentMap.WR[{style}].Ticks > 0)"
			);
#endif

			CheckpointEntity? wrCheckpoint = null;
			SurfTimer.CurrentMap.WR[style].Checkpoints?.TryGetValue(playerCurrentCheckpoint, out wrCheckpoint);
			if (wrCheckpoint != null)
			{
				wrTime = wrCheckpoint.RunTime;
				wrSpeed = Extensions.Speed(wrCheckpoint.StartVelX, wrCheckpoint.StartVelY, wrCheckpoint.StartVelZ, axes);
				// Reset the string
				strWrDifference = string.Empty;

				// Calculate the WR time difference
				if (wrTime - currentTime < 0.0)
				{
					strWrDifference += ChatColors.Red + "+" + FormatTime((wrTime - currentTime) * -1); // We multiply by -1 to get the positive value
				}
				else if (wrTime - currentTime >= 0.0)
				{
					strWrDifference += ChatColors.Green + "-" + FormatTime(wrTime - currentTime);
				}
				strWrDifference += ChatColors.Default + " ";

				// Calculate the WR speed difference
				if (wrSpeed - currentSpeed <= 0.0)
				{
					strWrDifference +=
						"(" + ChatColors.Green + "+" + ((wrSpeed - currentSpeed) * -1).ToString("0"); // We multiply by -1 to get the positive value
				}
				else if (wrSpeed - currentSpeed > 0.0)
				{
					strWrDifference +=
						"(" + ChatColors.Red + "-" + (wrSpeed - currentSpeed).ToString("0");
				}
				strWrDifference += ChatColors.Default + ")";
			}
		}

		// Print checkpoint message
		_player.Controller.PrintToChat(
			$"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpoint_message",
			playerCurrentCheckpoint, FormatTime(_player.Timer.Ticks), currentSpeed.ToString("0"), strPbDifference, strWrDifference]}"
		);

#if DEBUG
		Console.WriteLine(
			$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> [TIME]  PB: {pbTime} - CURR: {currentTime} = pbTime: {pbTime - currentTime}"
		);
		Console.WriteLine(
			$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> [SPEED] PB: {pbSpeed} - CURR: {currentSpeed} = difference: {pbSpeed - currentSpeed}"
		);
		Console.WriteLine(
			$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> [TIME]  WR: {wrTime} - CURR: {currentTime} = difference: {wrTime - currentTime}"
		);
		Console.WriteLine(
			$"CS2 Surf DEBUG >> DisplayCheckpointMessages -> [SPEED] WR: {wrSpeed} - CURR: {currentSpeed} = difference: {wrSpeed - currentSpeed}"
		);
#endif
	}

	/// <summary>
	/// Displays a Stage completion comparison message in player chat (staged maps only).
	/// Compares against the standalone Stage PB/WR records, not the generic per-run Checkpoint splits.
	/// Only calculates if a record exists yet, otherwise it will display N/A.
	/// </summary>
	/// <param name="stage">Stage that was just completed</param>
	/// <param name="stageRunTime">Ticks it took to complete the stage</param>
	/// <param name="exitVelocity">Player's velocity at the moment the stage was completed</param>
	internal void DisplayStageMessage(short stage, int stageRunTime, VectorT exitVelocity)
	{
		// !options - Chat: own split messages
		if (!_player.Options.ChatSplits)
			return;

		int style = _player.Timer.Style;
		var axes = _player.Options.SpeedAxes;
		float exitSpeed = Extensions.Speed(exitVelocity, axes);

		string strPbDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";
		string strWrDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";

		PersonalBest stagePb = _player.Stats.StagePB[stage][style];
		if (stagePb.ID != -1)
		{
			int pbTime = stagePb.RunTime;
			float pbSpeed = Extensions.Speed(stagePb.EndVelX, stagePb.EndVelY, stagePb.EndVelZ, axes);

			strPbDifference = string.Empty;
			if (pbTime - stageRunTime < 0.0)
				strPbDifference += ChatColors.Red + "+" + FormatTime((pbTime - stageRunTime) * -1);
			else
				strPbDifference += ChatColors.Green + "-" + FormatTime(pbTime - stageRunTime);
			strPbDifference += ChatColors.Default + " ";

			if (pbSpeed - exitSpeed <= 0.0)
				strPbDifference += "(" + ChatColors.Green + "+" + ((pbSpeed - exitSpeed) * -1).ToString("0");
			else
				strPbDifference += "(" + ChatColors.Red + "-" + (pbSpeed - exitSpeed).ToString("0");
			strPbDifference += ChatColors.Default + ")";
		}

		PersonalBest stageWr = SurfTimer.CurrentMap.StageWR[stage][style];
		if (stageWr.ID != -1)
		{
			int wrTime = stageWr.RunTime;
			float wrSpeed = Extensions.Speed(stageWr.EndVelX, stageWr.EndVelY, stageWr.EndVelZ, axes);

			strWrDifference = string.Empty;
			if (wrTime - stageRunTime < 0.0)
				strWrDifference += ChatColors.Red + "+" + FormatTime((wrTime - stageRunTime) * -1);
			else
				strWrDifference += ChatColors.Green + "-" + FormatTime(wrTime - stageRunTime);
			strWrDifference += ChatColors.Default + " ";

			if (wrSpeed - exitSpeed <= 0.0)
				strWrDifference += "(" + ChatColors.Green + "+" + ((wrSpeed - exitSpeed) * -1).ToString("0");
			else
				strWrDifference += "(" + ChatColors.Red + "-" + (wrSpeed - exitSpeed).ToString("0");
			strWrDifference += ChatColors.Default + ")";
		}

		_player.Controller.PrintToChat(
			$"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["stage_message",
			stage, FormatTime(stageRunTime), exitSpeed.ToString("0"), strPbDifference, strWrDifference]}"
		);
	}

	/// <summary>
	/// Displays a Checkpoint segment completion comparison message in player chat (non-staged maps
	/// only). Compares against the standalone Checkpoint PB/WR records, not the generic per-run
	/// embedded splits. Only calculates if a record exists yet, otherwise it will display N/A.
	/// </summary>
	/// <param name="checkpoint">Checkpoint segment that was just completed</param>
	/// <param name="checkpointRunTime">Ticks it took to complete the checkpoint segment</param>
	/// <param name="exitVelocity">Player's velocity at the moment the checkpoint segment was completed</param>
	internal void DisplayCheckpointSegmentMessage(short checkpoint, int checkpointRunTime, VectorT exitVelocity)
	{
		// !options - Chat: own split messages
		if (!_player.Options.ChatSplits)
			return;

		int style = _player.Timer.Style;
		var axes = _player.Options.SpeedAxes;
		float exitSpeed = Extensions.Speed(exitVelocity, axes);

		// Runs inside zone touch handlers - an unknown segment must not throw and abort the handler
		if (!HasEntry(_player.Stats.CheckpointPB, checkpoint, style) || !HasEntry(SurfTimer.CurrentMap.CheckpointWR, checkpoint, style))
			return;

		string strPbDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";
		string strWrDifference =
			$"{ChatColors.Grey}N/A{ChatColors.Default} ({ChatColors.Grey}N/A{ChatColors.Default})";

		PersonalBest checkpointPb = _player.Stats.CheckpointPB[checkpoint][style];
		if (checkpointPb.ID != -1)
		{
			int pbTime = checkpointPb.RunTime;
			float pbSpeed = Extensions.Speed(checkpointPb.EndVelX, checkpointPb.EndVelY, checkpointPb.EndVelZ, axes);

			strPbDifference = string.Empty;
			if (pbTime - checkpointRunTime < 0.0)
				strPbDifference += ChatColors.Red + "+" + FormatTime((pbTime - checkpointRunTime) * -1);
			else
				strPbDifference += ChatColors.Green + "-" + FormatTime(pbTime - checkpointRunTime);
			strPbDifference += ChatColors.Default + " ";

			if (pbSpeed - exitSpeed <= 0.0)
				strPbDifference += "(" + ChatColors.Green + "+" + ((pbSpeed - exitSpeed) * -1).ToString("0");
			else
				strPbDifference += "(" + ChatColors.Red + "-" + (pbSpeed - exitSpeed).ToString("0");
			strPbDifference += ChatColors.Default + ")";
		}

		PersonalBest checkpointWr = SurfTimer.CurrentMap.CheckpointWR[checkpoint][style];
		if (checkpointWr.ID != -1)
		{
			int wrTime = checkpointWr.RunTime;
			float wrSpeed = Extensions.Speed(checkpointWr.EndVelX, checkpointWr.EndVelY, checkpointWr.EndVelZ, axes);

			strWrDifference = string.Empty;
			if (wrTime - checkpointRunTime < 0.0)
				strWrDifference += ChatColors.Red + "+" + FormatTime((wrTime - checkpointRunTime) * -1);
			else
				strWrDifference += ChatColors.Green + "-" + FormatTime(wrTime - checkpointRunTime);
			strWrDifference += ChatColors.Default + " ";

			if (wrSpeed - exitSpeed <= 0.0)
				strWrDifference += "(" + ChatColors.Green + "+" + ((wrSpeed - exitSpeed) * -1).ToString("0");
			else
				strWrDifference += "(" + ChatColors.Red + "-" + (wrSpeed - exitSpeed).ToString("0");
			strWrDifference += ChatColors.Default + ")";
		}

		_player.Controller.PrintToChat(
			$"{Config.PluginPrefix} {LocalizationService.LocalizerNonNull["checkpoint_message",
			checkpoint, FormatTime(checkpointRunTime), exitSpeed.ToString("0"), strPbDifference, strWrDifference]}"
		);
	}
}
