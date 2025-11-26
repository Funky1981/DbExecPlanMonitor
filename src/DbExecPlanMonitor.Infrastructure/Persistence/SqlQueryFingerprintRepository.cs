using System.Data;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Infrastructure.Persistence;

/// <summary>
/// SQL Server implementation of the query fingerprint repository.
/// Uses the monitoring schema to store and retrieve query identities.
/// </summary>
public sealed class SqlQueryFingerprintRepository : RepositoryBase, IQueryFingerprintRepository
{
    private readonly ILogger<SqlQueryFingerprintRepository> _logger;

    public SqlQueryFingerprintRepository(
        IOptions<MonitoringStorageOptions> options,
        ILogger<SqlQueryFingerprintRepository> logger)
        : base(options.Value.ConnectionString, logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> GetOrCreateFingerprintAsync(
        byte[] queryHash,
        string queryTextSample,
        string databaseName,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "GetOrCreateFingerprint for hash {HashPrefix}... in database {Database}",
            Convert.ToHexString(queryHash.Take(8).ToArray()),
            databaseName);

        // Use stored procedure for atomic upsert
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        
        command.CommandText = "monitoring.usp_GetOrCreateFingerprint";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = CommandTimeoutSeconds;

        command.Parameters.Add(new SqlParameter("@QueryHash", SqlDbType.VarBinary, 32) 
        { 
            Value = queryHash 
        });
        command.Parameters.Add(new SqlParameter("@QueryTextSample", SqlDbType.NVarChar, -1) 
        { 
            Value = TruncateQueryText(queryTextSample) 
        });
        command.Parameters.Add(new SqlParameter("@DatabaseName", SqlDbType.NVarChar, 128) 
        { 
            Value = databaseName 
        });
        
        var outputParam = new SqlParameter("@FingerprintId", SqlDbType.UniqueIdentifier)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(outputParam);

        await command.ExecuteNonQueryAsync(ct);

        var fingerprintId = (Guid)outputParam.Value;
        
        _logger.LogDebug("Fingerprint ID: {FingerprintId}", fingerprintId);
        return fingerprintId;
    }

    /// <inheritdoc />
    public async Task<QueryFingerprintRecord?> GetByIdAsync(Guid fingerprintId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT Id, QueryHash, QueryTextSample, DatabaseName, FirstSeenUtc, LastSeenUtc
            FROM monitoring.QueryFingerprint
            WHERE Id = @Id";

        return await ExecuteQuerySingleAsync(
            sql,
            MapFingerprint,
            p => AddGuidParameter(p, "@Id", fingerprintId),
            ct);
    }

