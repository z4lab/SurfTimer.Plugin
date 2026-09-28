using System.Collections.Concurrent;
using System.Data.Common;
using Dapper;
using MySqlConnector;

namespace SurfTimer;

/// <summary>
/// The plugin's database access: one pooled MySqlDataSource (MariaDB / MySQL) plus thin Dapper helpers.
/// SQL uses {p} for the table prefix from database.json - Sql() substitutes it (cached per statement).
/// Every call opens a pooled connection and returns it right away; only InTransactionAsync groups
/// statements into one transaction.
/// </summary>
internal sealed class Database : IAsyncDisposable
{
	private readonly MySqlDataSource _dataSource;
	private readonly ConcurrentDictionary<string, string> _sqlCache = new();

	internal string TablePrefix { get; }
	internal string DatabaseName { get; }
	internal uint MaxPoolSize { get; }

	static Database()
	{
		// snake_case columns map to PascalCase properties
		DefaultTypeMap.MatchNamesWithUnderscores = true;
	}

	internal Database(DatabaseSettings settings)
	{
		var builder = new MySqlConnectionStringBuilder
		{
			Server = settings.Host,
			Port = settings.Port,
			Database = settings.Database,
			UserID = settings.User,
			Password = settings.Password,
			ConnectionTimeout = settings.ConnectTimeoutSeconds,
			Pooling = true,
			MaximumPoolSize = settings.MaxPoolSize,
			// Stored dates are UTC - read them back as such
			DateTimeKind = MySqlDateTimeKind.Utc,
			CharacterSet = "utf8mb4",
		};

		_dataSource = new MySqlDataSourceBuilder(builder.ConnectionString).Build();
		TablePrefix = settings.TablePrefix;
		DatabaseName = settings.Database;
		MaxPoolSize = settings.MaxPoolSize;
	}

	/// <summary>
	/// Replaces {p} with the table prefix.
	/// </summary>
	internal string Sql(string sql) => _sqlCache.GetOrAdd(sql, s => s.Replace("{p}", TablePrefix));

	internal async Task<DbConnection> OpenAsync() => await _dataSource.OpenConnectionAsync();

	internal async Task<List<T>> QueryAsync<T>(string sql, object? args = null)
	{
		await using var connection = await OpenAsync();
		return (await connection.QueryAsync<T>(Sql(sql), args)).AsList();
	}

	internal async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? args = null)
	{
		await using var connection = await OpenAsync();
		return await connection.QueryFirstOrDefaultAsync<T>(Sql(sql), args);
	}

	internal async Task<T?> ExecuteScalarAsync<T>(string sql, object? args = null)
	{
		await using var connection = await OpenAsync();
		return await connection.ExecuteScalarAsync<T>(Sql(sql), args);
	}

	internal async Task<int> ExecuteAsync(string sql, object? args = null)
	{
		await using var connection = await OpenAsync();
		return await connection.ExecuteAsync(Sql(sql), args);
	}

	/// <summary>
	/// Runs the work in one transaction - committed when it returns, rolled back when it throws.
	/// Use the given Tx helpers so every statement joins the transaction. A deadlock (e.g. two players
	/// saving on the same course at once) is retried, so the work must be safe to run again.
	/// </summary>
	internal async Task<T> InTransactionAsync<T>(Func<Tx, Task<T>> work)
	{
		const int attempts = 3;
		for (int attempt = 1; ; attempt++)
		{
			await using var connection = await OpenAsync();
			await using var transaction = await connection.BeginTransactionAsync();
			try
			{
				T result = await work(new Tx(this, connection, transaction));
				await transaction.CommitAsync();
				return result;
			}
			catch (MySqlException ex) when (attempt < attempts
				&& ex.ErrorCode is MySqlErrorCode.LockDeadlock or MySqlErrorCode.LockWaitTimeout)
			{
				await transaction.RollbackAsync();
				await Task.Delay(50 * attempt);
			}
			catch
			{
				await transaction.RollbackAsync();
				throw;
			}
		}
	}

	internal Task InTransactionAsync(Func<Tx, Task> work) =>
		InTransactionAsync(async tx => { await work(tx); return true; });

	public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

	/// <summary>
	/// Statements inside one transaction.
	/// </summary>
	internal sealed class Tx(Database db, DbConnection connection, DbTransaction transaction)
	{
		internal Task<IEnumerable<T>> QueryAsync<T>(string sql, object? args = null) =>
			connection.QueryAsync<T>(db.Sql(sql), args, transaction);

		internal Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? args = null) =>
			connection.QueryFirstOrDefaultAsync<T>(db.Sql(sql), args, transaction);

		internal Task<T?> ExecuteScalarAsync<T>(string sql, object? args = null) =>
			connection.ExecuteScalarAsync<T>(db.Sql(sql), args, transaction);

		internal Task<int> ExecuteAsync(string sql, object? args = null) =>
			connection.ExecuteAsync(db.Sql(sql), args, transaction);
	}
}

/// <summary>
/// cfg/SurfTimer/database.json
/// </summary>
internal sealed record DatabaseSettings(
	string Host, uint Port, string Database, string User, string Password,
	uint ConnectTimeoutSeconds, string TablePrefix, uint MaxPoolSize);
