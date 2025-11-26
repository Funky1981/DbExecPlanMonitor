using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DbExecPlanMonitor.Infrastructure.Persistence;

/// <summary>
/// Base class for ADO.NET repositories providing common patterns and helper methods.
/// </summary>
public abstract class RepositoryBase
{
    private readonly string _connectionString;
    protected readonly ILogger Logger;
    
    /// <summary>
    /// Command timeout in seconds for SQL commands.
    /// </summary>
    protected int CommandTimeoutSeconds { get; } = 60;

    protected RepositoryBase(string connectionString, ILogger logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates and opens a new SQL connection.
    /// </summary>
    protected async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    /// <summary>
    /// Executes a non-query command (INSERT, UPDATE, DELETE) and returns affected row count.
    /// </summary>
    protected async Task<int> ExecuteNonQueryAsync(
        string sql,
        Action<SqlParameterCollection>? configureParameters = null,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = CreateCommand(connection, sql, configureParameters);
        
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Executes a scalar query and returns a single value.
    /// </summary>
    protected async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        Action<SqlParameterCollection>? configureParameters = null,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = CreateCommand(connection, sql, configureParameters);
        
        var result = await command.ExecuteScalarAsync(ct);
        
        if (result is null or DBNull)
            return default;
            
        return (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>
    /// Executes a query and maps results to a list of objects.
    /// </summary>
    protected async Task<List<T>> ExecuteQueryAsync<T>(
        string sql,
        Func<SqlDataReader, T> mapper,
        Action<SqlParameterCollection>? configureParameters = null,
        CancellationToken ct = default)
    {
        var results = new List<T>();
        
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = CreateCommand(connection, sql, configureParameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            results.Add(mapper(reader));
        }
        
        return results;
    }

    /// <summary>
    /// Executes a query and returns the first result, or null if no results.
    /// </summary>
    protected async Task<T?> ExecuteQuerySingleAsync<T>(
        string sql,
        Func<SqlDataReader, T> mapper,
        Action<SqlParameterCollection>? configureParameters = null,
        CancellationToken ct = default) where T : class
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = CreateCommand(connection, sql, configureParameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        
        if (await reader.ReadAsync(ct))
        {
            return mapper(reader);
        }
        
        return null;
    }

    /// <summary>
    /// Executes multiple commands within a transaction.
    /// </summary>
    protected async Task ExecuteInTransactionAsync(
        Func<SqlConnection, SqlTransaction, Task> action,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        
        try
        {
            await action(connection, transaction);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Executes a batch insert for multiple records using a transaction.
    /// </summary>
    protected async Task ExecuteBatchInsertAsync<T>(
        IEnumerable<T> items,
        string insertSql,
        Action<SqlParameterCollection, T> configureParameters,
        CancellationToken ct = default)
    {
        await ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            foreach (var item in items)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = insertSql;
                command.CommandTimeout = CommandTimeoutSeconds;
                
                configureParameters(command.Parameters, item);
                
                await command.ExecuteNonQueryAsync(ct);
            }
        }, ct);
    }

    /// <summary>
    /// Creates a command with the given SQL and optional parameters.
    /// </summary>
    private SqlCommand CreateCommand(
        SqlConnection connection,
        string sql,
        Action<SqlParameterCollection>? configureParameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = CommandTimeoutSeconds;
        
        configureParameters?.Invoke(command.Parameters);
        
        return command;
    }

    /// <summary>
    /// Adds common parameter types with null handling.
    /// </summary>
    protected static void AddParameter(
        SqlParameterCollection parameters,
        string name,
        object? value,
        SqlDbType dbType)
    {
        var parameter = new SqlParameter(name, dbType)
        {
            Value = value ?? DBNull.Value
        };
        parameters.Add(parameter);
    }

    /// <summary>
    /// Adds a GUID parameter.
    /// </summary>
    protected static void AddGuidParameter(
        SqlParameterCollection parameters,
        string name,
        Guid value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.UniqueIdentifier) { Value = value });
    }

    /// <summary>
    /// Adds a nullable GUID parameter.
    /// </summary>
    protected static void AddGuidParameter(
        SqlParameterCollection parameters,
        string name,
        Guid? value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.UniqueIdentifier) 
        { 
            Value = value.HasValue ? value.Value : DBNull.Value 
        });
    }

    /// <summary>
    /// Adds a string parameter with specified max length.
    /// </summary>
    protected static void AddStringParameter(
        SqlParameterCollection parameters,
        string name,
        string? value,
        int maxLength = -1)
    {
        var parameter = new SqlParameter(name, SqlDbType.NVarChar, maxLength)
        {
            Value = value ?? (object)DBNull.Value
        };
        parameters.Add(parameter);
    }

    /// <summary>
    /// Adds a DateTime parameter.
    /// </summary>
    protected static void AddDateTimeParameter(
        SqlParameterCollection parameters,
        string name,
        DateTime value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.DateTime2) { Value = value });
    }

    /// <summary>
    /// Adds a nullable DateTime parameter.
    /// </summary>
    protected static void AddDateTimeParameter(
        SqlParameterCollection parameters,
        string name,
        DateTime? value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.DateTime2) 
        { 
            Value = value.HasValue ? value.Value : DBNull.Value 
        });
    }

    /// <summary>
    /// Adds a bigint parameter.
    /// </summary>
    protected static void AddBigIntParameter(
        SqlParameterCollection parameters,
        string name,
        long value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.BigInt) { Value = value });
    }

    /// <summary>
    /// Adds a nullable bigint parameter.
    /// </summary>
    protected static void AddBigIntParameter(
        SqlParameterCollection parameters,
        string name,
        long? value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.BigInt) 
        { 
            Value = value.HasValue ? value.Value : DBNull.Value 
        });
    }

    /// <summary>
    /// Adds a varbinary parameter (for hashes, binary data).
    /// </summary>
    protected static void AddBinaryParameter(
        SqlParameterCollection parameters,
        string name,
        byte[]? value,
        int maxLength = -1)
    {
        var parameter = new SqlParameter(name, SqlDbType.VarBinary, maxLength)
        {
            Value = value ?? (object)DBNull.Value
        };
        parameters.Add(parameter);
    }

    /// <summary>
    /// Adds an int parameter.
    /// </summary>
    protected static void AddIntParameter(
        SqlParameterCollection parameters,
        string name,
        int value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.Int) { Value = value });
    }

    /// <summary>
    /// Adds a bit (boolean) parameter.
    /// </summary>
    protected static void AddBoolParameter(
        SqlParameterCollection parameters,
        string name,
        bool value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.Bit) { Value = value });
    }

    /// <summary>
    /// Adds a float/double parameter.
    /// </summary>
    protected static void AddFloatParameter(
        SqlParameterCollection parameters,
        string name,
        double value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.Float) { Value = value });
    }

    /// <summary>
    /// Adds a nullable float/double parameter.
    /// </summary>
    protected static void AddFloatParameter(
        SqlParameterCollection parameters,
        string name,
        double? value)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.Float) 
        { 
            Value = value.HasValue ? value.Value : DBNull.Value 
        });
    }
}
