namespace DbExecPlanMonitor.Application.DTOs;

/// <summary>
/// Data transfer object for query execution plan metrics.
/// Unified format for both DMV and Query Store data.
/// </summary>
/// <remarks>
/// This DTO bridges the gap between raw SQL Server data and domain entities.
/// It normalizes the different formats from DMVs and Query Store into
/// a consistent structure that application services can work with.
/// 
/// All time values are in milliseconds for consistency.
/// All counts are exact (not averaged unless prefixed with Avg).
/// </remarks>
public class PlanMetricsDto
{
    #region Query Identification

    /// <summary>
    /// Query hash in hex format (e.g., "0x1234ABCD5678EF90").
    /// This identifies the normalized query pattern.
    /// </summary>
    public string QueryHashHex { get; set; } = string.Empty;

    /// <summary>
    /// Plan hash in hex format.
    /// This identifies the specific execution plan.
    /// </summary>
    public string PlanHashHex { get; set; } = string.Empty;

    /// <summary>
    /// The SQL text of the query.
    /// May be a statement within a batch.
    /// </summary>
    public string SqlText { get; set; } = string.Empty;

    /// <summary>
    /// Database name where the query was executed.
    /// </summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// Object name if query is part of a stored procedure/function.
    /// </summary>
    public string? ObjectName { get; set; }

    /// <summary>
    /// Schema of the object (if applicable).
    /// </summary>
    public string? SchemaName { get; set; }

    #endregion

    #region Execution Counts

    /// <summary>
    /// Total number of executions.
    /// </summary>
    public long ExecutionCount { get; set; }

    #endregion

    #region CPU Time (milliseconds)

    /// <summary>
    /// Average CPU time per execution in milliseconds.
    /// </summary>
    public double AvgCpuTimeMs { get; set; }

    /// <summary>
    /// Total CPU time across all executions in milliseconds.
    /// </summary>
    public double TotalCpuTimeMs { get; set; }

    /// <summary>
    /// Minimum CPU time observed in milliseconds.
    /// </summary>
    public double MinCpuTimeMs { get; set; }

    /// <summary>
    /// Maximum CPU time observed in milliseconds.
    /// </summary>
    public double MaxCpuTimeMs { get; set; }

    /// <summary>
    /// Standard deviation of CPU time (Query Store only).
    /// </summary>
    public double? StdevCpuTimeMs { get; set; }

    #endregion

    #region Duration (milliseconds)

    /// <summary>
    /// Average elapsed time per execution in milliseconds.
    /// </summary>
    public double AvgDurationMs { get; set; }

    /// <summary>
    /// Total elapsed time across all executions in milliseconds.
    /// </summary>
    public double TotalDurationMs { get; set; }

    /// <summary>
    /// Minimum elapsed time observed in milliseconds.
    /// </summary>
    public double MinDurationMs { get; set; }

    /// <summary>
    /// Maximum elapsed time observed in milliseconds.
    /// </summary>
    public double MaxDurationMs { get; set; }

    /// <summary>
    /// Standard deviation of duration (Query Store only).
    /// </summary>
    public double? StdevDurationMs { get; set; }

    #endregion

    #region Logical Reads

    /// <summary>
    /// Average logical reads per execution.
    /// </summary>
    public double AvgLogicalReads { get; set; }

    /// <summary>
    /// Total logical reads across all executions.
    /// </summary>
    public long TotalLogicalReads { get; set; }

    /// <summary>
    /// Minimum logical reads observed.
    /// </summary>
    public long MinLogicalReads { get; set; }

    /// <summary>
    /// Maximum logical reads observed.
    /// </summary>
    public long MaxLogicalReads { get; set; }

    #endregion

    #region Other IO Metrics

    /// <summary>
    /// Average physical reads per execution.
    /// </summary>
    public double AvgPhysicalReads { get; set; }

    /// <summary>
    /// Average logical writes per execution.
    /// </summary>
    public double AvgLogicalWrites { get; set; }

    #endregion

    #region Row Counts

    /// <summary>
    /// Average rows returned per execution.
    /// </summary>
    public double AvgRowCount { get; set; }

    /// <summary>
    /// Minimum rows returned.
    /// </summary>
    public long MinRowCount { get; set; }

    /// <summary>
    /// Maximum rows returned.
    /// </summary>
    public long MaxRowCount { get; set; }

    #endregion

    #region Memory

    /// <summary>
    /// Average memory grant in KB (Query Store only).
    /// </summary>
    public double? AvgMemoryGrantKb { get; set; }

    /// <summary>
    /// Maximum memory grant in KB (Query Store only).
    /// </summary>
    public double? MaxMemoryGrantKb { get; set; }

    /// <summary>
    /// Average TempDB space used (Query Store only).
    /// </summary>
    public double? AvgTempdbSpaceUsedKb { get; set; }

    #endregion

    #region Timestamps

    /// <summary>
    /// When the plan was created/cached.
    /// </summary>
    public DateTime? CreationTime { get; set; }

    /// <summary>
    /// First execution time (Query Store only).
    /// </summary>
    public DateTime? FirstExecutionTime { get; set; }

    /// <summary>
    /// Most recent execution time.
    /// </summary>
    public DateTime? LastExecutionTime { get; set; }

    /// <summary>
    /// Last compilation time (Query Store only).
    /// </summary>
    public DateTime? LastCompileTime { get; set; }

    #endregion

    #region Query Store Specific

    /// <summary>
    /// Whether a plan is forced for this query (Query Store only).
    /// </summary>
    public bool IsForced { get; set; }

    /// <summary>
    /// Query Store query_id for plan forcing operations.
    /// </summary>
    public long? QueryStoreQueryId { get; set; }

    /// <summary>
    /// Query Store plan_id for plan forcing operations.
    /// </summary>
    public long? QueryStorePlanId { get; set; }

    #endregion

    #region Metadata

    /// <summary>
    /// Source of the data: "DMV" or "QueryStore".
    /// </summary>
    public string DataSource { get; set; } = string.Empty;

    /// <summary>
    /// When this data was collected.
    /// </summary>
    public DateTime CollectedAtUtc { get; set; } = DateTime.UtcNow;

    #endregion

    #region Computed Properties

    /// <summary>
    /// CPU time variability (max/min ratio).
    /// High variability may indicate parameter sniffing issues.
    /// </summary>
    public double CpuTimeVariability =>
        MinCpuTimeMs > 0 ? MaxCpuTimeMs / MinCpuTimeMs : 0;

    /// <summary>
    /// Duration variability (max/min ratio).
    /// </summary>
    public double DurationVariability =>
        MinDurationMs > 0 ? MaxDurationMs / MinDurationMs : 0;

    /// <summary>
    /// Reads variability (max/min ratio).
    /// </summary>
    public double ReadsVariability =>
        MinLogicalReads > 0 ? (double)MaxLogicalReads / MinLogicalReads : 0;

    /// <summary>
    /// Whether this query shows signs of parameter sensitivity.
    /// High variability suggests different execution paths based on parameters.
    /// </summary>
    public bool HasHighVariability =>
        CpuTimeVariability > 10 || DurationVariability > 10 || ReadsVariability > 100;

    #endregion
}
