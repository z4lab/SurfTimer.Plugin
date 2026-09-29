using System.Collections;
using System.Reflection;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Whether a chat command (css_&lt;name&gt;) exists in any loaded plugin. CounterStrikeSharp has no public
/// lookup, so the core command manager's table is read by reflection (every plugin registers there).
/// If that ever breaks, only this plugin's own commands are known - logged once.
/// </summary>
internal static class CommandRegistry
{
	private static IDictionary? _definitions;
	private static HashSet<string> _ownCommands = new(StringComparer.OrdinalIgnoreCase);

	internal static void Init(BasePlugin plugin, ILogger logger)
	{
		_ownCommands = plugin.GetType()
			.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			.SelectMany(m => m.GetCustomAttributes<ConsoleCommandAttribute>())
			.Select(a => a.Command)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		try
		{
			// BasePlugin.CommandManager (PluginCommandManagerDecorator) -> _inner (core CommandManager) -> _commandDefinitions
			object? manager = plugin.CommandManager;
			var inner = manager?.GetType().GetField("_inner", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager);
			if (inner != null)
				manager = inner;
			_definitions = manager?.GetType().GetField("_commandDefinitions", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager) as IDictionary;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "[Chat] Reading CounterStrikeSharp's command table failed");
		}

		if (_definitions == null)
			logger.LogWarning("[Chat] CounterStrikeSharp's command table isn't readable - only SurfTimer's own commands are hidden from chat");
	}

	/// <summary>
	/// Whether css_&lt;name&gt; (or &lt;name&gt; when it already starts with css_) is a registered command.
	/// </summary>
	internal static bool Exists(string name)
	{
		if (name.Length == 0)
			return false;

		string command = name.StartsWith("css_", StringComparison.OrdinalIgnoreCase) ? name : "css_" + name;
		if (_ownCommands.Contains(command))
			return true;
		if (_definitions == null)
			return false;

		if (_definitions.Contains(command) || _definitions.Contains(command.ToLowerInvariant()))
			return true;

		foreach (var key in _definitions.Keys)
		{
			if (key is string registered && registered.Equals(command, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}
}
