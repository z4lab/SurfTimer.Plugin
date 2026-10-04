using System.Reflection;

namespace SurfTimer;

/// <summary>
/// What this plugin build is: the git commit (from the informational version, "1.0.0+&lt;sha&gt;"), whether it had
/// uncommitted changes, and when it was built (UTC) - see AddBuildInfo in the .csproj.
/// </summary>
internal static class BuildInfo
{
	private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;

	/// <summary>Short commit hash, "*" appended for uncommitted changes - null without git</summary>
	internal static string? Commit { get; } = ReadCommit();

	/// <summary>"yyyy-MM-dd HH:mm" (UTC) - null when unknown</summary>
	internal static string? BuildDate { get; } = Metadata("BuildDate");

	private static string? ReadCommit()
	{
		string? version = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		int plus = version?.IndexOf('+') ?? -1;
		if (version == null || plus < 0 || plus == version.Length - 1)
			return null;

		string sha = version[(plus + 1)..];
		sha = sha.Length > 7 ? sha[..7] : sha;
		return Metadata("GitDirty") == "true" ? sha + "*" : sha;
	}

	private static string? Metadata(string key) =>
		Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
}
