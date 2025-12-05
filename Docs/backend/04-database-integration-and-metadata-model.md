# 04 – Database Integration and Metadata Model

This spec focuses on **how we talk to SQL Server** to obtain metadata and statistics required for execution plan monitoring.

## Target Features in SQL Server

We will leverage:

- **Dynamic Management Views (DMVs)**:
  - `sys.dm_exec_query_stats`
  - `sys.dm_exec_sql_text`
  - `sys.dm_exec_query_plan`
- **Query Store** (if enabled):
  - `sys.query_store_query`
  - `sys.query_store_plan`
  - `sys.query_store_runtime_stats`

Version 1 can work using DMVs alone; Query Store is a bonus.

## Connection Model

- All DB connections go through an **Infrastructure-layer provider**:
  - `ISqlConnectionFactory`
    - Returns `SqlConnection` with:
      - Correct connection string
      - Reasonable default timeouts
      - Connection pooling enabled (default ADO.NET behaviour)

### Example Interface (for later implementation)

```csharp
public interface ISqlConnectionFactory
{
    SqlConnection CreateMonitoringConnection(DatabaseInstanceId instanceId, string databaseName);
}
```

> Do **not** implement here – this file is for design. Implementation comes later.

## Metadata Concepts

We need to model:

- `DatabaseInstanceConfig`
  - Name, host, port
  - List of databases to monitor
  - Credentials (ideally via secure store, not hard-coded)

- `DmvPlanRecord`
  - Raw view of DMVs for a query + plan:
    - SQL text
    - Plan handle
    - Execution stats
    - Last execution time

- `QueryStorePlanRecord`
  - Similar to `DmvPlanRecord` but from Query Store.

These are **Infrastructure models** used only in the Infra layer and mapped into **Domain models** (`ExecutionPlanSnapshot`, `PlanMetricSample`, etc.).

## Queries We Expect to Run

We will define **parameterised queries** that:

1. Pull top N plans by resource usage over a time window.
2. Pull metrics for a specific query fingerprint.
3. Pull information required to compute baselines.

Example (rough, for later refinement in Infra):

```sql
SELECT TOP (@TopN)
    qs.plan_handle,
    qs.total_worker_time,
    qs.total_elapsed_time,
    qs.total_logical_reads,
    qs.execution_count,
    qs.last_execution_time,
    st.text AS sql_text
FROM sys.dm_exec_query_stats AS qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
ORDER BY qs.total_worker_time DESC;
```

We will store these SQL strings in the Infrastructure layer, with:

- **Command timeouts** tuned for monitoring (e.g., 5–30 seconds).
- Robust error handling and logging around every call.

## Provider Model

To prepare for other DB engines, we introduce **interfaces** in the Application layer:

- `IPlanStatisticsProvider`
  - `Task<IReadOnlyList<PlanMetricSample>> GetTopPlansAsync(DatabaseInstanceId instanceId, int topN, TimeWindow window);`
- `IPlanDetailsProvider`
  - `Task<ExecutionPlanSnapshot?> GetPlanSnapshotAsync(DatabaseInstanceId instanceId, PlanHandle handle);`

SQL Server implementation lives in:

- `DbExecPlanMonitor.Infrastructure.Data.SqlServer`

This keeps the rest of the system DB-agnostic.

Next: see `05-ado-net-data-access-layer.md` for details on our ADO.NET patterns and repository model.
