using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace SurfTimer;

public class Injection : IPluginServiceCollection<SurfTimer>
{
	private static readonly string LogDirectory =
		$"{Server.GameDirectory}/csgo/addons/counterstrikesharp/logs";

#if DEBUG
	private const LogEventLevel ConsoleLevel = LogEventLevel.Verbose;
	private const LogEventLevel FileLevel = LogEventLevel.Verbose;
#else
	private const LogEventLevel ConsoleLevel = LogEventLevel.Warning;
	private const LogEventLevel FileLevel = LogEventLevel.Information;
#endif

	/// <summary>Players' chat lines (ChatProcessor) - the game doesn't print them anymore, so they stay on the console</summary>
	private static bool IsChat(LogEvent e) => e.MessageTemplate.Text.StartsWith("[Chat] {Prefix}", StringComparison.Ordinal);

	public void ConfigureServices(IServiceCollection serviceCollection)
	{
		var fileName = $"log-SurfTimer-.txt"; // Date seems to be automatically appended so we leave it out
		var filePath = Path.Combine(LogDirectory, fileName);

		// Configure Serilog - the console only gets warnings and errors (and the chat log) in release builds,
		// the log file everything from Information; debug builds get everything in both
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Logger(console => console
				.Filter.ByIncludingOnly(e => e.Level >= ConsoleLevel || IsChat(e))
				.WriteTo.Console())
			.WriteTo.File(
				path: filePath,
				restrictedToMinimumLevel: FileLevel,
				rollingInterval: RollingInterval.Day
			)
			.CreateLogger();

		// Show the full path to the log file
#if DEBUG
		Console.WriteLine($"[SurfTimer] Logging to file: {filePath}");
#endif
		Log.Information("[SurfTimer] Logging to file: {LogFile}", filePath);

		// Register Serilog as a logging provider for Microsoft.Extensions.Logging
		serviceCollection.AddLogging(builder =>
		{
			builder.ClearProviders();
			builder.AddSerilog(dispose: true);
		});

		// Register Dependencies
		serviceCollection.AddScoped<ReplayRecorder>(); // Multiple instances for different players
		serviceCollection.AddScoped<CurrentRun>(); // Multiple instances for different players
		serviceCollection.AddScoped<ReplayPlayer>(); // Multiple instances for different players
		serviceCollection.AddScoped<PersonalBest>(); // Multiple instances for different players
		serviceCollection.AddScoped<PlayerStats>(); // Multiple instances for different players
		serviceCollection.AddScoped<PlayerProfile>(); // Multiple instances for different players
		serviceCollection.AddSingleton<Map>(); // Single instance for 1 Map object
	}
}

/// <summary>
/// Handles translation files
/// </summary>
public static class LocalizationService
{
	// Localizer as a Singleton
	public static IStringLocalizer? Localizer { get; private set; }
	public static IStringLocalizer LocalizerNonNull => Localizer!;

	public static void Init(IStringLocalizer localizer)
	{
		Localizer = localizer;
	}
}
