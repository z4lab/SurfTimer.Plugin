using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>Game sounds a player can silence (!options - Sound - Game sounds)</summary>
internal enum GameSoundGroup
{
	Footsteps,
	FallDamage,
	Water,
	DamageDeath,
	Weapons,
	Map,
	Other,
}

/// <summary>
/// Game sounds per player: every sound the game plays reaches clients as CMsgSosStartSoundEvent - a player who turned a
/// group off is taken out of its recipients. The group comes from the sound event's name hash (GameSoundNames), else
/// from who plays it: a player / weapon - "other game sounds", anything else - map sounds. The timer's own sounds (played
/// on the player controller) are never touched. Other players' gunshots come as CMsgTEFireBullets - "weapons" removes
/// those too (their tracers / impacts with them). Sounds a client plays itself (some of its own movement, predicted)
/// can't be reached from the server.
/// </summary>
public partial class SurfTimer
{
	internal static readonly (GameSoundGroup Group, string Label, string Sub)[] GameSoundGroups =
	[
		(GameSoundGroup.Footsteps, "Footsteps & landing", "walking, ladders, landing, jumping"),
		(GameSoundGroup.FallDamage, "Fall damage", "the fall damage sound"),
		(GameSoundGroup.Water, "Water", "wading, swimming, splashes, underwater"),
		(GameSoundGroup.DamageDeath, "Damage & death", "hits, deaths, headshots"),
		(GameSoundGroup.Weapons, "Weapons & items", "shots, reloads, switching, pickups"),
		(GameSoundGroup.Map, "Map sounds", "ambience, map music, props"),
		(GameSoundGroup.Other, "Other game sounds", "everything else, e.g. the surf wind"),
	];

	private static readonly int GameSoundGroupCount = Enum.GetValues<GameSoundGroup>().Length;

	/// <summary>Per group: the slots of players who turned it off - rebuilt every half second and on every change</summary>
	private readonly ulong[] _gameSoundMuted = new ulong[GameSoundGroupCount];
	private bool _anyGameSoundMuted;

	private static readonly Dictionary<uint, GameSoundGroup> GameSoundByHash = BuildGameSoundHashes();

	private int _sosStartMessageId = -1;
	private int _fireBulletsMessageId = -1;

	private void HookGameSounds()
	{
		try
		{
			_sosStartMessageId = UserMessage.FindIdByName("CMsgSosStartSoundEvent");
			_fireBulletsMessageId = UserMessage.FindIdByName("CMsgTEFireBullets");
			HookUserMessage(_sosStartMessageId, OnSosStartSound, HookMode.Pre);
			HookUserMessage(_fireBulletsMessageId, OnFireBullets, HookMode.Pre);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "[CS2 Surf] Hooking game sounds failed - Options › Sound › Game sounds won't silence anything");
		}
	}

	/// <summary>Who turned which group off - every half second (players come and go) and after every change</summary>
	internal void RebuildGameSoundMutes()
	{
		Array.Clear(_gameSoundMuted);
		foreach (var p in playerList.Values)
		{
			var controller = p.Controller;
			if (!controller.IsValid || controller.IsBot)
				continue;
			ulong bit = 1UL << controller.Slot;
			for (int group = 0; group < GameSoundGroupCount; group++)
			{
				if (!p.Options.GameSoundOn((GameSoundGroup)group))
					_gameSoundMuted[group] |= bit;
			}
		}
		_anyGameSoundMuted = _gameSoundMuted.Any(mask => mask != 0);
	}

	private HookResult OnSosStartSound(UserMessage message)
	{
		if (!_anyGameSoundMuted)
			return HookResult.Continue;

		var group = GroupOf(message.ReadUInt("soundevent_hash"), message.ReadInt("source_entity_index"));
		if (group is GameSoundGroup g)
			RemoveRecipients(message, _gameSoundMuted[(int)g]);
		return HookResult.Continue;
	}

	private HookResult OnFireBullets(UserMessage message)
	{
		if (_anyGameSoundMuted)
			RemoveRecipients(message, _gameSoundMuted[(int)GameSoundGroup.Weapons]);
		return HookResult.Continue;
	}

	private static void RemoveRecipients(UserMessage message, ulong muted)
	{
		if (muted == 0)
			return;
		ulong recipients = message.Recipients.GetRecipientMask();
		if ((recipients & muted) != 0)
			message.Recipients = new RecipientFilter(recipients & ~muted);
	}

	/// <summary>The group of a sound - null: the timer's own (or an addon's) sound, never silenced</summary>
	private static GameSoundGroup? GroupOf(uint hash, int sourceIndex)
	{
		var source = sourceIndex > 0 ? Utilities.GetEntityFromIndex<CBaseEntity>(sourceIndex) : null;
		string designer = source != null && source.IsValid ? source.DesignerName : "";

		// The timer's sounds play on the controller (Sounds.cs), game sounds on pawns, weapons and the world
		if (designer == "cs_player_controller")
			return null;
		if (GameSoundByHash.TryGetValue(hash, out var group))
			return group;
		return designer == "player" || designer.StartsWith("weapon_", StringComparison.Ordinal)
			|| designer == "predicted_viewmodel"
			? GameSoundGroup.Other
			: GameSoundGroup.Map;
	}

	private static Dictionary<uint, GameSoundGroup> BuildGameSoundHashes()
	{
		var hashes = new Dictionary<uint, GameSoundGroup>();
		void Add(GameSoundGroup group, IEnumerable<string> names)
		{
			foreach (string name in names)
				hashes.TryAdd(SoundEventHash(name), group);
		}
		Add(GameSoundGroup.Footsteps, GameSoundNames.Footsteps);
		Add(GameSoundGroup.Water, GameSoundNames.Water);
		Add(GameSoundGroup.FallDamage, GameSoundNames.FallDamage);
		Add(GameSoundGroup.DamageDeath, GameSoundNames.DamageDeath);
		Add(GameSoundGroup.Weapons, GameSoundNames.Weapons);
		return hashes;
	}

	/// <summary>A sound event's soundevent_hash: MurmurHash2 of the lowercased name, seed "SRC2" (0x53524332)</summary>
	internal static uint SoundEventHash(string name)
	{
		const uint m = 0x5bd1e995;
		const int r = 24;
		byte[] data = Encoding.UTF8.GetBytes(name.ToLowerInvariant());
		int length = data.Length;
		uint h = 0x53524332u ^ (uint)length;

		int i = 0;
		while (length >= 4)
		{
			uint k = BitConverter.ToUInt32(data, i);
			k *= m;
			k ^= k >> r;
			k *= m;
			h *= m;
			h ^= k;
			i += 4;
			length -= 4;
		}

		switch (length)
		{
			case 3:
				h ^= (uint)data[i + 2] << 16;
				goto case 2;
			case 2:
				h ^= (uint)data[i + 1] << 8;
				goto case 1;
			case 1:
				h ^= data[i];
				h *= m;
				break;
		}

		h ^= h >> 13;
		h *= m;
		h ^= h >> 15;
		return h;
	}
}
