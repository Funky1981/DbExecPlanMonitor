namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer;

/// <summary>
/// SQL queries for fetching data from Query Store views.
/// </summary>
/// <remarks>
/// Query Store is available in SQL Server 2016+ and must be enabled per-database.
/// These queries target:
/// - sys.query_store_query - Query metadata
/// - sys.query_store_plan - Execution plans
/// - sys.query_store_runtime_stats - Runtime statistics
/// - sys.query_store_runtime_stats_interval - Time intervals
/// - sys.query_store_query_text - Full query text
/// 
/// Benefits over DMVs:
/// - Persists across service restarts
/// - Tracks plan history over time
/// - Supports plan regression detection
/// - Enables plan forcing
/// </remarks>
public static class QueryStoreQueries
{
    /// <summary>
    /// Checks if Query Store is enabled for the current database.
    /// </summary>
    public const string CheckQueryStoreEnabled = @"
SELECT 
    desired_state_desc AS DesiredState,
    actual_state_desc AS ActualState,
    readonly_reason AS ReadOnlyReason,
    current_storage_size_mb AS CurrentStorageSizeMb,
    max_storage_size_mb AS MaxStorageSizeMb,
    query_capture_mode_desc AS QueryCaptureMode,
    size_based_cleanup_mode_desc AS SizeBasedCleanupMode,
    stale_query_threshold_days AS StaleQueryThresholdDays,
    max_plans_per_query AS MaxPlansPerQuery,
    interval_length_minutes AS IntervalLengthMinutes
FROM sys.database_query_store_options;";

    /// <summary>
    /// Gets top N queries by total CPU time from Query Store.
    /// Aggregates across all runtime intervals.
    /// </summary>
    public const string TopQueriesByCpu = @"
SELECT TOP (@TopN)
    q.query_id AS QueryId,
    q.query_hash AS QueryHash,
    qt.query_sql_text AS QueryText,
    q.object_id AS ObjectId,
    OBJECT_NAME(q.object_id) AS ObjectName,
    OBJECT_SCHEMA_NAME(q.object_id) AS SchemaName,
    p.plan_id AS PlanId,
    p.query_plan_hash AS QueryPlanHash,
    TRY_CAST(p.query_plan AS NVARCHAR(MAX)) AS PlanXml,
    p.is_forced_plan AS IsForced,
    p.is_natively_compiled AS IsNativelyCompiled,
    p.compatibility_level AS CompatibilityLevel,
    -- Aggregated stats across intervals
    SUM(rs.count_executions) AS ExecutionCount,
    AVG(rs.avg_cpu_time) AS AvgCpuTime,
    MAX(rs.last_cpu_time) AS LastCpuTime,
    MIN(rs.min_cpu_time) AS MinCpuTime,
    MAX(rs.max_cpu_time) AS MaxCpuTime,
    AVG(rs.stdev_cpu_time) AS StdevCpuTime,
    AVG(rs.avg_duration) AS AvgDuration,
    MAX(rs.last_duration) AS LastDuration,
    MIN(rs.min_duration) AS MinDuration,
    MAX(rs.max_duration) AS MaxDuration,
    AVG(rs.stdev_duration) AS StdevDuration,
    AVG(rs.avg_logical_io_reads) AS AvgLogicalReads,
    MAX(rs.last_logical_io_reads) AS LastLogicalReads,
    MIN(rs.min_logical_io_reads) AS MinLogicalReads,
    MAX(rs.max_logical_io_reads) AS MaxLogicalReads,
    AVG(rs.avg_physical_io_reads) AS AvgPhysicalReads,
    MAX(rs.last_physical_io_reads) AS LastPhysicalReads,
    MIN(rs.min_physical_io_reads) AS MinPhysicalReads,
    MAX(rs.max_physical_io_reads) AS MaxPhysicalReads,
    AVG(rs.avg_logical_io_writes) AS AvgLogicalWrites,
    MAX(rs.last_logical_io_writes) AS LastLogicalWrites,
    MIN(rs.min_logical_io_writes) AS MinLogicalWrites,
    MAX(rs.max_logical_io_writes) AS MaxLogicalWrites,
    AVG(rs.avg_rowcount) AS AvgRowCount,
    MAX(rs.last_rowcount) AS LastRowCount,
    MIN(rs.min_rowcount) AS MinRowCount,
    MAX(rs.max_rowcount) AS MaxRowCount,
    AVG(rs.avg_query_max_used_memory) AS AvgMemoryGrant,
    MAX(rs.last_query_max_used_memory) AS LastMemoryGrant,
    MIN(rs.min_query_max_used_memory) AS MinMemoryGrant,
    MAX(rs.max_query_max_used_memory) AS MaxMemoryGrant,
    AVG(rs.avg_tempdb_space_used) AS AvgTempdbSpaceUsed,
    MIN(rs.first_execution_time) AS FirstExecutionTime,
    MAX(rs.last_execution_time) AS LastExecutionTime,
    MAX(p.last_compile_start_time) AS LastCompileTime
FROM sys.query_store_query q
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan p ON q.query_id = p.query_id
JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
WHERE p.is_online_index_plan = 0
GROUP BY q.query_id, q.query_hash, qt.query_sql_text, q.object_id,
    p.plan_id, p.query_plan_hash, p.query_plan, p.is_forced_plan, 
    p.is_natively_compiled, p.compatibility_level
ORDER BY SUM(rs.avg_cpu_time * rs.count_executions) DESC;";

