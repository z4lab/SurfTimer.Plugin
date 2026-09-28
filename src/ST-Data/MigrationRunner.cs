using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// Creates and upgrades the schema on plugin load. Migrations are the embedded Migrations/NNNN_name.sql
/// files, applied in order and recorded in {p}schema_migrations with a SHA-256 checksum:
/// - already applied ones are skipped, but must be unchanged (a mismatch stops the plugin - add a new
///   migration instead of editing one);
/// - a named lock keeps several servers on one database from migrating at the same time.
/// MariaDB / MySQL commit DDL implicitly, so a migration isn't atomic - write them idempotent
/// (IF NOT EXISTS / INSERT IGNORE) so a failed one can simply run again.
/// </summary>
internal static class MigrationRunner
{
	private static readonly Regex ResourceName = new(@"^Migrations\.(?<version>\d{4})_(?<name>[A-Za-z0-9_]+)\.sql$");

	private const string CreateMigrationsTable = @"
		CREATE TABLE IF NOT EXISTS `{p}schema_migrations` (
			`version`     INT UNSIGNED NOT NULL,
			`name`        VARCHAR(128) NOT NULL,
			`checksum`    CHAR(64)     NOT NULL,
			`applied_at`  DATETIME(3)  NOT NULL,
			`duration_ms` INT UNSIGNED NOT NULL,
			PRIMARY KEY (`version`)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

	private sealed class AppliedRow
	{
		public int Version { get; set; }
		public string Checksum { get; set; } = "";
	}

	private sealed record Migration(int Version, string Name, string Sql, string Checksum);

	/// <summary>
	/// Applies all pending migrations. Throws when the database can't be brought up to date.
	/// </summary>
	internal static async Task RunAsync(Database db, ILogger logger)
	{
		await using var connection = await db.OpenAsync();

		// Lock names are limited to 64 characters
		string lockName = $"{db.DatabaseName}.{db.TablePrefix}surftimer_migrate";
		if (lockName.Length > 64)
			lockName = lockName[^64..];

		long? locked = await connection.ExecuteScalarAsync<long?>("SELECT GET_LOCK(@Name, 30)", new { Name = lockName });
		if (locked != 1)
			throw new InvalidOperationException("Could not get the migration lock within 30s - another server is migrating the database");

		try
		{
			await connection.ExecuteAsync(db.Sql(CreateMigrationsTable));
			var applied = (await connection.QueryAsync<AppliedRow>(db.Sql("SELECT `version`, `checksum` FROM `{p}schema_migrations`")))
				.ToDictionary(r => r.Version, r => r.Checksum);

			int appliedNow = 0;
			foreach (var migration in LoadMigrations())
			{
				if (applied.TryGetValue(migration.Version, out var checksum))
				{
					if (!string.Equals(checksum, migration.Checksum, StringComparison.OrdinalIgnoreCase))
						throw new InvalidOperationException(
							$"Migration {migration.Version:D4}_{migration.Name} was changed after it was applied (checksum mismatch) - add a new migration instead");
					continue;
				}

				var stopwatch = Stopwatch.StartNew();
				foreach (string statement in SplitStatements(db.Sql(migration.Sql)))
					await connection.ExecuteAsync(statement);
				stopwatch.Stop();

				await connection.ExecuteAsync(db.Sql(@"
					INSERT INTO `{p}schema_migrations` (`version`, `name`, `checksum`, `applied_at`, `duration_ms`)
					VALUES (@Version, @Name, @Checksum, UTC_TIMESTAMP(3), @Duration)"),
					new { migration.Version, migration.Name, migration.Checksum, Duration = (uint)stopwatch.ElapsedMilliseconds });

				logger.LogInformation("[Database] Applied migration {Version:D4}_{Name} in {Elapsed}ms",
					migration.Version, migration.Name, stopwatch.ElapsedMilliseconds);
				appliedNow++;
			}

			logger.LogInformation("[Database] Schema up to date ({Applied} migration(s) applied now, {Total} total)",
				appliedNow, applied.Count + appliedNow);
		}
		finally
		{
			await connection.ExecuteScalarAsync<long?>("SELECT RELEASE_LOCK(@Name)", new { Name = lockName });
		}
	}

	private static List<Migration> LoadMigrations()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var migrations = new List<Migration>();

		foreach (string resource in assembly.GetManifestResourceNames())
		{
			var match = ResourceName.Match(resource);
			if (!match.Success)
				continue;

			using var stream = assembly.GetManifestResourceStream(resource)!;
			using var reader = new StreamReader(stream, Encoding.UTF8);
			// Line endings don't change the checksum (git may check files out either way)
			string sql = reader.ReadToEnd().Replace("\r\n", "\n");
			string checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

			migrations.Add(new Migration(int.Parse(match.Groups["version"].Value), match.Groups["name"].Value, sql, checksum));
		}

		var duplicate = migrations.GroupBy(m => m.Version).FirstOrDefault(g => g.Count() > 1);
		if (duplicate != null)
			throw new InvalidOperationException($"Two migrations share version {duplicate.Key:D4}");

		return migrations.OrderBy(m => m.Version).ToList();
	}

	/// <summary>
	/// Splits a script at `;` outside of quotes, backticks and comments. Comment-only parts are dropped.
	/// </summary>
	internal static IEnumerable<string> SplitStatements(string script)
	{
		var current = new StringBuilder();
		bool hasCode = false;
		char quote = '\0';

		for (int i = 0; i < script.Length; i++)
		{
			char c = script[i];
			char next = i + 1 < script.Length ? script[i + 1] : '\0';

			if (quote != '\0')
			{
				current.Append(c);
				if (c == '\\' && quote != '`' && next != '\0')
				{
					current.Append(next); // Escaped character inside a string
					i++;
				}
				else if (c == quote)
				{
					quote = '\0';
				}
				continue;
			}

			// Comments: "-- " / "#" to the end of the line, /* ... */
			if ((c == '-' && next == '-') || c == '#')
			{
				while (i < script.Length && script[i] != '\n')
					i++;
				current.Append('\n');
				continue;
			}
			if (c == '/' && next == '*')
			{
				int end = script.IndexOf("*/", i + 2, StringComparison.Ordinal);
				i = end < 0 ? script.Length : end + 1;
				current.Append(' ');
				continue;
			}

			if (c == ';')
			{
				if (hasCode)
					yield return current.ToString().Trim();
				current.Clear();
				hasCode = false;
				continue;
			}

			if (c is '\'' or '"' or '`')
				quote = c;
			if (!char.IsWhiteSpace(c))
				hasCode = true;
			current.Append(c);
		}

		if (hasCode)
			yield return current.ToString().Trim();
	}
}
