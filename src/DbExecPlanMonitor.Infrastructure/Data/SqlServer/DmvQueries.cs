namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer;

/// <summary>
/// SQL queries for fetching execution plan data from Dynamic Management Views (DMVs).
/// </summary>
/// <remarks>
/// These queries target the plan cache and execution statistics DMVs:
/// - sys.dm_exec_query_stats - Aggregated execution statistics
/// - sys.dm_exec_sql_text - SQL text from plan handles
/// - sys.dm_exec_query_plan - XML execution plans
/// - sys.dm_exec_cached_plans - Plan cache metadata
/// 
/// DMV limitations:
/// - Data is cleared on service restart
/// - Stats are approximate (sampling-based)
/// - Memory pressure can evict plans from cache
/// </remarks>
public static class DmvQueries
{
    /// <summary>
    /// Gets top N queries by total CPU time from the plan cache.
    /// </summary>
    public const string TopQueriesByCpu = @"
SELECT TOP (@TopN)
    qs.plan_handle AS PlanHandle,
    qs.sql_handle AS SqlHandle,
    qs.query_hash AS QueryHash,
    qs.query_plan_hash AS QueryPlanHash,
    qs.statement_start_offset AS StatementStartOffset,
    qs.statement_end_offset AS StatementEndOffset,
    -- Execution counts
    qs.execution_count AS ExecutionCount,
    -- CPU time (microseconds)
    qs.total_worker_time AS TotalCpuTime,
    qs.last_worker_time AS LastCpuTime,
    qs.min_worker_time AS MinCpuTime,
    qs.max_worker_time AS MaxCpuTime,
    -- Elapsed time (microseconds)
    qs.total_elapsed_time AS TotalElapsedTime,
    qs.last_elapsed_time AS LastElapsedTime,
    qs.min_elapsed_time AS MinElapsedTime,
    qs.max_elapsed_time AS MaxElapsedTime,
    -- Logical reads
    qs.total_logical_reads AS TotalLogicalReads,
    qs.last_logical_reads AS LastLogicalReads,
    qs.min_logical_reads AS MinLogicalReads,
    qs.max_logical_reads AS MaxLogicalReads,
    -- Physical reads
    qs.total_physical_reads AS TotalPhysicalReads,
    qs.last_physical_reads AS LastPhysicalReads,
    qs.min_physical_reads AS MinPhysicalReads,
    qs.max_physical_reads AS MaxPhysicalReads,
    -- Writes
    qs.total_logical_writes AS TotalLogicalWrites,
    qs.last_logical_writes AS LastLogicalWrites,
    qs.min_logical_writes AS MinLogicalWrites,
    qs.max_logical_writes AS MaxLogicalWrites,
    -- Rows
    qs.total_rows AS TotalRows,
    qs.last_rows AS LastRows,
    qs.min_rows AS MinRows,
    qs.max_rows AS MaxRows,
    -- Timestamps
    qs.creation_time AS CreationTime,
    qs.last_execution_time AS LastExecutionTime,
    -- SQL text (substring for the specific statement)
    CASE 
        WHEN qs.statement_end_offset = -1 
        THEN SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, LEN(st.text))
        ELSE SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, 
            (qs.statement_end_offset - qs.statement_start_offset) / 2 + 1)
    END AS SqlText,
    st.text AS FullSqlText,
    st.objectid AS ObjectId,
    st.dbid AS DatabaseId,
    DB_NAME(st.dbid) AS DatabaseName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE st.dbid = DB_ID(@DatabaseName)
ORDER BY qs.total_worker_time DESC;";

    /// <summary>
    /// Gets top N queries by total logical reads.
    /// </summary>
    public const string TopQueriesByLogicalReads = @"
