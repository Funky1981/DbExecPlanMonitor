using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Provides access to query execution statistics from a database.
/// Implemented by infrastructure layer (SQL Server DMVs or Query Store).
/// </summary>
/// <remarks>
/// This is the primary interface for collecting performance metrics.
/// The implementation decides whether to use DMVs or Query Store.
/// </remarks>
public interface IPlanStatisticsProvider
{
    /// <summary>
    /// Gets the top N queries by a specific metric within a time window.
    /// </summary>
    /// <param name="databaseId">The database to query.</param>
    /// <param name="topN">Number of top queries to return.</param>
    /// <param name="window">Time window for the analysis.</param>
    /// <param name="orderBy">Which metric to rank by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of query statistics.</returns>
    Task<IReadOnlyList<QueryStatisticsResult>> GetTopQueriesAsync(
        Guid databaseId,
        int topN,
        TimeWindow window,
        QueryOrderBy orderBy = QueryOrderBy.TotalCpuTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current statistics for a specific query fingerprint.
    /// </summary>
    /// <param name="databaseId">The database to query.</param>
    /// <param name="queryHash">The query hash to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Statistics for the query, or null if not found.</returns>
    Task<QueryStatisticsResult?> GetQueryStatisticsAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics for all plans of a specific query.
    /// A query can have multiple execution plans.
    /// </summary>
    /// <param name="databaseId">The database to query.</param>
    /// <param name="queryHash">The query hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Statistics per plan.</returns>
    Task<IReadOnlyList<PlanStatisticsResult>> GetPlanStatisticsAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Query Store is enabled for a database.
    /// </summary>
    /// <param name="databaseId">The database to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if Query Store is available.</returns>
    Task<bool> IsQueryStoreEnabledAsync(
        Guid databaseId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// How to order query results.
/// </summary>
public enum QueryOrderBy
{
    TotalCpuTime,
    TotalDuration,
    TotalLogicalReads,
    TotalPhysicalReads,
    ExecutionCount,
    AvgCpuTime,
    AvgDuration,
    AvgLogicalReads
}

/// <summary>
/// Query-level statistics result from the provider.
/// </summary>
public class QueryStatisticsResult
{
    /// <summary>
    /// SQL Server's query hash (identifies the query pattern).
    /// </summary>
    public required string QueryHash { get; init; }

    /// <summary>
    /// The normalized SQL text.
    /// </summary>
    public required string QueryText { get; init; }

    /// <summary>
    /// Object name (stored procedure, function) if applicable.
    /// </summary>
    public string? ObjectName { get; init; }

    /// <summary>
    /// Total CPU time in milliseconds.
    /// </summary>
    public double TotalCpuTimeMs { get; init; }

    /// <summary>
    /// Total elapsed time in milliseconds.
    /// </summary>
    public double TotalDurationMs { get; init; }

    /// <summary>
    /// Total logical reads.
    /// </summary>
    public long TotalLogicalReads { get; init; }

    /// <summary>
    /// Total physical reads.
    /// </summary>
    public long TotalPhysicalReads { get; init; }

    /// <summary>
    /// Total number of executions.
    /// </summary>
    public long ExecutionCount { get; init; }

    /// <summary>
    /// Last execution time.
    /// </summary>
    public DateTime? LastExecutionTimeUtc { get; init; }

    /// <summary>
    /// Average CPU time per execution.
    /// </summary>
    public double AvgCpuTimeMs => ExecutionCount > 0 ? TotalCpuTimeMs / ExecutionCount : 0;

    /// <summary>
    /// Average duration per execution.
    /// </summary>
    public double AvgDurationMs => ExecutionCount > 0 ? TotalDurationMs / ExecutionCount : 0;

    /// <summary>
    /// Average logical reads per execution.
    /// </summary>
    public double AvgLogicalReads => ExecutionCount > 0 ? (double)TotalLogicalReads / ExecutionCount : 0;
}

/// <summary>
/// Plan-level statistics result from the provider.
/// </summary>
public class PlanStatisticsResult
{
    /// <summary>
    /// SQL Server's query hash.
    /// </summary>
    public required string QueryHash { get; init; }

    /// <summary>
    /// SQL Server's plan hash (identifies this specific plan).
    /// </summary>
    public required string PlanHash { get; init; }

    /// <summary>
    /// Plan handle for retrieving the full plan XML.
    /// </summary>
    public byte[]? PlanHandle { get; init; }

    /// <summary>
    /// Query Store plan ID (if from Query Store).
    /// </summary>
    public long? QueryStorePlanId { get; init; }

    /// <summary>
    /// Total CPU time for this plan.
    /// </summary>
    public double TotalCpuTimeMs { get; init; }

    /// <summary>
    /// Total elapsed time for this plan.
    /// </summary>
    public double TotalDurationMs { get; init; }

    /// <summary>
    /// Total logical reads for this plan.
    /// </summary>
    public long TotalLogicalReads { get; init; }

    /// <summary>
    /// Total physical reads for this plan.
    /// </summary>
    public long TotalPhysicalReads { get; init; }

    /// <summary>
    /// Execution count for this plan.
    /// </summary>
    public long ExecutionCount { get; init; }

    /// <summary>
    /// When this plan was first created.
    /// </summary>
    public DateTime? CreationTimeUtc { get; init; }

    /// <summary>
    /// Last time this plan was used.
    /// </summary>
    public DateTime? LastExecutionTimeUtc { get; init; }

    /// <summary>
    /// Minimum CPU time observed.
    /// </summary>
    public double? MinCpuTimeMs { get; init; }

    /// <summary>
    /// Maximum CPU time observed.
    /// </summary>
    public double? MaxCpuTimeMs { get; init; }

    /// <summary>
    /// Minimum duration observed.
    /// </summary>
    public double? MinDurationMs { get; init; }

    /// <summary>
    /// Maximum duration observed.
    /// </summary>
    public double? MaxDurationMs { get; init; }
}
