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
	// Nord (nordtheme.com) - CustomHud.ColorClass maps each to the layout's colour class
	private readonly string TimerColor = "#88C0D0";
	private readonly string TimerColorPractice = "#B48EAD";
	private readonly string TimerColorActive = "#A3BE8C";
	/// <summary>Running, but already slower than the PB (or WR - !options - Compare - Timer warning)</summary>
	private readonly string TimerColorMissed = "#EBCB8B";
	private readonly string RankColorPb = "#81A1C1";
	private readonly string RankColorWr = "#EBCB8B";
	private readonly string SpectatorColor = "#7B88A1";
	private readonly string SlowerColor = "#BF616A";

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
	/// <param name="Mono">The body in the monospace font (changing numbers, e.g. a countdown)</param>
	internal readonly record struct HudElement(string Title, string Body, string Color, string Suffix = "",
		HudSize Size = HudSize.Medium, string? ColorClass = null, bool Label = false, bool Mono = false);

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
	/// Timer colour: idle, running, running in practice mode, or running but already slower than the PB (or WR, the
	/// viewer's Timer warning option) of this run.
	/// </summary>
	private string TimerColorOf(Player p)
	{
		if (!p.Timer.IsRunning)
			return TimerColor;
		if (p.Timer.IsPracticeMode)
			return TimerColorPractice;
		int target = _player.Options.TimerWarning == TimerWarning.Wr ? RunTargets.WrTicks(p) : RunTargets.PbTicks(p);
		return RunTargets.Missed(p, target) ? TimerColorMissed : TimerColorActive;
	}

	/// <summary>
	/// The timer with a prefix based on mode ([P], [B#], [S#])
	/// </summary>
	private HudElement TimerElement(Player p)
	{
		if (p.TimerDisabled)
			return new HudElement("", "Noclip", TimerColorPractice, Size: HudSize.XLarge);

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
	private static bool HasBonusData(Player p, int style) => RunTargets.HasBonusData(p, style);

	private static bool HasStageData(Player p, int style) => RunTargets.HasStageData(p, style);

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
	private readonly record struct HudSegment(string Text, string? ColorClass, string? Kind, string? Size, bool Mono = false);

	// Slot updates are sent 16x/s at most, and each segment's text/class only when it changed
	private const int CustomHudUpdateTicks = 4;
	private const int MaxSplitLines = 6;
	private const int PaceSplitLines = 4;
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

		// The shown speed's colour for this update (gain / loss compares with the last update)
		var axes = _player.Options.SpeedAxes;
		float? shownSpeed = subject != null ? Extensions.SpeedOf(subject.Controller, axes)
			: replay?.Controller != null ? Extensions.SpeedOf(replay.Controller, axes) : null;
		if (shownSpeed is float speedNow)
		{
			_speedDelta = _lastShownSpeed is float before ? Math.Abs(speedNow) - Math.Abs(before) : 0f;
			_lastShownSpeed = speedNow;
			var colorOptions = _player.Options;
			_speedClass = SpeedClassOf(speedNow, colorOptions.SpeedColor, colorOptions.SpeedColorStatic);
			// The center speed follows the HUD speed colour unless it has its own
			_centerSpeedClass = colorOptions.CenterSpeedColor is SpeedColorMode own
				? SpeedClassOf(speedNow, own, colorOptions.CenterSpeedColorStatic)
				: _speedClass;
		}
		else
		{
			_lastShownSpeed = null;
			_speedClass = _centerSpeedClass = "nc-snow";
		}

		// Panels the viewer turned off in !options (HUD) are sent empty, which collapses them
		var options = _player.Options;
		_fieldsThisUpdate.Clear();
		var source = FieldSourceOf(subject, replay);
		SendCenter(source);
		SendMid(source);
		SendSlot(CustomHud.Top, options.HudTop ? TopRows(subject, replay) : []);
		// Left: the run's splits - or, spectating the best segments bot, every segment WR it chains
		var leftRows = subject != null ? SplitRowsKept(subject, options)
			: replay?.Type == ReplayManager.BestSegmentsType ? BestSegmentRows(replay)
			: [];
		// Live pace under the splits (also in stage / bonus mode, where there are no splits)
		if (subject != null && options.HudPace && options.HudSplits)
			leftRows = [.. leftRows, .. PaceRows(subject, options)];
		SendSlot(CustomHud.Left, options.HudSplits ? leftRows : []);
		// Spectators of whoever this HUD shows - ourselves while alive, else the watched player / replay bot
		var watched = subject?.Controller ?? replay?.Controller;
		SendSlot(CustomHud.Right, options.HudSpectators ? SpectatorRows(allPlayers, watched) : []);
		SendMenu(); // Refreshes live values (e.g. !spec times) - clicks re-render right away

		// Accent colour of every block: the shown player's team (CT blue / T yellow)
		string team = TeamClassOf(subject, replay);
		foreach (string id in TeamAccentIds)
			SendExclusive(id, "team", team);

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
	/// What one HUD update shows - a live player or a replay bot. Both layouts' fields are built from it.
	/// </summary>
	private sealed record FieldSource(Player? Subject, ReplayPlayer? Replay, int Ticks, string TimerColor, float Velocity,
		float? Prespeed, PlayerButtons? Buttons, float? Sync);

	/// <summary>The shown values for this update - null when there's nothing to show (no subject, unknown replay type)</summary>
	private FieldSource? FieldSourceOf(Player? subject, ReplayPlayer? replay)
	{
		// Speeds on the viewer's axes (!options - HUD), also for whoever they spectate
		var axes = _player.Options.SpeedAxes;
		if (subject != null)
		{
			float velocity = Extensions.SpeedOf(subject.Controller, axes);
			// Prespeed: live while in a start zone, then the exit speed
			float? prespeed = subject.IsTouchingAnyStartZone ? velocity
				: subject.LastPrespeed is VectorT exit ? Extensions.Speed(exit, axes) : null;
			return new FieldSource(subject, null, subject.Timer.Ticks, TimerColorOf(subject), velocity, prespeed,
				subject.Controller.Buttons, subject.SyncPercent);
		}

		if (replay?.Controller == null || ReplayTypeLabel(replay) == "")
			return null;
		float replayVelocity = Extensions.SpeedOf(replay.Controller, axes);
		// Replays recorded before buttons were stored: keys stay released, sync N/A
		return new FieldSource(null, replay, replay.ReplayCurrentRunTime, ReplayTimerColor(replay), replayVelocity,
			replay.Prespeed(replayVelocity, axes), replay.CurrentButtons(), replay.CurrentSync());
	}

	// Fields built in this update, by key - the bottom and the center HUD can show the same one
	private readonly Dictionary<string, HudField> _fieldsThisUpdate = new();

	private HudField FieldOf(string key, FieldSource source)
	{
		if (!_fieldsThisUpdate.TryGetValue(key, out var field))
			_fieldsThisUpdate[key] = field = BuildField(key, source);
		return field;
	}

	private static bool IsAvailable(HudField field) => !(field.Segments.Count == 1 && field.Segments[0] == NotAvailable);

	private static string Signed(float value) => $"{(value >= 0 ? "+" : "-")}{Math.Abs(value).ToString("0", CultureInfo.InvariantCulture)}";

	private static string SignedTime(int ticks) => $"{(ticks <= 0 ? "-" : "+")}{FormatTime(Math.Abs(ticks))}";

	private static HudField Unavailable(string label) => new(label, [NotAvailable]);

	/// <summary>One field's label and value segments (HudFields.cs) - N/A when the shown player / replay has no such value</summary>
	private HudField BuildField(string key, FieldSource s)
	{
		var p = s.Subject;
		string label = HudFieldCatalog.Label(key);
		if (HudFieldCatalog.KindOf(key) is not HudFieldKind kind)
			return AddonField(key, label);

		string ahead = CustomHud.ColorClass(TimerColorActive) ?? "nc-green";
		string behind = CustomHud.ColorClass(SlowerColor) ?? "nc-red";
		FieldSegment Value(string text, string? colorClass = null) => new(text, colorClass, Mono: true);
		HudField One(string text, string? colorClass = null) => new(label, [Value(text, colorClass)]);

		switch (kind)
		{
			case HudFieldKind.Timer:
				if (p != null && p.TimerDisabled)
					return new HudField(label, [Value("Noclip", CustomHud.ColorClass(TimerColorPractice))], Wide: true);
				return new HudField(label, [Value(FormatTime(s.Ticks, PlayerTimer.TimeFormatStyle.Full), CustomHud.ColorClass(s.TimerColor))], Wide: true);

			case HudFieldKind.SegmentTime:
				return p != null && p.Timer.IsRunning
					? One(FormatTime(Math.Max(0, p.Timer.Ticks - p.Stats.ThisRun.RunTime)), CustomHud.ColorClass(s.TimerColor))
					: Unavailable(label);

			case HudFieldKind.Speed:
				return new HudField($"Speed {Extensions.SpeedLabel(_player.Options.SpeedAxes)}",
					[Value(s.Velocity.ToString("0", CultureInfo.InvariantCulture), _speedClass)]);

			case HudFieldKind.Prespeed:
				return s.Prespeed is float prespeed
					? One(prespeed.ToString("0", CultureInfo.InvariantCulture), PrespeedClassOf(prespeed))
					: Unavailable(label);

			case HudFieldKind.Keys:
				var held = s.Buttons ?? 0;
				return new HudField(label, Keys.Select(k => new FieldSegment(k.Letter, Dim: !held.HasFlag(k.Button), Key: true)).ToList());

			case HudFieldKind.Sync:
				return s.Sync is float sync ? One(sync.ToString("00.00", CultureInfo.InvariantCulture) + "%") : Unavailable(label);

			case HudFieldKind.SegmentSync:
				return p != null && p.Timer.IsRunning
					? One(p.SegmentSyncPercent.ToString("00.00", CultureInfo.InvariantCulture) + "%")
					: Unavailable(label);

			case HudFieldKind.Energy:
				return p != null ? One(p.Energy.Value.ToString("0", CultureInfo.InvariantCulture), EnergyClassOf(p.Energy.Value)) : Unavailable(label);

			case HudFieldKind.Pace:
				return p != null && PaceOf(p) is PaceResult pace
					? One((pace.Live ? "" : "~") + FormatTime(pace.PaceTicks))
					: Unavailable(label);

			case HudFieldKind.PaceDelta:
				return p != null && PaceOf(p) is PaceResult paceDelta
					? One(SignedTime(paceDelta.DeltaTicks), paceDelta.DeltaTicks <= 0 ? ahead : behind)
					: Unavailable(label);

			case HudFieldKind.EnergyDiff:
				return p != null && PaceOf(p) is { EnergyDiff: float energyDiff }
					? One(Signed(energyDiff), energyDiff >= 0 ? ahead : behind)
					: Unavailable(label);

			case HudFieldKind.SpeedDiff:
				return p != null && PaceOf(p) is { SpeedDiff: float speedDiff }
					? One(Signed(speedDiff), speedDiff >= 0 ? ahead : behind)
					: Unavailable(label);

			case HudFieldKind.LastSplit:
				return p != null ? LastSplitField(p, label) : Unavailable(label);

			case HudFieldKind.Progress:
				return p != null ? ProgressField(p) : Unavailable(label);

			case HudFieldKind.Pb:
				return p != null && RunTargets.PbTicks(p) is > 0 and var pb
					? One(FormatTime(pb, PlayerTimer.TimeFormatStyle.Full), CustomHud.ColorClass(RankColorPb))
					: Unavailable(label);

			case HudFieldKind.Wr:
				return p != null && RunTargets.WrTicks(p) is > 0 and var wr
					? One(FormatTime(wr, PlayerTimer.TimeFormatStyle.Full), CustomHud.ColorClass(RankColorWr))
					: Unavailable(label);

			case HudFieldKind.Rank:
				return p != null ? One(RankElement(p).Body, CustomHud.ColorClass(RankColorPb)) : Unavailable(label);

			case HudFieldKind.Jumps:
				return p != null ? One(p.RunJumps.ToString(CultureInfo.InvariantCulture)) : Unavailable(label);

			case HudFieldKind.Strafes:
				return p != null ? One(p.RunStrafes.ToString(CultureInfo.InvariantCulture)) : Unavailable(label);

			default:
				return Unavailable(label);
		}
	}

	/// <summary>An addon's field (SurfTimer.Api RegisterHudField) - N/A while it isn't registered or has no value</summary>
	private HudField AddonField(string key, string label)
	{
		var addon = HudFieldCatalog.Addon(key);
		if (addon == null)
			return Unavailable(label);

		HudValue? value;
		try
		{
			value = addon.Value(_player.Controller);
		}
		catch (Exception)
		{
			return Unavailable(label); // An addon's failure must not break the HUD
		}
		return value == null || value.Value.Length == 0
			? Unavailable(label)
			: new HudField(label, [new FieldSegment(value.Value, SurfTimerApiImpl.ColorClassOf(value.Color), Mono: value.Monospace)]);
	}

	/// <summary>The map run's last checkpoint / stage split and its difference to the splits panel's target</summary>
	private HudField LastSplitField(Player p, string label)
	{
		var splits = p.Stats.ThisRun.Checkpoints;
		if (!p.Timer.IsRunning || p.Timer.IsStageMode || p.Timer.IsBonusMode || splits.Count == 0)
			return Unavailable(label);

		int number = splits.Keys.Max();
		var split = splits[number];
		string name = SurfTimer.CurrentMap.Stages > 0 ? $"S{number}" : $"CP{number}";
		var (_, targetSplits, _) = ResolveSplitTarget(p, _player.Options.HudSplitTarget);
		if (targetSplits == null || !targetSplits.TryGetValue(number, out var target) || target.RunTime <= 0)
			return new HudField(label, [new FieldSegment(name, Dim: true), new FieldSegment(FormatTime(split.RunTime), Mono: true)]);

		int diff = split.RunTime - target.RunTime;
		return new HudField(label,
		[
			new FieldSegment(name, Dim: true),
			new FieldSegment(SignedTime(diff), CustomHud.ColorClass(diff <= 0 ? TimerColorActive : SlowerColor), Mono: true),
		]);
	}

	/// <summary>"Stage 3/7", "CP 4/12", "Bonus 2" - where the shown player is</summary>
	private HudField ProgressField(Player p)
	{
		var map = SurfTimer.CurrentMap;
		string color = CustomHud.ColorClass(TimerColor) ?? "";
		if (p.Timer.IsBonusMode && p.Timer.Bonus > 0)
			return new HudField("Bonus", [new FieldSegment($"{p.Timer.Bonus}/{map.Bonuses}", color, Mono: true)]);
		if (map.Stages > 0)
			return new HudField("Stage", [new FieldSegment($"{Math.Max((short)1, p.Timer.Stage)}/{map.Stages}", color, Mono: true)]);
		if (map.TotalCheckpoints > 0)
			return new HudField("CP", [new FieldSegment($"{p.Timer.Checkpoint}/{map.TotalCheckpoints}", color, Mono: true)]);
		return new HudField("Map", [new FieldSegment("linear", color)]);
	}

	/// <summary>
	/// Bottom center, as rows of fields in the viewer's layout (!options - HUD; by default timer (wide) and
	/// speed, then prespeed, keys and sync).
	/// </summary>
	private void SendCenter(FieldSource? source)
	{
		string slotId = CustomHud.SlotId(CustomHud.Center);
		bool visible = source != null && _player.Options.HudRows.Count > 0; // A layout without fields hides the block

		SendClass(slotId, "hidden", !visible);
		SendExclusive(slotId, "shift", $"shift-{_player.Options.HudPosition(CustomHud.Center)}");
		if (!visible)
			return; // Keep the last texts - they're hidden anyway

		var rows = _player.Options.HudRows.Select(row => row.Select(key => FieldOf(key, source!)).ToList()).ToList();
		for (int r = 0; r < CustomHud.FieldRows; r++)
		{
			List<HudField> row = r < rows.Count ? rows[r] : [];
			SendClass(CustomHud.FieldRowId(r), "hidden", row.Count == 0);

			for (int f = 0; f < CustomHud.FieldsPerRow; f++)
				SendField(r, f, f < row.Count ? row[f] : null);
		}
	}

	// ---- Speed colour (!options - HUD - Speed) ----

	private string _speedClass = "nc-snow";
	private string _centerSpeedClass = "nc-snow";
	private float? _lastShownSpeed;
	/// <summary>How much the shown speed changed since the last update (gain / loss)</summary>
	private float _speedDelta;
	/// <summary>Speed changes smaller than this (u/s per HUD update) count as neither gaining nor losing</summary>
	private const float GainLossDeadZone = 2f;

	/// <summary>The colour class of the shown speed: Nord gradient, gain / loss since the last update, or static</summary>
	private string SpeedClassOf(float speed, SpeedColorMode mode, string staticColor) => mode switch
	{
		SpeedColorMode.Static => $"nc-{staticColor}",
		SpeedColorMode.GainLoss => _speedDelta > GainLossDeadZone ? "nc-green"
			: _speedDelta < -GainLossDeadZone ? "nc-red"
			: "nc-snow",
		_ => CustomHud.SpeedColorClass(speed),
	};

	/// <summary>Prespeed: like the speed, but a single value has no gain / loss (snow then)</summary>
	private string PrespeedClassOf(float speed) => _player.Options.SpeedColor switch
	{
		SpeedColorMode.Static => $"nc-{_player.Options.SpeedColorStatic}",
		SpeedColorMode.GainLoss => "nc-snow",
		_ => CustomHud.SpeedColorClass(speed),
	};

	// ---- Team accent ----

	private static readonly string[] TeamAccentIds =
	[
		CustomHud.SlotId(CustomHud.Top), CustomHud.SlotId(CustomHud.Center), CustomHud.SlotId(CustomHud.Left),
		CustomHud.SlotId(CustomHud.Right), CustomHud.MidId, CustomHud.MenuId,
	];

	/// <summary>
	/// team-t / team-ct for the HUD's accent colour: the shown player's team - our own while alive, the spectated
	/// player's (replay bots are T) while spectating, CT otherwise.
	/// </summary>
	private string TeamClassOf(Player? subject, ReplayPlayer? replay)
	{
		var team = subject?.Controller.Team ?? replay?.Controller?.Team ?? _player.Controller.Team;
		return team == CsTeam.Terrorist ? "team-t" : "team-ct";
	}

	// ---- Center HUD: three lines above, at and below the crosshair ----

	/// <summary>A text shown in a center line instead of its fields until a tick (center messages)</summary>
	private sealed record MidMessage(long Id, string Text, string? ColorClass, int Size, int UntilTick);
	private readonly List<MidMessage>[] _midMessages = Enumerable.Range(0, CustomHud.MidSlots).Select(_ => new List<MidMessage>()).ToArray();
	private static long _nextCenterMessageId;

	internal static long NewCenterMessageId() => ++_nextCenterMessageId;

	/// <summary>
	/// Shows a text in a center line for some ticks (SurfTimer.Api ShowCenterMessage). line null:
	/// a line without fields (at, below, above the crosshair), else the one at the crosshair. Without the custom HUD it's
	/// CS2's center print.
	/// </summary>
	internal void ShowCenterMessage(long id, int? line, string text, int ticks, string? colorClass, int size)
	{
		if (!CustomHud.IsActive)
		{
			_player.Controller.PrintToCenter(text);
			return;
		}

		int target = line is int fixedLine && fixedLine >= 0 && fixedLine < CustomHud.MidSlots ? fixedLine : AutoCenterLine();
		_midMessages[target].Add(new MidMessage(id, text, colorClass, Math.Clamp(size, 1, PlayerOptions.CenterSizes),
			Server.TickCount + Math.Max(1, ticks)));
	}

	internal void ClearCenterMessage(long id)
	{
		foreach (var messages in _midMessages)
			messages.RemoveAll(m => m.Id == id);
	}

	// At the crosshair first, then below, then above
	private static readonly int[] AutoLineOrder = [1, 2, 0];

	/// <summary>The first line without fields or a message - else the one at the crosshair</summary>
	private int AutoCenterLine()
	{
		var options = _player.Options;
		foreach (int line in AutoLineOrder)
		{
			bool fields = options.CenterHud && line < options.CenterLines.Count && options.CenterLines[line].Count > 0;
			if (!fields && ActiveMessage(line) == null)
				return line;
		}
		return 1;
	}

	/// <summary>The newest message of a line that still has time left - expired ones are dropped</summary>
	private MidMessage? ActiveMessage(int line)
	{
		var messages = _midMessages[line];
		messages.RemoveAll(m => m.UntilTick <= Server.TickCount);
		return messages.Count > 0 ? messages[^1] : null;
	}

	/// <summary>A field as center text: its values (keys: the held ones), with its label when the viewer wants labels</summary>
	private string CenterTextOf(HudField field, bool labels)
	{
		string value = field.Segments.Any(s => s.Key)
			? string.Join(" ", field.Segments.Select(s => s.Dim ? "_" : s.Text))
			: string.Join(" ", field.Segments.Where(s => s.Text.Length > 0).Select(s => s.Text));
		return labels ? $"{field.Label} {value}" : value;
	}

	private const string CenterSeparator = "  ·  ";

	/// <summary>
	/// The center HUD: each line a message if one is shown there, else its fields in one text (the HUD addon's middle slots
	/// are single labels) - coloured like its first field, the speed in the center speed colour.
	/// </summary>
	private void SendMid(FieldSource? source)
	{
		var options = _player.Options;
		bool any = false;
		for (int line = 0; line < CustomHud.MidSlots; line++)
		{
			string id = CustomHud.MidSlotId(line);
			string text = "";
			string? colorClass = null;
			int size = options.CenterSize(line);
			bool mono = options.CenterSpeedMono;

			if (ActiveMessage(line) is MidMessage message)
			{
				(text, colorClass, size, mono) = (message.Text, message.ColorClass, message.Size, false);
			}
			else if (options.CenterHud && source != null && line < options.CenterLines.Count)
			{
				var parts = new List<string>();
				foreach (string key in options.CenterLines[line])
				{
					var field = FieldOf(key, source);
					if (!IsAvailable(field))
						continue; // Nothing to show - the center leaves it out instead of N/A
					parts.Add(CenterTextOf(field, options.CenterLabels));
					colorClass ??= HudFieldCatalog.KindOf(key) == HudFieldKind.Speed ? _centerSpeedClass
						: field.Segments.FirstOrDefault(s => s.ColorClass != null).ColorClass ?? "nc-snow";
				}
				text = string.Join(CenterSeparator, parts);
			}

			SendText(id, text);
			SendClass(id, "hidden", text.Length == 0);
			if (text.Length == 0)
				continue;

			any = true;
			SendExclusive(id, "size", $"msize-{size}");
			SendExclusive(id, "pos", $"mpos-{CustomHud.MidPosition(line, options.CenterOffset(line))}");
			SendExclusive(id, "color", colorClass);
			SendExclusive(id, "font", mono ? CustomHud.MonoFontClass : null);
		}
		SendClass(CustomHud.MidId, "hidden", !any);
	}

	// ---- Live pace and energy (!options - Compare - Pace, Energy field) ----

	private readonly PaceTracker _pace = new();
	private float? _lastEnergy;
	/// <summary>Energy changes smaller than this (units per HUD update) count as neither gaining nor losing</summary>
	private const float EnergyDeadZone = 1f;

	/// <summary>The Energy field's colour: green gaining, red losing, white otherwise</summary>
	private string EnergyClassOf(float energy)
	{
		float? last = _lastEnergy;
		_lastEnergy = energy;
		return last is not float before ? "nc-snow"
			: energy - before > EnergyDeadZone ? "nc-green"
			: before - energy > EnergyDeadZone ? "nc-red"
			: "nc-snow";
	}

	// The pace of this update - the splits panel and the pace fields share it (one tracker step per update)
	private int _paceTick = -1;
	private Player? _paceSubject;
	private PaceResult? _paceResult;

	/// <summary>The shown player's live pace against the splits panel's target - null without a reference / match</summary>
	private PaceResult? PaceOf(Player subject)
	{
		if (_paceTick == Server.TickCount && ReferenceEquals(_paceSubject, subject))
			return _paceResult;

		_paceTick = Server.TickCount;
		_paceSubject = subject;
		_paceResult = null;
		if (subject.Controller.PawnIsAlive && PaceReferenceOf(subject, _player.Options.HudSplitTarget) is { } reference)
		{
			float speed = Extensions.SpeedOf(subject.Controller, SpeedAxes.XYZ);
			_paceResult = _pace.Update(subject, reference, speed, Gravity.Current, Server.TickCount);
		}
		return _paceResult;
	}

	/// <summary>
	/// What the pace compares against: the splits panel's target for map runs (PB, WR, #10, a group's rank); in stage /
	/// bonus mode that stage's / bonus's PB (target PB) or WR (any other target). Null when there's no such time.
	/// </summary>
	internal static PaceReference? PaceReferenceOf(Player p, SplitTarget target)
	{
		var map = SurfTimer.CurrentMap;
		int style = p.Timer.Style;
		var replays = map.ReplayManager;

		static IReadOnlyList<ReplayFrame>? Template(Dictionary<int, ReplayPlayer>[] byNumber, int number, int style, int? replayId) =>
			number > 0 && number < byNumber.Length && byNumber[number] != null && byNumber[number].TryGetValue(style, out var template)
				&& template.ReplayId == replayId ? template.Frames : null;

		if (p.Timer.IsBonusMode && RunTargets.HasBonusData(p, style))
		{
			short bonus = p.Timer.Bonus;
			var pb = p.Stats.BonusPB[bonus][style];
			if (target == SplitTarget.Pb && pb.RunTime > 0)
				return new($"bpb{bonus}:{pb.ID}", pb.RunTime, null, pb.ReplayId, null, 1, bonus);
			var wr = map.BonusWR[bonus][style];
			return wr.RunTime > 0
				? new($"bwr{bonus}:{wr.ID}", wr.RunTime, null, wr.ReplayId, Template(replays.AllBonusWR, bonus, style, wr.ReplayId), 1, bonus)
				: null;
		}

		if (p.Timer.IsStageMode && RunTargets.HasStageData(p, style))
		{
			short stage = p.Timer.Stage;
			var pb = p.Stats.StagePB[stage][style];
			if (target == SplitTarget.Pb && pb.RunTime > 0)
				return new($"spb{stage}:{pb.ID}", pb.RunTime, null, pb.ReplayId, null, 2, stage);
			var wr = map.StageWR[stage][style];
			return wr.RunTime > 0
				? new($"swr{stage}:{wr.ID}", wr.RunTime, null, wr.ReplayId, Template(replays.AllStageWR, stage, style, wr.ReplayId), 2, stage)
				: null;
		}

		if (p.Timer.IsStageMode || p.Timer.IsBonusMode)
			return null; // No data for that stage / bonus

		var (_, splits, rank) = ResolveSplitTarget(p, target);
		if (rank == 0)
		{
			var pb = p.Stats.PB.GetValueOrDefault(style);
			return pb != null && pb.RunTime > 0 ? new($"pb:{pb.ID}", pb.RunTime, splits, pb.ReplayId, null, 0, 0) : null;
		}
		if (rank == 1)
		{
			var wr = map.WR.GetValueOrDefault(style);
			var template = style == 0 && replays.MapWR?.ReplayId == wr?.ReplayId ? replays.MapWR?.Frames : null;
			return wr != null && wr.RunTime > 0 ? new($"wr:{wr.ID}", wr.RunTime, splits, wr.ReplayId, template, 0, 0) : null;
		}
		var ranked = rank > 1 ? map.SplitTargets.Get(map.CourseId(0, 0), style, rank) : null;
		return ranked != null && ranked.RunTime > 0
			? new($"rank{rank}:{ranked.RunTime}", ranked.RunTime, splits, ranked.ReplayId, null, 0, 0)
			: null;
	}

	/// <summary>The map run of a leaderboard rank (1 = WR) as a reference - null while its time loads, or without one</summary>
	internal static PaceReference? RankReferenceOf(Player p, int rank)
	{
		if (rank == 1)
			return PaceReferenceOf(p, SplitTarget.Wr);
		var map = SurfTimer.CurrentMap;
		var ranked = rank > 1 ? map.SplitTargets.Get(map.CourseId(0, 0), p.Timer.Style, rank) : null;
		return ranked != null && ranked.RunTime > 0
			? new($"rank{rank}:{ranked.RunTime}", ranked.RunTime, ranked.Splits, ranked.ReplayId, null, 0, 0)
			: null;
	}

	/// <summary>
	/// Under the splits: the projected finish ("~" when only from the splits) and how far ahead / behind - and, with
	/// Energy vs reference, the energy and speed difference to the reference at the same spot.
	/// </summary>
	private List<List<HudElement>> PaceRows(Player subject, PlayerOptions options)
	{
		if (PaceOf(subject) is not PaceResult pace)
			return [];

		var rows = new List<List<HudElement>>
		{
			new()
			{
				new("Pace", (pace.Live ? "" : "~") + FormatTime(pace.PaceTicks), ""),
				new("", $"{(pace.DeltaTicks <= 0 ? "-" : "+")}{FormatTime(Math.Abs(pace.DeltaTicks))}",
					pace.DeltaTicks <= 0 ? TimerColorActive : SlowerColor, Size: HudSize.Small),
			},
		};
		if (options.HudPaceEnergy && pace.EnergyDiff is float energy && pace.SpeedDiff is float speedDiff)
		{
			rows.Add(
			[
				new("Energy", Signed(energy), energy >= 0 ? TimerColorActive : SlowerColor, Size: HudSize.Small),
				new("Speed", Signed(speedDiff), speedDiff >= 0 ? TimerColorActive : SlowerColor, Size: HudSize.Small),
			]);
		}
		return rows;
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
				e.Label ? "lbl" : "val", e.Label ? "sm" : SizeClass(e.Size), e.Mono));
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
				SendExclusive(id, "font", segment.Mono ? CustomHud.MonoFontClass : null);
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
		if (subject.TimerDisabled)
			flags.Add("Noclip");
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

	// The splits panel as last shown during a map run, and whose run it was (!options - HUD - Splits panel - Keep last splits)
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
		var (header, targetSplits, _) = ResolveSplitTarget(p, target);

		var rows = new List<List<HudElement>> { new() { new("", header, SpectatorColor, Label: true) } };
		// With the pace below (two rows), fewer splits fit the panel
		int lines = _player.Options.HudPace ? PaceSplitLines : MaxSplitLines;
		foreach (var cp in p.Stats.ThisRun.Checkpoints.OrderByDescending(cp => cp.Key).Take(lines).OrderBy(cp => cp.Key))
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
	/// <returns>Rank: 0 the PB, 1 the WR, n that leaderboard rank, -1 none</returns>
	private static (string Header, Dictionary<int, CheckpointEntity>? Splits, int Rank) ResolveSplitTarget(Player p, SplitTarget target)
	{
		var map = SurfTimer.CurrentMap;
		int style = p.Timer.Style;
		var wr = map.WR.GetValueOrDefault(style);
		var wrSplits = wr != null && wr.RunTime > 0 ? wr.Checkpoints : null;
		var pb = p.Stats.PB.GetValueOrDefault(style);

		if (target == SplitTarget.Wr)
			return ("Splits vs WR", wrSplits, 1);
		if (target is not (SplitTarget.Top10 or >= SplitTarget.G1 and <= SplitTarget.G5))
			return ("Splits vs PB", pb?.Checkpoints, 0);

		int completions = map.MapCompletions.GetValueOrDefault(style);
		int rank;
		string name;
		if (target == SplitTarget.Top10)
		{
			int pbRank = pb != null && pb.ID != -1 && pb.RunTime > 0 ? pb.Rank : 0;
			if (pbRank == 1)
				return ("Splits vs WR", wrSplits, 1);
			rank = pbRank is >= 2 and <= 10 ? pbRank - 1 : Math.Min(10, completions);
			name = rank > 0 ? $"#{rank}" : "#10";
		}
		else
		{
			int group = target - SplitTarget.G1 + 1;
			if (completions < 11)
				return ($"Splits vs G{group} · none yet", null, -1);
			rank = Math.Min(PointsCalculator.GroupLastRank(completions, group), completions);
			name = $"G{group} · #{rank}";
		}

		string header = $"Splits vs {name}";
		if (rank < 1)
			return (header, null, -1);
		if (rank == 1)
			return (header, wrSplits, 1); // Already in memory
		return (header, map.SplitTargets.Get(map.CourseId(0, 0), style, rank)?.Splits, rank);
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

	// ---- Popup menu (z4_menu) ----
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
	/// <param name="atStage">At a stage start of a staged map (stage N start = checkpoint N-1) - labelled with the finished stage N-1, like the splits panel</param>
	internal void DisplayCheckpointMessages(bool atStage = false)
	{
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

		// The time at this checkpoint with its rank among every map PB's split here: "1:23.456 [3/120]"
		string splitTime = CourseRanks.SplitTime(_player, playerCurrentCheckpoint, _player.Timer.Ticks);

		// The runner and their spectators by their own toggle, others by theirs (!options - Chat)
		var message = ChatAnnounce.Msg(atStage ? "stage_split_message" : "checkpoint_split_message",
			playerCurrentCheckpoint, splitTime, currentSpeed.ToString("0"), strPbDifference, strWrDifference,
			TimeOnly(strPbDifference), TimeOnly(strWrDifference));
		ChatAnnounce.Run(ChatEvent.SplitMap, _player, message, ChatAnnounce.About(_player, message));

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
		int style = _player.Timer.Style;
		var axes = _player.Options.SpeedAxes;
		float exitSpeed = Extensions.Speed(exitVelocity, axes);

		// Runs inside zone touch handlers - an unknown stage must not throw and abort the handler
		if (!RunTargets.HasEntry(_player.Stats.StagePB, stage, style) || !RunTargets.HasEntry(SurfTimer.CurrentMap.StageWR, stage, style))
			return;

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

		var message = ChatAnnounce.Msg("stage_message",
			stage, CourseRanks.Time(_player, 2, stage, stageRunTime), exitSpeed.ToString("0"), strPbDifference, strWrDifference,
			TimeOnly(strPbDifference), TimeOnly(strWrDifference));
		ChatAnnounce.Run(ChatEvent.SplitSegment, _player, message, ChatAnnounce.About(_player, message));
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
		int style = _player.Timer.Style;
		var axes = _player.Options.SpeedAxes;
		float exitSpeed = Extensions.Speed(exitVelocity, axes);

		// Runs inside zone touch handlers - an unknown segment must not throw and abort the handler
		if (!RunTargets.HasEntry(_player.Stats.CheckpointPB, checkpoint, style) || !RunTargets.HasEntry(SurfTimer.CurrentMap.CheckpointWR, checkpoint, style))
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

		var message = ChatAnnounce.Msg("checkpoint_message",
			checkpoint, CourseRanks.Time(_player, 3, checkpoint, checkpointRunTime), exitSpeed.ToString("0"), strPbDifference, strWrDifference,
			TimeOnly(strPbDifference), TimeOnly(strWrDifference));
		ChatAnnounce.Run(ChatEvent.SplitSegment, _player, message, ChatAnnounce.About(_player, message));
	}

	/// <summary>A split difference "time (speed)" without its speed - for compact messages</summary>
	private static string TimeOnly(string difference) => difference[..difference.IndexOf(" (")];
}