SELECT TOP (@TopN)
    qs.plan_handle AS PlanHandle,
    qs.sql_handle AS SqlHandle,
    qs.query_hash AS QueryHash,
    qs.query_plan_hash AS QueryPlanHash,
    qs.statement_start_offset AS StatementStartOffset,
    qs.statement_end_offset AS StatementEndOffset,
    qs.execution_count AS ExecutionCount,
    qs.total_worker_time AS TotalCpuTime,
    qs.last_worker_time AS LastCpuTime,
    qs.min_worker_time AS MinCpuTime,
    qs.max_worker_time AS MaxCpuTime,
    qs.total_elapsed_time AS TotalElapsedTime,
    qs.last_elapsed_time AS LastElapsedTime,
    qs.min_elapsed_time AS MinElapsedTime,
    qs.max_elapsed_time AS MaxElapsedTime,
    qs.total_logical_reads AS TotalLogicalReads,
    qs.last_logical_reads AS LastLogicalReads,
    qs.min_logical_reads AS MinLogicalReads,
    qs.max_logical_reads AS MaxLogicalReads,
    qs.total_physical_reads AS TotalPhysicalReads,
    qs.last_physical_reads AS LastPhysicalReads,
    qs.min_physical_reads AS MinPhysicalReads,
    qs.max_physical_reads AS MaxPhysicalReads,
    qs.total_logical_writes AS TotalLogicalWrites,
    qs.last_logical_writes AS LastLogicalWrites,
    qs.min_logical_writes AS MinLogicalWrites,
    qs.max_logical_writes AS MaxLogicalWrites,
    qs.total_rows AS TotalRows,
    qs.last_rows AS LastRows,
    qs.min_rows AS MinRows,
    qs.max_rows AS MaxRows,
    qs.creation_time AS CreationTime,
    qs.last_execution_time AS LastExecutionTime,
    CASE 
        WHEN qs.statement_end_offset = -1 
        THEN SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, LEN(st.text))
        ELSE SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, 
            (qs.statement_end_offset - qs.statement_start_offset) / 2 + 1)
    END AS SqlText,
    st.text AS FullSqlText,
    st.objectid AS ObjectId,
    st.dbid AS DatabaseId,
    DB_NAME(st.dbid) AS DatabaseName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE st.dbid = DB_ID(@DatabaseName)
ORDER BY qs.total_logical_reads DESC;";

    /// <summary>
    /// Gets top N queries by elapsed time (duration).
    /// </summary>
    public const string TopQueriesByElapsedTime = @"
SELECT TOP (@TopN)
    qs.plan_handle AS PlanHandle,
    qs.sql_handle AS SqlHandle,
    qs.query_hash AS QueryHash,
    qs.query_plan_hash AS QueryPlanHash,
    qs.statement_start_offset AS StatementStartOffset,
    qs.statement_end_offset AS StatementEndOffset,
    qs.execution_count AS ExecutionCount,
    qs.total_worker_time AS TotalCpuTime,
    qs.last_worker_time AS LastCpuTime,
    qs.min_worker_time AS MinCpuTime,
    qs.max_worker_time AS MaxCpuTime,
    qs.total_elapsed_time AS TotalElapsedTime,
    qs.last_elapsed_time AS LastElapsedTime,
    qs.min_elapsed_time AS MinElapsedTime,
    qs.max_elapsed_time AS MaxElapsedTime,
    qs.total_logical_reads AS TotalLogicalReads,
    qs.last_logical_reads AS LastLogicalReads,
    qs.min_logical_reads AS MinLogicalReads,
    qs.max_logical_reads AS MaxLogicalReads,
    qs.total_physical_reads AS TotalPhysicalReads,
    qs.last_physical_reads AS LastPhysicalReads,
    qs.min_physical_reads AS MinPhysicalReads,
    qs.max_physical_reads AS MaxPhysicalReads,
    qs.total_logical_writes AS TotalLogicalWrites,
    qs.last_logical_writes AS LastLogicalWrites,
    qs.min_logical_writes AS MinLogicalWrites,
    qs.max_logical_writes AS MaxLogicalWrites,
    qs.total_rows AS TotalRows,
    qs.last_rows AS LastRows,
    qs.min_rows AS MinRows,
    qs.max_rows AS MaxRows,
    qs.creation_time AS CreationTime,
    qs.last_execution_time AS LastExecutionTime,
    CASE 
        WHEN qs.statement_end_offset = -1 
        THEN SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, LEN(st.text))
        ELSE SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, 
            (qs.statement_end_offset - qs.statement_start_offset) / 2 + 1)
    END AS SqlText,
    st.text AS FullSqlText,
    st.objectid AS ObjectId,
    st.dbid AS DatabaseId,
    DB_NAME(st.dbid) AS DatabaseName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE st.dbid = DB_ID(@DatabaseName)
ORDER BY qs.total_elapsed_time DESC;";

    /// <summary>
    /// Gets the execution plan XML for a specific plan handle.
    /// </summary>
    public const string GetPlanXml = @"
SELECT query_plan AS PlanXml
FROM sys.dm_exec_query_plan(@PlanHandle);";

    /// <summary>
    /// Gets execution statistics for a specific query hash.
    /// Returns all cached plans for that query fingerprint.
    /// </summary>
    public const string GetStatsByQueryHash = @"