    /// <summary>
    /// Gets top N queries by logical reads from Query Store.
    /// </summary>
    public const string TopQueriesByLogicalReads = @"
SELECT TOP (@TopN)
    q.query_id AS QueryId,
    q.query_hash AS QueryHash,
    qt.query_sql_text AS QueryText,
    q.object_id AS ObjectId,
    OBJECT_NAME(q.object_id) AS ObjectName,
    OBJECT_SCHEMA_NAME(q.object_id) AS SchemaName,
    p.plan_id AS PlanId,
    p.query_plan_hash AS QueryPlanHash,
    TRY_CAST(p.query_plan AS NVARCHAR(MAX)) AS PlanXml,
    p.is_forced_plan AS IsForced,
    p.is_natively_compiled AS IsNativelyCompiled,
    p.compatibility_level AS CompatibilityLevel,
    SUM(rs.count_executions) AS ExecutionCount,
    AVG(rs.avg_cpu_time) AS AvgCpuTime,
    MAX(rs.last_cpu_time) AS LastCpuTime,
    MIN(rs.min_cpu_time) AS MinCpuTime,
    MAX(rs.max_cpu_time) AS MaxCpuTime,
    AVG(rs.avg_duration) AS AvgDuration,
    MAX(rs.last_duration) AS LastDuration,
    MIN(rs.min_duration) AS MinDuration,
    MAX(rs.max_duration) AS MaxDuration,
    AVG(rs.avg_logical_io_reads) AS AvgLogicalReads,
    MAX(rs.last_logical_io_reads) AS LastLogicalReads,
    MIN(rs.min_logical_io_reads) AS MinLogicalReads,
    MAX(rs.max_logical_io_reads) AS MaxLogicalReads,
    AVG(rs.avg_physical_io_reads) AS AvgPhysicalReads,
    MAX(rs.last_physical_io_reads) AS LastPhysicalReads,
    MIN(rs.min_physical_io_reads) AS MinPhysicalReads,
    MAX(rs.max_physical_io_reads) AS MaxPhysicalReads,
    AVG(rs.avg_logical_io_writes) AS AvgLogicalWrites,
    MAX(rs.last_logical_io_writes) AS LastLogicalWrites,
    MIN(rs.min_logical_io_writes) AS MinLogicalWrites,
    MAX(rs.max_logical_io_writes) AS MaxLogicalWrites,
    AVG(rs.avg_rowcount) AS AvgRowCount,
    MAX(rs.last_rowcount) AS LastRowCount,
    MIN(rs.min_rowcount) AS MinRowCount,
    MAX(rs.max_rowcount) AS MaxRowCount,
    AVG(rs.avg_query_max_used_memory) AS AvgMemoryGrant,
    AVG(rs.avg_tempdb_space_used) AS AvgTempdbSpaceUsed,
    MIN(rs.first_execution_time) AS FirstExecutionTime,
    MAX(rs.last_execution_time) AS LastExecutionTime,
    MAX(p.last_compile_start_time) AS LastCompileTime
FROM sys.query_store_query q
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan p ON q.query_id = p.query_id
JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
WHERE p.is_online_index_plan = 0
GROUP BY q.query_id, q.query_hash, qt.query_sql_text, q.object_id,
    p.plan_id, p.query_plan_hash, p.query_plan, p.is_forced_plan, 
    p.is_natively_compiled, p.compatibility_level
ORDER BY SUM(rs.avg_logical_io_reads * rs.count_executions) DESC;";

