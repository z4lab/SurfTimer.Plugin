using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SurfTimer;

/// <summary>
/// One kind of sound a player can turn on / off (!options - Sound). Own sounds are on by default, sounds about other
/// players off. Addons add their own (SurfTimer.Api RegisterSoundCategory) - they're listed after the timer's.
/// </summary>
/// <param name="Key">Saved as player setting "sound_&lt;key&gt;"</param>
/// <param name="DefaultEvent">The CS2 sound event played - timer_settings.json "sound_&lt;key&gt;" overrides it ("" = none)</param>
internal sealed record SoundCategory(string Key, string Label, string Sub, bool DefaultOn, string DefaultEvent = "", float Pitch = 1f)
{
	/// <summary>The sound event to play (timer setting, else the default)</summary>
	internal string Event => Config.SoundEvent(Key, DefaultEvent);
}

/// <summary>
/// Sounds for one player: only when their sounds are on (!quake / !options - Sound), the kind is on, and at their own
/// volume. Sound events (e.g. "UI.XP.Star.Full") get the volume and pitch; a file path ("sounds/....vsnd") only goes
/// through the client's "play", which knows no volume. Played on the next frame - safe from the save tasks.
/// </summary>
internal static class Sounds
{
	internal static readonly SoundCategory TimerStart =
		new("timer_start", "Timer start", "your run starts", true, "UIPanorama.generic_button_press");
	internal static readonly SoundCategory AheadOfPb =
		new("ahead_pb", "Ahead of PB", "faster than your PB at a checkpoint / stage", true, "UIPanorama.round_report_odds_up");
	internal static readonly SoundCategory Pb =
		new("pb", "New PB", "map, stage, bonus or checkpoint", true, "UI.XP.Star.Full");
	internal static readonly SoundCategory Record =
		new("record", "Your record", "WR on the map, a stage, bonus or checkpoint", true, "UIPanorama.XP.NewSkillGroup");
	internal static readonly SoundCategory OthersRecord =
		new("others_record", "Others' records", "another player sets a WR", false, "UI.ArmsRace.BecomeMatchLeader");
	// The timer start click, lower - the first !r during a run (Options - Gameplay - Confirm !r)
	internal static readonly SoundCategory ResetConfirm =
		new("reset_confirm", "Reset warning", "first !r during a run", true, "UIPanorama.generic_button_press", Pitch: 0.7f);

	private static readonly List<SoundCategory> Builtin = [TimerStart, AheadOfPb, Pb, Record, OthersRecord, ResetConfirm];
	private static readonly List<SoundCategory> Addon = [];

	/// <summary>All kinds in !options order: the timer's, then the addons'</summary>
	internal static IEnumerable<SoundCategory> Categories => Builtin.Concat(Addon);

	internal static SoundCategory? Find(string key) => Categories.FirstOrDefault(c => c.Key == key);

	/// <summary>Adds (or replaces) an addon's kind of sound</summary>
	internal static void Register(SoundCategory category)
	{
		if (Builtin.Any(c => c.Key == category.Key))
			throw new ArgumentException($"Sound category '{category.Key}' is the timer's own");
		Addon.RemoveAll(c => c.Key == category.Key);
		Addon.Add(category);
	}

	internal static void ClearAddons() => Addon.Clear();

	/// <summary>A kind's own sound</summary>
	internal static void Play(Player player, SoundCategory category) => Play(player, category.Event, category.Pitch, category);

	/// <summary>
	/// A sound to the player - category null: only the main switch and volume apply (e.g. the !options sample).
	/// </summary>
	internal static void Play(Player player, string sound, float pitch, SoundCategory? category)
	{
		if (sound.Length == 0)
			return;

		Server.NextFrame(() =>
		{
			var controller = player.Controller;
			var options = player.Options;
			int volume = options.SoundVolume;
			if (!controller.IsValid || controller.IsBot || !options.SoundsEnabled || volume <= 0
				|| (category != null && !options.SoundOn(category)))
				return;

			if (sound.Contains('/') || sound.EndsWith(".vsnd", StringComparison.OrdinalIgnoreCase))
			{
				controller.ExecuteClientCommand($"play {sound}");
				return;
			}

			controller.EmitSound(sound, new RecipientFilter { controller }, volume / 100f, pitch);
		});
	}

	/// <summary>A record: the subject's own sound, and everyone else's "others' records" sound</summary>
	internal static void PlayRecord(Player subject)
	{
		Play(subject, Record);
		foreach (var other in SurfTimer.OnlinePlayers)
		{
			if (!ReferenceEquals(other, subject))
				Play(other, OthersRecord);
		}
	}

	/// <summary>
	/// A map run reached a checkpoint / stage: the "ahead of PB" sound when it's faster than the PB's split there.
	/// split = the PB checkpoint key (checkpoint number, or stage - 1 on staged maps).
	/// </summary>
	internal static void CheckAheadOfPb(Player player, int split)
	{
		if (!player.Timer.IsRunning || player.Timer.IsPracticeMode)
			return;

		var pb = player.Stats.PB[player.Timer.Style].Checkpoints;
		if (pb != null && pb.TryGetValue(split, out var pbSplit) && pbSplit.RunTime > 0 && player.Timer.Ticks < pbSplit.RunTime)
			Play(player, AheadOfPb);
	}
}
