using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.ValueObjects;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer.Models;

namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer;

/// <summary>
/// Retrieves execution plan statistics from SQL Server using DMVs and Query Store.
/// </summary>
/// <remarks>
/// This provider implements a fallback strategy:
/// 1. Try Query Store first (if enabled and configured)
/// 2. Fall back to DMVs if Query Store unavailable
/// 
/// Query Store advantages:
/// - Data persists across restarts
/// - Historical tracking
/// - Plan forcing support
/// 
/// DMV advantages:
/// - Always available (no setup required)
/// - Real-time cache data
/// - Works on all editions
/// </remarks>
public class DmvPlanStatisticsProvider : SqlDataReaderBase, IPlanStatisticsProvider
{
    private readonly ILogger<DmvPlanStatisticsProvider> _logger;

    // Maps databaseId (Guid) to instance/database config
    // In a real system, this would be populated from a repository
    private readonly Dictionary<Guid, (string instanceName, string databaseName)> _databaseRegistry = new();

    public DmvPlanStatisticsProvider(
        ISqlConnectionFactory connectionFactory,
        ILogger<DmvPlanStatisticsProvider> logger)
        : base(connectionFactory, logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a database for monitoring.
    /// </summary>
    public void RegisterDatabase(Guid databaseId, string instanceName, string databaseName)
    {
        _databaseRegistry[databaseId] = (instanceName, databaseName);
    }

    /// <summary>
    /// Gets top N queries by the specified metric.
    /// </summary>
    public async Task<IReadOnlyList<QueryStatisticsResult>> GetTopQueriesAsync(
        Guid databaseId,
        int topN,
        TimeWindow window,
        QueryOrderBy orderBy = QueryOrderBy.TotalCpuTime,
        CancellationToken cancellationToken = default)
    {
        if (!_databaseRegistry.TryGetValue(databaseId, out var dbInfo))
        {
            throw new ArgumentException($"Database {databaseId} is not registered", nameof(databaseId));
        }

        // First check if Query Store is available
        var config = ConnectionFactory.GetInstanceConfig(dbInfo.instanceName);
        if (config?.PreferQueryStore == true)
        {
            var queryStoreAvailable = await IsQueryStoreEnabledAsync(databaseId, cancellationToken);

            if (queryStoreAvailable)
            {
                return await GetTopQueriesFromQueryStoreAsync(
                    dbInfo.instanceName, dbInfo.databaseName, topN, orderBy, cancellationToken);
            }
        }

        // Fall back to DMVs
        return await GetTopQueriesFromDmvAsync(
            dbInfo.instanceName, dbInfo.databaseName, topN, orderBy, cancellationToken);
    }

    /// <summary>
    /// Gets current statistics for a specific query hash.
    /// </summary>
    public async Task<QueryStatisticsResult?> GetQueryStatisticsAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default)
    {
        if (!_databaseRegistry.TryGetValue(databaseId, out var dbInfo))
        {
            throw new ArgumentException($"Database {databaseId} is not registered", nameof(databaseId));
        }

        // Parse the hash from hex string
        if (!TryParseQueryHash(queryHash, out var hashBytes))
        {
            _logger.LogWarning("Invalid query hash format: {QueryHash}", queryHash);
            return null;
        }

        var sql = DmvQueries.GetStatsByQueryHash;
        var parameters = new Dictionary<string, object>
        {
            { "@QueryHash", hashBytes }
        };

        var results = await ExecuteQueryAsync(
            dbInfo.instanceName,
            dbInfo.databaseName,
            sql,
            parameters,
            MapDmvRecordToQueryResult,
            commandTimeout: 60,
            cancellationToken);

        return results.FirstOrDefault();
    }