    /// <summary>
    /// Gets statistics for a specific query from Query Store.
    /// Used for baseline comparison and regression detection.
    /// </summary>
    public const string GetQueryByHash = @"
SELECT 
    q.query_id AS QueryId,
    q.query_hash AS QueryHash,
    qt.query_sql_text AS QueryText,
    q.object_id AS ObjectId,
    OBJECT_NAME(q.object_id) AS ObjectName,
    OBJECT_SCHEMA_NAME(q.object_id) AS SchemaName,
    p.plan_id AS PlanId,
    p.query_plan_hash AS QueryPlanHash,
    TRY_CAST(p.query_plan AS NVARCHAR(MAX)) AS PlanXml,
    p.is_forced_plan AS IsForced,
    p.is_natively_compiled AS IsNativelyCompiled,
    p.compatibility_level AS CompatibilityLevel,
    rsi.runtime_stats_interval_id AS RuntimeStatsIntervalId,
    rsi.start_time AS IntervalStartTime,
    rsi.end_time AS IntervalEndTime,
    rs.count_executions AS ExecutionCount,
    rs.avg_cpu_time AS AvgCpuTime,
    rs.last_cpu_time AS LastCpuTime,
    rs.min_cpu_time AS MinCpuTime,
    rs.max_cpu_time AS MaxCpuTime,
    rs.stdev_cpu_time AS StdevCpuTime,
    rs.avg_duration AS AvgDuration,
    rs.last_duration AS LastDuration,
    rs.min_duration AS MinDuration,
    rs.max_duration AS MaxDuration,
    rs.stdev_duration AS StdevDuration,
    rs.avg_logical_io_reads AS AvgLogicalReads,
    rs.last_logical_io_reads AS LastLogicalReads,
    rs.min_logical_io_reads AS MinLogicalReads,
    rs.max_logical_io_reads AS MaxLogicalReads,
    rs.stdev_logical_io_reads AS StdevLogicalReads,
    rs.avg_physical_io_reads AS AvgPhysicalReads,
    rs.last_physical_io_reads AS LastPhysicalReads,
    rs.min_physical_io_reads AS MinPhysicalReads,
    rs.max_physical_io_reads AS MaxPhysicalReads,
    rs.avg_logical_io_writes AS AvgLogicalWrites,
    rs.last_logical_io_writes AS LastLogicalWrites,
    rs.min_logical_io_writes AS MinLogicalWrites,
    rs.max_logical_io_writes AS MaxLogicalWrites,
    rs.avg_rowcount AS AvgRowCount,
    rs.last_rowcount AS LastRowCount,
    rs.min_rowcount AS MinRowCount,
    rs.max_rowcount AS MaxRowCount,
    rs.avg_query_max_used_memory AS AvgMemoryGrant,
    rs.last_query_max_used_memory AS LastMemoryGrant,
    rs.min_query_max_used_memory AS MinMemoryGrant,
    rs.max_query_max_used_memory AS MaxMemoryGrant,
    rs.avg_tempdb_space_used AS AvgTempdbSpaceUsed,
    rs.first_execution_time AS FirstExecutionTime,
    rs.last_execution_time AS LastExecutionTime,
    p.last_compile_start_time AS LastCompileTime
FROM sys.query_store_query q
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan p ON q.query_id = p.query_id
JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
JOIN sys.query_store_runtime_stats_interval rsi ON rs.runtime_stats_interval_id = rsi.runtime_stats_interval_id
WHERE q.query_hash = @QueryHash
ORDER BY rsi.start_time DESC, p.plan_id;";

