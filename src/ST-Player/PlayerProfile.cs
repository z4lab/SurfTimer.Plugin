namespace SurfTimer;

/// <summary>
/// The connected player's stored profile, their open session and persisted settings.
/// </summary>
public class PlayerProfile : PlayerProfileEntity
{
	/// <summary>player_sessions row of this connection (0 until opened)</summary>
	internal long SessionId { get; set; }

	/// <summary>player_settings, loaded on connect</summary>
	internal Dictionary<string, string> Settings { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

	internal const string SettingHideSelf = "hideself";

	/// <summary>
	/// Creates / updates the player's row (name, country, last seen, name history), opens a session for
	/// this map and loads their settings.
	/// </summary>
	/// <param name="country">ISO country code - "XX" (unknown) keeps the stored one</param>
	internal static async Task<PlayerProfile> CreateAsync(ulong steamId, string name, string country, int? mapId)
	{
		var stored = await PlayerRepository.UpsertAsync(steamId, name, country is "" or "XX" ? null : country);
		var profile = new PlayerProfile
		{
			ID = stored.ID,
			SteamID = stored.SteamID,
			Name = stored.Name,
			Country = stored.Country ?? country,
			JoinDate = stored.JoinDate,
			LastSeen = stored.LastSeen,
			Connections = stored.Connections + 1, // This session
		};

		profile.SessionId = await PlayerRepository.OpenSessionAsync(profile.ID, mapId);
		profile.Settings = await PlayerRepository.GetSettingsAsync(profile.ID);
		return profile;
	}

	internal bool GetBoolSetting(string key, bool fallback) =>
		Settings.TryGetValue(key, out var value) ? value is "1" or "true" : fallback;

	/// <summary>
	/// Stores a setting (in the background).
	/// </summary>
	internal void SetSetting(string key, string value)
	{
		Settings[key] = value;
		int playerId = ID;
		_ = Task.Run(() => PlayerRepository.SetSettingAsync(playerId, key, value));
	}
}
