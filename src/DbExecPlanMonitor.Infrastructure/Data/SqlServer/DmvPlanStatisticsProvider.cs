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