    /// <summary>
    /// Gets statistics for all plans of a specific query.
    /// </summary>
    public async Task<IReadOnlyList<PlanStatisticsResult>> GetPlanStatisticsAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default)
    {
        if (!_databaseRegistry.TryGetValue(databaseId, out var dbInfo))
        {
            throw new ArgumentException($"Database {databaseId} is not registered", nameof(databaseId));
        }

        if (!TryParseQueryHash(queryHash, out var hashBytes))
        {
            _logger.LogWarning("Invalid query hash format: {QueryHash}", queryHash);
            return Array.Empty<PlanStatisticsResult>();
        }

        var sql = DmvQueries.GetStatsByQueryHash;
        var parameters = new Dictionary<string, object>
        {
            { "@QueryHash", hashBytes }
        };

        return await ExecuteQueryAsync(
            dbInfo.instanceName,
            dbInfo.databaseName,
            sql,
            parameters,
            MapDmvRecordToPlanResult,
            commandTimeout: 60,
            cancellationToken);
    }

    /// <summary>
    /// Checks if Query Store is enabled for a database.
    /// </summary>
    public async Task<bool> IsQueryStoreEnabledAsync(
        Guid databaseId,
        CancellationToken cancellationToken = default)
    {
        if (!_databaseRegistry.TryGetValue(databaseId, out var dbInfo))
        {
            return false;
        }

        return await IsQueryStoreEnabledInternalAsync(
            dbInfo.instanceName, dbInfo.databaseName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectedQueryStatistics>> GetTopQueriesByElapsedTimeAsync(
        string connectionString,
        string databaseName,
        int topN,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Fetching top {TopN} queries by elapsed time for {Database}",
            topN,
            databaseName);

        // Connect directly using the provided connection string
        await using var connection = new SqlConnection(connectionString);
        
        // Append the database name to the connection if not already in the connection string
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName
        };
        connection.ConnectionString = builder.ConnectionString;

        await connection.OpenAsync(cancellationToken);

        // Check if Query Store is available
        var useQueryStore = await IsQueryStoreEnabledDirectAsync(connection, cancellationToken);

        if (useQueryStore)
        {
            return await GetTopQueriesFromQueryStoreDirectAsync(
                connection,
                topN,
                window,
                cancellationToken);
        }

        return await GetTopQueriesFromDmvDirectAsync(
            connection,
            topN,
            window,
            cancellationToken);
    }

    /// <summary>
    /// Checks Query Store status using an open connection.
    /// </summary>
    private async Task<bool> IsQueryStoreEnabledDirectAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT CASE WHEN actual_state IN (1, 2) THEN 1 ELSE 0 END 
                FROM sys.database_query_store_options";
            command.CommandTimeout = 10;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result != null && (int)result == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets top queries from DMVs using an open connection.
    /// Applies lookback filtering using last_execution_time.
    /// </summary>
    private async Task<IReadOnlyList<CollectedQueryStatistics>> GetTopQueriesFromDmvDirectAsync(
        SqlConnection connection,
        int topN,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT TOP (@TopN)
                qs.query_hash,
                SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1,
                    CASE WHEN qs.statement_end_offset = -1 THEN LEN(CONVERT(nvarchar(max), st.text)) * 2
                         ELSE qs.statement_end_offset - qs.statement_start_offset END / 2 + 1) AS query_text,
                qs.execution_count,
                qs.total_elapsed_time / 1000.0 AS total_elapsed_time_ms,
                (qs.total_elapsed_time / qs.execution_count) / 1000.0 AS avg_elapsed_time_ms,
                qs.total_worker_time / 1000.0 AS total_cpu_time_ms,
                (qs.total_worker_time / qs.execution_count) / 1000.0 AS avg_cpu_time_ms,
                qs.total_logical_reads,
                qs.total_logical_reads / qs.execution_count AS avg_logical_reads,
                qs.total_physical_reads,
                qs.total_physical_reads / qs.execution_count AS avg_physical_reads,
                qs.total_logical_writes,
                qs.total_logical_writes / qs.execution_count AS avg_logical_writes,
                qs.plan_handle,
                qs.last_execution_time
            FROM sys.dm_exec_query_stats qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
            WHERE qs.execution_count > 0
              AND st.text IS NOT NULL
              AND qs.last_execution_time >= @LookbackStart
            ORDER BY qs.total_elapsed_time DESC";
        
        command.CommandTimeout = 120;
        command.Parameters.AddWithValue("@TopN", topN);
        command.Parameters.AddWithValue("@LookbackStart", window.StartUtc);

        var results = new List<CollectedQueryStatistics>();
        
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapToCollectedStatistics(reader));
        }

        return results;
    }

    /// <summary>
    /// Gets top queries from Query Store using an open connection.
    /// </summary>
    private async Task<IReadOnlyList<CollectedQueryStatistics>> GetTopQueriesFromQueryStoreDirectAsync(
        SqlConnection connection,
        int topN,
        TimeWindow window,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT TOP (@TopN)
                q.query_hash,
                qt.query_sql_text,
                SUM(rs.count_executions) AS execution_count,
                SUM(rs.avg_duration * rs.count_executions) / 1000.0 AS total_elapsed_time_ms,
                AVG(rs.avg_duration) / 1000.0 AS avg_elapsed_time_ms,
                SUM(rs.avg_cpu_time * rs.count_executions) / 1000.0 AS total_cpu_time_ms,
                AVG(rs.avg_cpu_time) / 1000.0 AS avg_cpu_time_ms,
                SUM(rs.avg_logical_io_reads * rs.count_executions) AS total_logical_reads,
                AVG(rs.avg_logical_io_reads) AS avg_logical_reads,
                SUM(rs.avg_physical_io_reads * rs.count_executions) AS total_physical_reads,
                AVG(rs.avg_physical_io_reads) AS avg_physical_reads,
                SUM(rs.avg_logical_io_writes * rs.count_executions) AS total_logical_writes,
                AVG(rs.avg_logical_io_writes) AS avg_logical_writes,
                MAX(rs.last_execution_time) AS last_execution_time
            FROM sys.query_store_query q
            JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
            JOIN sys.query_store_plan p ON q.query_id = p.query_id
            JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
            JOIN sys.query_store_runtime_stats_interval rsi ON rs.runtime_stats_interval_id = rsi.runtime_stats_interval_id
            WHERE rsi.start_time >= @StartTime
              AND rsi.end_time <= @EndTime
            GROUP BY q.query_hash, qt.query_sql_text
            ORDER BY total_elapsed_time_ms DESC";

        command.CommandTimeout = 120;
        command.Parameters.AddWithValue("@TopN", topN);
        command.Parameters.AddWithValue("@StartTime", window.StartUtc);
        command.Parameters.AddWithValue("@EndTime", window.EndUtc);

        var results = new List<CollectedQueryStatistics>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapQueryStoreToCollectedStatistics(reader));
        }

        return results;
    }

    /// <summary>
    /// Maps a DMV query result to CollectedQueryStatistics.
    /// </summary>
    private static CollectedQueryStatistics MapToCollectedStatistics(SqlDataReader reader)
    {
        var queryHashBytes = new byte[8];
        reader.GetBytes(0, 0, queryHashBytes, 0, 8);

        return new CollectedQueryStatistics
        {
            QueryHash = queryHashBytes,
            SqlText = reader.GetString(1),
            ExecutionCount = reader.GetInt64(2),
            TotalElapsedTimeMs = reader.GetDouble(3),
            AvgElapsedTimeMs = reader.GetDouble(4),
            TotalCpuTimeMs = reader.GetDouble(5),
            AvgCpuTimeMs = reader.GetDouble(6),
            TotalLogicalReads = reader.GetInt64(7),
            AvgLogicalReads = reader.GetDouble(8),
            TotalPhysicalReads = reader.GetInt64(9),
            AvgPhysicalReads = reader.GetDouble(10),
            TotalLogicalWrites = reader.GetInt64(11),
            AvgLogicalWrites = reader.GetDouble(12),
            PlanHandle = reader.IsDBNull(13) ? null : GetPlanHandle(reader, 13),
            LastExecutionTimeUtc = reader.IsDBNull(14) ? null : reader.GetDateTime(14)
        };
    }

    /// <summary>
    /// Maps a Query Store result to CollectedQueryStatistics.
    /// </summary>
    private static CollectedQueryStatistics MapQueryStoreToCollectedStatistics(SqlDataReader reader)
    {
        var queryHashBytes = new byte[8];
        reader.GetBytes(0, 0, queryHashBytes, 0, 8);

        return new CollectedQueryStatistics
        {
            QueryHash = queryHashBytes,
            SqlText = reader.GetString(1),
            ExecutionCount = (long)reader.GetDouble(2), // SUM returns float in some cases
            TotalElapsedTimeMs = reader.GetDouble(3),
            AvgElapsedTimeMs = reader.GetDouble(4),
            TotalCpuTimeMs = reader.GetDouble(5),
            AvgCpuTimeMs = reader.GetDouble(6),
            TotalLogicalReads = (long)reader.GetDouble(7),
            AvgLogicalReads = reader.GetDouble(8),
            TotalPhysicalReads = (long)reader.GetDouble(9),
            AvgPhysicalReads = reader.GetDouble(10),
            TotalLogicalWrites = (long)reader.GetDouble(11),
            AvgLogicalWrites = reader.GetDouble(12),
            LastExecutionTimeUtc = reader.IsDBNull(13) ? null : reader.GetDateTime(13)
        };
    }

    /// <summary>
    /// Safely reads plan_handle binary data.
    /// </summary>
    private static byte[] GetPlanHandle(SqlDataReader reader, int ordinal)
    {
        var length = (int)reader.GetBytes(ordinal, 0, null!, 0, 0);
        var buffer = new byte[length];
        reader.GetBytes(ordinal, 0, buffer, 0, length);
        return buffer;
    }
    /// <summary>
    /// Checks if Query Store is enabled (internal implementation).
    /// </summary>
    private async Task<bool> IsQueryStoreEnabledInternalAsync(
        string instanceName,
        string databaseName,
        CancellationToken cancellationToken)
    {
        try
        {
            var sql = @"
                SELECT CASE WHEN actual_state = 1 OR actual_state = 2 THEN 1 ELSE 0 END 
                FROM sys.database_query_store_options";

            var result = await ExecuteScalarAsync<int>(
                instanceName,
                databaseName,
                sql,
                null,
                commandTimeout: 10,
                cancellationToken);

            return result == 1;
        }
        catch (SqlException ex) when (ex.Number == 208) // Object not found
        {
            // Query Store views don't exist (SQL Server < 2016)
            _logger.LogDebug(
                "Query Store not available for {Database} on {Instance}",
                databaseName, instanceName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to check Query Store status for {Database} on {Instance}",
                databaseName, instanceName);
            return false;
        }
    }

    /// <summary>
    /// Gets top queries from DMVs.
    /// </summary>
    private async Task<IReadOnlyList<QueryStatisticsResult>> GetTopQueriesFromDmvAsync(
        string instanceName,
        string databaseName,
        int topN,
        QueryOrderBy orderBy,
        CancellationToken cancellationToken)
    {
        var sql = orderBy switch
        {
            QueryOrderBy.TotalCpuTime or QueryOrderBy.AvgCpuTime => DmvQueries.TopQueriesByCpu,
            QueryOrderBy.TotalLogicalReads or QueryOrderBy.AvgLogicalReads => DmvQueries.TopQueriesByLogicalReads,
            QueryOrderBy.TotalDuration or QueryOrderBy.AvgDuration => DmvQueries.TopQueriesByElapsedTime,
            _ => DmvQueries.TopQueriesByCpu
        };

        var parameters = new Dictionary<string, object>
        {
            { "@TopN", topN },
            { "@DatabaseName", databaseName }
        };

        _logger.LogDebug(
            "Fetching top {TopN} queries by {OrderBy} from DMVs for {Database}",
            topN, orderBy, databaseName);

        return await ExecuteQueryAsync(
            instanceName,
            databaseName,
            sql,
            parameters,
            MapDmvRecordToQueryResult,
            commandTimeout: 120,
            cancellationToken);
    }

    /// <summary>
    /// Gets top queries from Query Store.
    /// </summary>
    private async Task<IReadOnlyList<QueryStatisticsResult>> GetTopQueriesFromQueryStoreAsync(
        string instanceName,
        string databaseName,
        int topN,
        QueryOrderBy orderBy,
        CancellationToken cancellationToken)
    {
        var sql = orderBy switch
        {
            QueryOrderBy.TotalCpuTime or QueryOrderBy.AvgCpuTime => QueryStoreQueries.TopQueriesByCpu,
            QueryOrderBy.TotalLogicalReads or QueryOrderBy.AvgLogicalReads => QueryStoreQueries.TopQueriesByLogicalReads,
            _ => QueryStoreQueries.TopQueriesByCpu
        };

        var parameters = new Dictionary<string, object>
        {
            { "@TopN", topN }
        };

        _logger.LogDebug(
            "Fetching top {TopN} queries by {OrderBy} from Query Store for {Database}",
            topN, orderBy, databaseName);

        return await ExecuteQueryAsync(
            instanceName,
            databaseName,
            sql,
            parameters,
            MapQueryStoreRecordToQueryResult,
            commandTimeout: 120,
            cancellationToken);
    }

    /// <summary>
    /// Maps a DMV result row to QueryStatisticsResult.
    /// </summary>
    private QueryStatisticsResult MapDmvRecordToQueryResult(SqlDataReader reader)
    {
        var executionCount = GetRequiredInt64(reader, "ExecutionCount");
        var totalCpuTime = GetRequiredInt64(reader, "TotalCpuTime");
        var totalElapsedTime = GetRequiredInt64(reader, "TotalElapsedTime");
        var totalLogicalReads = GetRequiredInt64(reader, "TotalLogicalReads");
        var totalPhysicalReads = GetRequiredInt64(reader, "TotalPhysicalReads");

        return new QueryStatisticsResult
        {
            QueryHash = $"0x{GetRequiredInt64(reader, "QueryHash"):X16}",
            QueryText = GetStringOrNull(reader, "SqlText") ?? "",
            ObjectName = null, // DMV doesn't reliably provide this
            TotalCpuTimeMs = totalCpuTime / 1000.0,
            TotalDurationMs = totalElapsedTime / 1000.0,
            TotalLogicalReads = totalLogicalReads,
            TotalPhysicalReads = totalPhysicalReads,
            ExecutionCount = executionCount,
            LastExecutionTimeUtc = GetDateTimeOrNull(reader, "LastExecutionTime")
        };
    }

    /// <summary>
    /// Maps a DMV result row to PlanStatisticsResult.
    /// </summary>
    private PlanStatisticsResult MapDmvRecordToPlanResult(SqlDataReader reader)
    {
        var executionCount = GetRequiredInt64(reader, "ExecutionCount");
        var totalCpuTime = GetRequiredInt64(reader, "TotalCpuTime");
        var totalElapsedTime = GetRequiredInt64(reader, "TotalElapsedTime");

        return new PlanStatisticsResult
        {
            QueryHash = $"0x{GetRequiredInt64(reader, "QueryHash"):X16}",
            PlanHash = $"0x{GetRequiredInt64(reader, "QueryPlanHash"):X16}",
            PlanHandle = GetBytesOrNull(reader, "PlanHandle"),
            QueryStorePlanId = null,
            TotalCpuTimeMs = totalCpuTime / 1000.0,
            TotalDurationMs = totalElapsedTime / 1000.0,
            TotalLogicalReads = GetRequiredInt64(reader, "TotalLogicalReads"),
            TotalPhysicalReads = GetRequiredInt64(reader, "TotalPhysicalReads"),
            ExecutionCount = executionCount,
            CreationTimeUtc = GetDateTimeOrNull(reader, "CreationTime"),
            LastExecutionTimeUtc = GetDateTimeOrNull(reader, "LastExecutionTime"),
            MinCpuTimeMs = GetRequiredInt64(reader, "MinCpuTime") / 1000.0,
            MaxCpuTimeMs = GetRequiredInt64(reader, "MaxCpuTime") / 1000.0,
            MinDurationMs = GetRequiredInt64(reader, "MinElapsedTime") / 1000.0,
            MaxDurationMs = GetRequiredInt64(reader, "MaxElapsedTime") / 1000.0
        };
    }

    /// <summary>
    /// Maps a Query Store result row to QueryStatisticsResult.
    /// </summary>
    private QueryStatisticsResult MapQueryStoreRecordToQueryResult(SqlDataReader reader)
    {
        var executionCount = GetRequiredInt64(reader, "ExecutionCount");
        var avgCpuTime = GetDoubleOrNull(reader, "AvgCpuTime") ?? 0;
        var avgDuration = GetDoubleOrNull(reader, "AvgDuration") ?? 0;
        var avgLogicalReads = GetDoubleOrNull(reader, "AvgLogicalReads") ?? 0;
        var avgPhysicalReads = GetDoubleOrNull(reader, "AvgPhysicalReads") ?? 0;

        return new QueryStatisticsResult
        {
            QueryHash = $"0x{GetRequiredInt64(reader, "QueryHash"):X16}",
            QueryText = GetStringOrNull(reader, "QueryText") ?? "",
            ObjectName = GetStringOrNull(reader, "ObjectName"),
            TotalCpuTimeMs = (avgCpuTime * executionCount) / 1000.0,
            TotalDurationMs = (avgDuration * executionCount) / 1000.0,
            TotalLogicalReads = (long)(avgLogicalReads * executionCount),
            TotalPhysicalReads = (long)(avgPhysicalReads * executionCount),
            ExecutionCount = executionCount,
            LastExecutionTimeUtc = GetDateTimeOrNull(reader, "LastExecutionTime")
        };
    }

    /// <summary>
    /// Tries to parse a query hash from hex string format.
    /// </summary>
    private static bool TryParseQueryHash(string queryHash, out long hashValue)
    {
        hashValue = 0;
        if (string.IsNullOrEmpty(queryHash))
            return false;

        // Remove "0x" prefix if present
        var hex = queryHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? queryHash[2..]
            : queryHash;

        return long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out hashValue);
    }
}
