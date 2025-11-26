using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer;

/// <summary>
/// Base class for SQL Server data readers.
/// Provides common ADO.NET patterns with proper disposal and error handling.
/// </summary>
/// <remarks>
/// This follows the "Template Method" pattern:
/// - Base class handles connection, command, and reader lifecycle
/// - Derived classes implement specific query logic and mapping
/// 
/// All methods are async-aware and support cancellation.
/// </remarks>
public abstract class SqlDataReaderBase
{
    protected readonly ISqlConnectionFactory ConnectionFactory;
    protected readonly ILogger Logger;

    protected SqlDataReaderBase(
        ISqlConnectionFactory connectionFactory,
        ILogger logger)
    {
        ConnectionFactory = connectionFactory;
        Logger = logger;
    }

    /// <summary>
    /// Executes a query and maps results using the provided mapper function.
    /// </summary>
    protected async Task<List<T>> ExecuteQueryAsync<T>(
        string instanceName,
        string? databaseName,
        string sql,
        Dictionary<string, object>? parameters,
        Func<SqlDataReader, T> mapper,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var connection = databaseName != null
            ? await ConnectionFactory.CreateConnectionForDatabaseAsync(instanceName, databaseName, cancellationToken)
            : await ConnectionFactory.CreateConnectionAsync(instanceName, cancellationToken);

        await using var command = CreateCommand(connection, sql, parameters, commandTimeout);

        var results = new List<T>();

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(mapper(reader));
            }
        }
        catch (SqlException ex) when (ex.Number == 208) // Invalid object name
        {
            Logger.LogWarning(
                "Query failed - object not found. This may indicate Query Store is not enabled. Error: {Message}",
                ex.Message);
        }
        catch (SqlException ex) when (ex.Number == 229) // Permission denied
        {
            Logger.LogError(
                "Query failed - insufficient permissions. Ensure the monitoring user has VIEW DATABASE STATE. Error: {Message}",
                ex.Message);
            throw;
        }

        return results;
    }

    /// <summary>
    /// Executes a query that returns a single scalar value.
    /// </summary>
    protected async Task<T?> ExecuteScalarAsync<T>(
        string instanceName,
        string? databaseName,
        string sql,
        Dictionary<string, object>? parameters,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var connection = databaseName != null
            ? await ConnectionFactory.CreateConnectionForDatabaseAsync(instanceName, databaseName, cancellationToken)
            : await ConnectionFactory.CreateConnectionAsync(instanceName, cancellationToken);

        await using var command = CreateCommand(connection, sql, parameters, commandTimeout);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (result == null || result == DBNull.Value)
        {
            return default;
        }

        return (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>
    /// Executes a non-query command (INSERT, UPDATE, DELETE, stored proc).
    /// </summary>
    protected async Task<int> ExecuteNonQueryAsync(
        string instanceName,
        string? databaseName,
        string sql,
        Dictionary<string, object>? parameters,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var connection = databaseName != null
            ? await ConnectionFactory.CreateConnectionForDatabaseAsync(instanceName, databaseName, cancellationToken)
            : await ConnectionFactory.CreateConnectionAsync(instanceName, cancellationToken);

        await using var command = CreateCommand(connection, sql, parameters, commandTimeout);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a command with parameters and timeout.
    /// </summary>
    private static SqlCommand CreateCommand(
        SqlConnection connection,
        string sql,
        Dictionary<string, object>? parameters,
        int? commandTimeout)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        if (commandTimeout.HasValue)
        {
            command.CommandTimeout = commandTimeout.Value;
        }

        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                // Handle byte arrays specially (for plan handles, sql handles)
                if (value is byte[] bytes)
                {
                    command.Parameters.Add(new SqlParameter(name, System.Data.SqlDbType.VarBinary)
                    {
                        Value = bytes
                    });
                }
                else
                {
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                }
            }
        }

        return command;
    }

    #region Safe Reader Helpers

    /// <summary>
    /// Safely reads a string value (handles DBNull).
    /// </summary>
    protected static string? GetStringOrNull(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// Safely reads an int value (handles DBNull).
    /// </summary>
    protected static int? GetInt32OrNull(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    /// <summary>
    /// Safely reads a long value (handles DBNull).
    /// </summary>
    protected static long? GetInt64OrNull(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    /// <summary>
    /// Safely reads a double value (handles DBNull).
    /// </summary>
    protected static double? GetDoubleOrNull(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    /// <summary>
    /// Safely reads a DateTime value (handles DBNull).
    /// </summary>
    protected static DateTime? GetDateTimeOrNull(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    /// <summary>
    /// Safely reads a bool value (handles DBNull).
    /// </summary>
    protected static bool? GetBooleanOrNull(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    /// <summary>
    /// Safely reads a byte array value (handles DBNull).
    /// Used for plan handles and sql handles.
    /// </summary>
    protected static byte[]? GetBytesOrNull(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
            return null;

        // Get the length first
        var length = (int)reader.GetBytes(ordinal, 0, null, 0, 0);
        var buffer = new byte[length];
        reader.GetBytes(ordinal, 0, buffer, 0, length);
        return buffer;
    }

    /// <summary>
    /// Gets a required string (throws if null).
    /// </summary>
    protected static string GetRequiredString(SqlDataReader reader, string columnName)
    {
        return GetStringOrNull(reader, columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' was unexpectedly null");
    }

    /// <summary>
    /// Gets a required int (throws if null).
    /// </summary>
    protected static int GetRequiredInt32(SqlDataReader reader, string columnName)
    {
        return GetInt32OrNull(reader, columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' was unexpectedly null");
    }

    /// <summary>
    /// Gets a required long (throws if null).
    /// </summary>
    protected static long GetRequiredInt64(SqlDataReader reader, string columnName)
    {
        return GetInt64OrNull(reader, columnName)
            ?? throw new InvalidOperationException($"Column '{columnName}' was unexpectedly null");
    }

    #endregion
}