SELECT 
    qs.plan_handle AS PlanHandle,
    qs.sql_handle AS SqlHandle,
    qs.query_hash AS QueryHash,
    qs.query_plan_hash AS QueryPlanHash,
    qs.statement_start_offset AS StatementStartOffset,
    qs.statement_end_offset AS StatementEndOffset,
    qs.execution_count AS ExecutionCount,
    qs.total_worker_time AS TotalCpuTime,
    qs.last_worker_time AS LastCpuTime,
    qs.min_worker_time AS MinCpuTime,
    qs.max_worker_time AS MaxCpuTime,
    qs.total_elapsed_time AS TotalElapsedTime,
    qs.last_elapsed_time AS LastElapsedTime,
    qs.min_elapsed_time AS MinElapsedTime,
    qs.max_elapsed_time AS MaxElapsedTime,
    qs.total_logical_reads AS TotalLogicalReads,
    qs.last_logical_reads AS LastLogicalReads,
    qs.min_logical_reads AS MinLogicalReads,
    qs.max_logical_reads AS MaxLogicalReads,
    qs.total_physical_reads AS TotalPhysicalReads,
    qs.last_physical_reads AS LastPhysicalReads,
    qs.min_physical_reads AS MinPhysicalReads,
    qs.max_physical_reads AS MaxPhysicalReads,
    qs.total_logical_writes AS TotalLogicalWrites,
    qs.last_logical_writes AS LastLogicalWrites,
    qs.min_logical_writes AS MinLogicalWrites,
    qs.max_logical_writes AS MaxLogicalWrites,
    qs.total_rows AS TotalRows,
    qs.last_rows AS LastRows,
    qs.min_rows AS MinRows,
    qs.max_rows AS MaxRows,
    qs.creation_time AS CreationTime,
    qs.last_execution_time AS LastExecutionTime,
    CASE 
        WHEN qs.statement_end_offset = -1 
        THEN SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, LEN(st.text))
        ELSE SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, 
            (qs.statement_end_offset - qs.statement_start_offset) / 2 + 1)
    END AS SqlText,
    st.text AS FullSqlText,
    st.objectid AS ObjectId,
    st.dbid AS DatabaseId,
    DB_NAME(st.dbid) AS DatabaseName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE qs.query_hash = @QueryHash;";

    /// <summary>
    /// Checks if a database exists and returns basic info.
    /// </summary>
    public const string CheckDatabaseExists = @"
SELECT 
    database_id AS DatabaseId,
    name AS DatabaseName,
    state_desc AS State,
    is_read_only AS IsReadOnly,
    compatibility_level AS CompatibilityLevel
FROM sys.databases
WHERE name = @DatabaseName;";

    /// <summary>
    /// Gets recently executed queries (last N minutes) for change detection.
    /// </summary>
    public const string GetRecentlyExecutedQueries = @"
SELECT 
    qs.plan_handle AS PlanHandle,
    qs.query_hash AS QueryHash,
    qs.query_plan_hash AS QueryPlanHash,
    qs.execution_count AS ExecutionCount,
    qs.total_worker_time AS TotalCpuTime,
    qs.total_elapsed_time AS TotalElapsedTime,
    qs.total_logical_reads AS TotalLogicalReads,
    qs.last_execution_time AS LastExecutionTime,
    CASE 
        WHEN qs.statement_end_offset = -1 
        THEN SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, LEN(st.text))
        ELSE SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, 
            (qs.statement_end_offset - qs.statement_start_offset) / 2 + 1)
    END AS SqlText,
    st.dbid AS DatabaseId,
    DB_NAME(st.dbid) AS DatabaseName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE qs.last_execution_time >= DATEADD(MINUTE, -@MinutesAgo, GETUTCDATE())
  AND st.dbid = DB_ID(@DatabaseName)
ORDER BY qs.last_execution_time DESC;";

    /// <summary>
    /// Gets server-level wait statistics for context.
    /// </summary>
    public const string GetTopWaits = @"
SELECT TOP (@TopN)
    wait_type AS WaitType,
    waiting_tasks_count AS WaitingTasksCount,
    wait_time_ms AS WaitTimeMs,
    max_wait_time_ms AS MaxWaitTimeMs,
    signal_wait_time_ms AS SignalWaitTimeMs
FROM sys.dm_os_wait_stats
WHERE wait_type NOT IN (
    'CLR_SEMAPHORE', 'LAZYWRITER_SLEEP', 'RESOURCE_QUEUE', 
    'SLEEP_TASK', 'SLEEP_SYSTEMTASK', 'SQLTRACE_BUFFER_FLUSH',
    'WAITFOR', 'LOGMGR_QUEUE', 'CHECKPOINT_QUEUE', 
    'REQUEST_FOR_DEADLOCK_SEARCH', 'XE_TIMER_EVENT', 
    'BROKER_TO_FLUSH', 'BROKER_TASK_STOP', 'CLR_MANUAL_EVENT',
    'CLR_AUTO_EVENT', 'DISPATCHER_QUEUE_SEMAPHORE', 
    'FT_IFTS_SCHEDULER_IDLE_WAIT', 'XE_DISPATCHER_WAIT', 
    'XE_DISPATCHER_JOIN', 'SQLTRACE_INCREMENTAL_FLUSH_SLEEP'
)
AND wait_time_ms > 0
ORDER BY wait_time_ms DESC;";
}
