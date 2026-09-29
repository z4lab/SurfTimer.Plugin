using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Gags from CS2-SimpleAdmin. Its API (ICS2_SimpleAdminApi, capability "simpleadmin:api") is looked up
/// at runtime by reflection, so there's no build dependency and the timer works without SimpleAdmin -
/// gags are then simply not checked.
/// </summary>
internal static class SimpleAdminGags
{
	private const string CapabilityName = "simpleadmin:api";
	private const string ApiTypeName = "ICS2_SimpleAdminApi";

	private static object? _api;
	private static MethodInfo? _getMuteStatus;
	private static ILogger? _logger;
	private static bool _loggedFailure;

	/// <summary>
	/// Finds SimpleAdmin's API - called from OnAllPluginsLoaded (and again on hot reloads).
	/// </summary>
	internal static void Resolve(ILogger logger)
	{
		_logger = logger;
		_api = null;
		_getMuteStatus = null;

		try
		{
			var apiType = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a =>
				{
					try { return a.GetTypes(); }
					catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>(); }
					catch { return []; }
				})
				.FirstOrDefault(t => t.IsInterface && t.Name == ApiTypeName);

			if (apiType == null)
			{
				logger.LogInformation("[Chat] CS2-SimpleAdmin not found - gags aren't checked by the chat processor");
				return;
			}

			var capabilityType = typeof(PluginCapability<>).MakeGenericType(apiType);
			var capability = Activator.CreateInstance(capabilityType, CapabilityName);
			_api = capabilityType.GetMethod("Get")?.Invoke(capability, null);
			_getMuteStatus = apiType.GetMethod("GetPlayerMuteStatus", [typeof(CCSPlayerController)]);

			if (_api == null || _getMuteStatus == null)
			{
				logger.LogWarning("[Chat] CS2-SimpleAdmin's API isn't available ({Api}, GetPlayerMuteStatus {Method}) - gags aren't checked",
					_api == null ? "no capability" : "ok", _getMuteStatus == null ? "missing" : "ok");
				_api = null;
				return;
			}

			logger.LogInformation("[Chat] Using CS2-SimpleAdmin gags");
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "[Chat] Looking up CS2-SimpleAdmin's API failed - gags aren't checked");
			_api = null;
		}
	}

	/// <summary>
	/// Whether the player has an active Gag or Silence in SimpleAdmin.
	/// </summary>
	internal static bool IsGagged(CCSPlayerController player)
	{
		if (_api == null || _getMuteStatus == null)
			return false;

		try
		{
			// Dictionary<PenaltyType, List<(DateTime EndDateTime, int Duration, bool Passed)>>
			if (_getMuteStatus.Invoke(_api, [player]) is not IDictionary status)
				return false;

			foreach (DictionaryEntry entry in status)
			{
				string type = entry.Key?.ToString() ?? "";
				if (type is not ("Gag" or "Silence") || entry.Value is not IEnumerable penalties)
					continue;

				foreach (var penalty in penalties)
				{
					// (EndDateTime, Duration, Passed) - an entry that hasn't passed is active
					bool passed = penalty is ITuple tuple && tuple.Length >= 3 && tuple[2] is true;
					if (penalty != null && !passed)
						return true;
				}
			}
		}
		catch (Exception ex)
		{
			if (!_loggedFailure)
			{
				_loggedFailure = true;
				_logger?.LogWarning(ex, "[Chat] Reading a SimpleAdmin gag failed - gags aren't checked until the next reload");
			}
			_api = null;
		}
		return false;
	}
}