    /// <summary>
    /// Gets all plans for a specific query (for plan regression detection).
    /// </summary>
    public const string GetPlansForQuery = @"
SELECT 
    p.plan_id AS PlanId,
    p.query_plan_hash AS QueryPlanHash,
    TRY_CAST(p.query_plan AS NVARCHAR(MAX)) AS PlanXml,
    p.is_forced_plan AS IsForced,
    p.force_failure_count AS ForceFailureCount,
    p.last_force_failure_reason_desc AS LastForceFailureReason,
    p.count_compiles AS CompileCount,
    p.initial_compile_start_time AS InitialCompileTime,
    p.last_compile_start_time AS LastCompileTime,
    p.last_execution_time AS LastExecutionTime,
    p.is_natively_compiled AS IsNativelyCompiled,
    p.compatibility_level AS CompatibilityLevel
FROM sys.query_store_plan p
JOIN sys.query_store_query q ON p.query_id = q.query_id
WHERE q.query_id = @QueryId
ORDER BY p.last_execution_time DESC;";

    /// <summary>
    /// Identifies queries with plan regressions (multiple plans, one worse).
    /// </summary>
    public const string FindRegressedQueries = @"
WITH QueryStats AS (
    SELECT 
        q.query_id,
        q.query_hash,
        p.plan_id,
        p.query_plan_hash,
        AVG(rs.avg_cpu_time) AS avg_cpu_time,
        AVG(rs.avg_duration) AS avg_duration,
        AVG(rs.avg_logical_io_reads) AS avg_logical_reads,
        SUM(rs.count_executions) AS total_executions,
        MAX(rs.last_execution_time) AS last_execution
    FROM sys.query_store_query q
    JOIN sys.query_store_plan p ON q.query_id = p.query_id
    JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
    JOIN sys.query_store_runtime_stats_interval rsi ON rs.runtime_stats_interval_id = rsi.runtime_stats_interval_id
    WHERE rsi.start_time >= DATEADD(DAY, -@DaysBack, GETUTCDATE())
    GROUP BY q.query_id, q.query_hash, p.plan_id, p.query_plan_hash
),
QueryPlanCounts AS (
    SELECT 
        query_id,
        COUNT(DISTINCT plan_id) AS plan_count,
        MIN(avg_cpu_time) AS best_cpu_time,
        MAX(avg_cpu_time) AS worst_cpu_time
    FROM QueryStats
    GROUP BY query_id
    HAVING COUNT(DISTINCT plan_id) > 1
)
SELECT TOP (@TopN)
    qs.query_id AS QueryId,
    qs.query_hash AS QueryHash,
    qt.query_sql_text AS QueryText,
    qpc.plan_count AS PlanCount,
    qpc.best_cpu_time AS BestCpuTime,
    qpc.worst_cpu_time AS WorstCpuTime,
    (qpc.worst_cpu_time / NULLIF(qpc.best_cpu_time, 0)) AS RegressionFactor
FROM QueryPlanCounts qpc
JOIN sys.query_store_query q ON qpc.query_id = q.query_id
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
JOIN QueryStats qs ON qpc.query_id = qs.query_id
WHERE qpc.worst_cpu_time > qpc.best_cpu_time * @RegressionThreshold
ORDER BY (qpc.worst_cpu_time / NULLIF(qpc.best_cpu_time, 0)) DESC;";

    /// <summary>
    /// Forces a specific plan for a query.
    /// </summary>
    public const string ForcePlan = @"
EXEC sp_query_store_force_plan @query_id = @QueryId, @plan_id = @PlanId;";

    /// <summary>
    /// Unforces a plan (allows optimizer to choose).
    /// </summary>
    public const string UnforcePlan = @"
EXEC sp_query_store_unforce_plan @query_id = @QueryId, @plan_id = @PlanId;";

    /// <summary>
    /// Gets Query Store statistics for capacity planning.
    /// </summary>
    public const string GetQueryStoreStats = @"
SELECT 
    COUNT(DISTINCT q.query_id) AS TotalQueries,
    COUNT(DISTINCT p.plan_id) AS TotalPlans,
    COUNT(*) AS TotalRuntimeStatsRecords,
    MIN(rsi.start_time) AS OldestDataTime,
    MAX(rsi.end_time) AS NewestDataTime,
    (SELECT current_storage_size_mb FROM sys.database_query_store_options) AS CurrentStorageMb,
    (SELECT max_storage_size_mb FROM sys.database_query_store_options) AS MaxStorageMb
FROM sys.query_store_query q
JOIN sys.query_store_plan p ON q.query_id = p.query_id
LEFT JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
LEFT JOIN sys.query_store_runtime_stats_interval rsi ON rs.runtime_stats_interval_id = rsi.runtime_stats_interval_id;";
}
