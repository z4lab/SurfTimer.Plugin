using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Maps can run console commands through their scripts (point_script: Instance.ServerCommand) - the
/// point_servercommand entities removed on map load are only one way. Those arrive as commands from
/// the server (no player), so they're filtered here:
/// - chat (`say`, `say_team`) - map ads/spam (Config.BlockMapChat);
/// - bot kicks (`bot_kick`, `kick`/`kickid` on a bot) the plugin didn't ask for, so maps can't kick
///   replay bots (Config.ProtectReplayBots).
/// Everything blocked is logged. Commands from players are never touched.
/// </summary>
public partial class SurfTimer
{
	// UserIds the plugin itself is kicking (idle replay bots) - their kickid is let through once
	private static readonly HashSet<int> _pluginKicks = new();

	/// <summary>
	/// Lets the plugin's own `kickid` for this user through the bot kick protection.
	/// </summary>
	internal static void AllowPluginKick(int userId) => _pluginKicks.Add(userId);

	private void RegisterMapCommandFilter()
	{
		if (Config.BlockMapChat)
		{
			AddCommandListener("say", BlockServerChat);
			AddCommandListener("say_team", BlockServerChat);
		}

		if (Config.ProtectReplayBots)
		{
			AddCommandListener("bot_kick", BlockBotKick);
			AddCommandListener("kickid", BlockBotKickId);
			AddCommandListener("kick", BlockBotKickByName);
		}
	}

	/// <summary>
	/// Bot settings a map could change so bots can't join (bot_add / replay bots silently doing
	/// nothing). Checked about once a second while protect_replay_bots is on - anything changed is
	/// logged and set back. Values are what the plugin's replay bots need; bot_join_team is only
	/// corrected when it keeps bots out entirely.
	/// </summary>
	private static readonly (string Name, string Value)[] EnforcedBotConVars =
	[
		("bot_quota_mode", "normal"),
		("bot_join_after_player", "0"),
		("mp_limitteams", "0"),
		("mp_autoteambalance", "0"),
	];

	private void EnforceBotConVars()
	{
		if (!Config.ProtectReplayBots)
			return;

		foreach (var (name, value) in EnforcedBotConVars)
		{
			var convar = ConVar.Find(name);
			if (convar == null)
				continue;

			string current = ReadConVar(convar);
			if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
				continue;

			_logger.LogInformation("[{ClassName}] {ConVar} was changed to '{Current}' (map/config?) - set back to '{Value}' for replay bots",
				nameof(SurfTimer), name, current, value);
			Server.ExecuteCommand($"{name} {value}");
		}

		// Bots may join CT, T or any - but not be kept out of the teams
		var joinTeam = ConVar.Find("bot_join_team");
		if (joinTeam != null)
		{
			string team = joinTeam.StringValue;
			if (!team.Equals("ct", StringComparison.OrdinalIgnoreCase) && !team.Equals("t", StringComparison.OrdinalIgnoreCase)
				&& !team.Equals("any", StringComparison.OrdinalIgnoreCase))
			{
				_logger.LogInformation("[{ClassName}] bot_join_team was changed to '{Current}' - set back to 'any' for replay bots",
					nameof(SurfTimer), team);
				Server.ExecuteCommand("bot_join_team any");
			}
		}
	}

	// ConVars are typed - read them as the text you'd type in the console
	private static string ReadConVar(ConVar convar) => convar.Type switch
	{
		ConVarType.Bool => convar.GetPrimitiveValue<bool>() ? "1" : "0",
		ConVarType.Int16 or ConVarType.Int32 or ConVarType.UInt16 or ConVarType.UInt32 => convar.GetPrimitiveValue<int>().ToString(),
		ConVarType.Int64 or ConVarType.UInt64 => convar.GetPrimitiveValue<long>().ToString(),
		ConVarType.Float32 => convar.GetPrimitiveValue<float>().ToString(System.Globalization.CultureInfo.InvariantCulture),
		ConVarType.Float64 => convar.GetPrimitiveValue<double>().ToString(System.Globalization.CultureInfo.InvariantCulture),
		_ => convar.StringValue,
	};

	/// <summary>
	/// Bots joining - with OnPlayerDisconnect's bot log this shows whether a bot was added and
	/// removed again straight away (and why), or never added at all.
	/// </summary>
	[GameEventHandler]
	public HookResult OnBotConnect(EventPlayerConnect @event, GameEventInfo info)
	{
		if (@event.Bot)
			_logger.LogInformation("[{ClassName}] Bot connected: {Name}", nameof(SurfTimer), @event.Name);
		return HookResult.Continue;
	}

	private HookResult BlockServerChat(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null)
			return HookResult.Continue; // A player's own chat

		_logger.LogInformation("[{ClassName}] Blocked server chat: {Command} {Args}",
			nameof(SurfTimer), command.GetArg(0), command.ArgString);
		return HookResult.Stop;
	}

	private HookResult BlockBotKick(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null)
			return HookResult.Continue;

		_logger.LogInformation("[{ClassName}] Blocked bot kick: bot_kick {Args}", nameof(SurfTimer), command.ArgString);
		return HookResult.Stop;
	}

	private HookResult BlockBotKickId(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null || command.ArgCount < 2)
			return HookResult.Continue;

		if (!int.TryParse(command.GetArg(1).TrimStart('#'), out int userId))
			return HookResult.Continue;

		if (_pluginKicks.Remove(userId))
			return HookResult.Continue; // The plugin's own kick (idle replay bot)

		var target = Utilities.GetPlayerFromUserid(userId);
		if (target == null || !target.IsValid || !target.IsBot)
			return HookResult.Continue; // Humans can still be kicked from the console

		_logger.LogInformation("[{ClassName}] Blocked bot kick: kickid {Args} ({Bot})",
			nameof(SurfTimer), command.ArgString, target.PlayerName);
		return HookResult.Stop;
	}

	private HookResult BlockBotKickByName(CCSPlayerController? player, CommandInfo command)
	{
		if (player != null || command.ArgCount < 2)
			return HookResult.Continue;

		string name = command.ArgString.Trim().Trim('"');
		bool isBot = Utilities.GetPlayers().Any(p => p.IsValid && p.IsBot && p.PlayerName == name);
		if (!isBot)
			return HookResult.Continue;

		_logger.LogInformation("[{ClassName}] Blocked bot kick: kick {Args}", nameof(SurfTimer), command.ArgString);
		return HookResult.Stop;
	}
}