    /// <inheritdoc />
    public async Task<QueryFingerprintRecord?> GetByHashAsync(byte[] queryHash, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT Id, QueryHash, QueryTextSample, DatabaseName, FirstSeenUtc, LastSeenUtc
            FROM monitoring.QueryFingerprint
            WHERE QueryHash = @QueryHash";

        return await ExecuteQuerySingleAsync(
            sql,
            MapFingerprint,
            p => AddBinaryParameter(p, "@QueryHash", queryHash, 32),
            ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueryFingerprintRecord>> GetByDatabaseAsync(
        string databaseName,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT Id, QueryHash, QueryTextSample, DatabaseName, FirstSeenUtc, LastSeenUtc
            FROM monitoring.QueryFingerprint
            WHERE DatabaseName = @DatabaseName
            ORDER BY LastSeenUtc DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapFingerprint,
            p => AddStringParameter(p, "@DatabaseName", databaseName, 128),
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueryFingerprintRecord>> GetActiveInWindowAsync(
        TimeWindow window,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT Id, QueryHash, QueryTextSample, DatabaseName, FirstSeenUtc, LastSeenUtc
            FROM monitoring.QueryFingerprint
            WHERE LastSeenUtc BETWEEN @StartUtc AND @EndUtc
            ORDER BY LastSeenUtc DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapFingerprint,
            p =>
            {
                AddDateTimeParameter(p, "@StartUtc", window.StartUtc);
                AddDateTimeParameter(p, "@EndUtc", window.EndUtc);
            },
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task UpdateLastSeenAsync(Guid fingerprintId, DateTime lastSeenUtc, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE monitoring.QueryFingerprint
            SET LastSeenUtc = @LastSeenUtc
            WHERE Id = @Id";

        await ExecuteNonQueryAsync(
            sql,
            p =>
            {
                AddGuidParameter(p, "@Id", fingerprintId);
                AddDateTimeParameter(p, "@LastSeenUtc", lastSeenUtc);
            },
            ct);
    }

    /// <inheritdoc />
    public async Task<(Guid Id, bool IsNew)> UpsertAsync(
        string instanceName,
        string databaseName,
        byte[] queryHash,
        string sampleText,
        string normalizedText,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Upserting fingerprint for hash {HashPrefix}... in {Instance}/{Database}",
            Convert.ToHexString(queryHash.Take(8).ToArray()),
            instanceName,
            databaseName);

        // Use stored procedure for atomic upsert with IsNew indicator
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = "monitoring.usp_UpsertQueryFingerprint";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = CommandTimeoutSeconds;

        command.Parameters.Add(new SqlParameter("@InstanceName", SqlDbType.NVarChar, 128)
        {
            Value = instanceName
        });
        command.Parameters.Add(new SqlParameter("@DatabaseName", SqlDbType.NVarChar, 128)
        {
            Value = databaseName
        });
        command.Parameters.Add(new SqlParameter("@QueryHash", SqlDbType.VarBinary, 32)
        {
            Value = queryHash
        });
        command.Parameters.Add(new SqlParameter("@SampleText", SqlDbType.NVarChar, -1)
        {
            Value = TruncateQueryText(sampleText)
        });
        command.Parameters.Add(new SqlParameter("@NormalizedText", SqlDbType.NVarChar, -1)
        {
            Value = TruncateQueryText(normalizedText)
        });

        var idParam = new SqlParameter("@FingerprintId", SqlDbType.UniqueIdentifier)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(idParam);

        var isNewParam = new SqlParameter("@IsNew", SqlDbType.Bit)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(isNewParam);

        await command.ExecuteNonQueryAsync(ct);

        var fingerprintId = (Guid)idParam.Value;
        var isNew = (bool)isNewParam.Value;

        _logger.LogDebug(
            "Fingerprint ID: {FingerprintId}, IsNew: {IsNew}",
            fingerprintId,
            isNew);

        return (fingerprintId, isNew);
    }

    /// <summary>
    /// Maps a SqlDataReader row to a QueryFingerprintRecord.
    /// </summary>
    private static QueryFingerprintRecord MapFingerprint(SqlDataReader reader)
    {
        return new QueryFingerprintRecord
        {
            Id = reader.GetGuid(0),
            QueryHash = GetBytes(reader, 1),
            QueryTextSample = reader.GetString(2),
            DatabaseName = reader.GetString(3),
            FirstSeenUtc = reader.GetDateTime(4),
            LastSeenUtc = reader.GetDateTime(5)
        };
    }

    /// <summary>
    /// Safely reads binary data from a SqlDataReader.
    /// </summary>
    private static byte[] GetBytes(SqlDataReader reader, int ordinal)
    {
        var length = (int)reader.GetBytes(ordinal, 0, null, 0, 0);
        var buffer = new byte[length];
        reader.GetBytes(ordinal, 0, buffer, 0, length);
        return buffer;
    }

    /// <summary>
    /// Truncates query text to a reasonable sample length.
    /// We don't need to store the entire query, just enough for identification.
    /// </summary>
    private static string TruncateQueryText(string queryText, int maxLength = 4000)
    {
        if (string.IsNullOrEmpty(queryText))
            return string.Empty;

        if (queryText.Length <= maxLength)
            return queryText;

        return queryText[..maxLength] + "...";
    }
}

/// <summary>
/// Configuration options for the monitoring storage connection.
/// </summary>
public sealed class MonitoringStorageOptions
{
    public const string SectionName = "MonitoringStorage";

    /// <summary>
    /// Connection string to the database where monitoring data is stored.
    /// This can be the same as the monitored database or a separate central database.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Command timeout in seconds for storage operations.
    /// </summary>
    public int CommandTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Number of days to retain metric samples.
    /// </summary>
    public int RetentionDays { get; init; } = 90;
}
