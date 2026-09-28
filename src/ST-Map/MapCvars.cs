using System.Globalization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace SurfTimer;

/// <summary>
/// Per-map cvar overrides (map_settings keys "cvar.&lt;name&gt;"), limited to movement cvars. The server's
/// value is remembered when a cvar is first overridden and put back when the override is removed or the
/// map ends. Main thread only.
/// </summary>
internal static class MapCvars
{
	internal const string SettingPrefix = "cvar.";

	internal static readonly IReadOnlyList<string> Whitelist =
	[
		"sv_airaccelerate", "sv_accelerate", "sv_gravity", "sv_maxvelocity", "sv_friction",
		"sv_staminajumpcost", "sv_staminalandcost", "sv_enablebunnyhopping", "sv_autobunnyhopping",
	];

	// Numbers only - the value ends up in a console command
	private static readonly Regex ValuePattern = new(@"^-?\d{1,7}(\.\d{1,4})?$", RegexOptions.Compiled);

	private static readonly Dictionary<string, string> _originals = new(StringComparer.OrdinalIgnoreCase);

	internal static bool IsAllowed(string name) => Whitelist.Contains(name, StringComparer.OrdinalIgnoreCase);

	internal static bool IsValidValue(string value) => ValuePattern.IsMatch(value);

	internal static string SettingKey(string name) => SettingPrefix + name.ToLowerInvariant();

	/// <summary>The cvar's current value as typed in the console, or "" if it doesn't exist.</summary>
	internal static string Current(string name)
	{
		var convar = ConVar.Find(name);
		return convar == null ? "" : ConVarHelper.Read(convar);
	}

	/// <summary>The server's own value (before this map's override), or the current one when not overridden.</summary>
	internal static string Default(string name) => _originals.TryGetValue(name, out var value) ? value : Current(name);

	/// <summary>
	/// Applies the map's overrides and restores cvars that aren't overridden (any more).
	/// </summary>
	internal static void Apply(IReadOnlyDictionary<string, string> settings)
	{
		foreach (var name in _originals.Keys.ToList())
		{
			if (!settings.ContainsKey(SettingKey(name)))
				Restore(name);
		}

		foreach (var name in Whitelist)
		{
			if (!settings.TryGetValue(SettingKey(name), out var value) || !IsValidValue(value))
				continue;

			var convar = ConVar.Find(name);
			if (convar == null)
				continue;

			if (!_originals.ContainsKey(name))
				_originals[name] = ConVarHelper.Read(convar);
			Server.ExecuteCommand($"{name} {value}");
		}
	}

	/// <summary>
	/// Puts every overridden cvar back (map end).
	/// </summary>
	internal static void RestoreAll()
	{
		foreach (var name in _originals.Keys.ToList())
			Restore(name);
	}

	private static void Restore(string name)
	{
		if (_originals.Remove(name, out var original) && IsValidValue(original))
			Server.ExecuteCommand($"{name} {original}");
	}

	internal static string Format(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
