# Doc 04: Database Integration and Metadata Model

This document explains the database integration layer that connects our monitoring system to SQL Server.

## Overview

The Infrastructure layer implements the interfaces defined in the Application layer, providing concrete SQL Server access using ADO.NET with `Microsoft.Data.SqlClient`.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    DbExecPlanMonitor.Worker                      │
│                   (Hosts the background service)                 │
└────────────────────────────┬────────────────────────────────────┘
                             │ uses
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                 DbExecPlanMonitor.Application                    │
│              IPlanStatisticsProvider, IPlanDetailsProvider       │
└────────────────────────────┬────────────────────────────────────┘
                             │ implemented by
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                DbExecPlanMonitor.Infrastructure                  │
│   ┌───────────────────────────────────────────────────────┐     │
│   │              Data/SqlServer                           │     │
│   │  ┌─────────────────────────────────────────────────┐ │     │
│   │  │  ISqlConnectionFactory → SqlConnectionFactory   │ │     │
│   │  │  IPlanStatisticsProvider → DmvPlanStatsProvider │ │     │
│   │  │  IPlanDetailsProvider → DmvPlanDetailsProvider  │ │     │
│   │  └─────────────────────────────────────────────────┘ │     │
│   │  ┌─────────────────────────────────────────────────┐ │     │
│   │  │  DmvQueries.cs - DMV SQL templates              │ │     │
│   │  │  QueryStoreQueries.cs - Query Store SQL         │ │     │
│   │  └─────────────────────────────────────────────────┘ │     │
│   │  ┌─────────────────────────────────────────────────┐ │     │
│   │  │  Models/                                        │ │     │
│   │  │   - DatabaseInstanceConfig                      │ │     │
│   │  │   - DmvQueryStatsRecord                         │ │     │
│   │  │   - QueryStoreRecord                            │ │     │
│   │  └─────────────────────────────────────────────────┘ │     │
│   └───────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────┘
                             │ queries
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                      SQL Server DMVs                             │
│  sys.dm_exec_query_stats │ sys.dm_exec_sql_text                 │
│  sys.dm_exec_query_plan  │ sys.dm_exec_cached_plans             │
├─────────────────────────────────────────────────────────────────┤
│                    Query Store (if enabled)                      │
│  sys.query_store_query    │ sys.query_store_plan                │
│  sys.query_store_runtime_stats │ sys.query_store_query_text     │
└─────────────────────────────────────────────────────────────────┘
```

---

## Files Created

### Domain Layer

#### `TimeWindow.cs`
**Path:** `src/DbExecPlanMonitor.Domain/ValueObjects/TimeWindow.cs`

A **value object** representing a time range for filtering queries.

```csharp
public readonly struct TimeWindow
{
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }
    public TimeSpan Duration => EndUtc - StartUtc;
    
    // Factory methods
    public static TimeWindow LastHours(int hours);
    public static TimeWindow LastMinutes(int minutes);
    public static TimeWindow LastDays(int days);
}
```

**Usage:**
```csharp
var window = TimeWindow.LastHours(1);  // Last 1 hour
var stats = await provider.GetTopQueriesAsync(dbId, 10, window, QueryOrderBy.TotalCpuTime);
```

**Why it exists:**
- Immutable (safe to pass around)
- Stack-allocated struct (efficient)
- Self-validating (end must be after start)

---

### Application Layer

#### `IPlanStatisticsProvider.cs`
**Path:** `src/DbExecPlanMonitor.Application/Interfaces/IPlanStatisticsProvider.cs`

The primary **interface** for collecting query execution statistics.

```csharp
public interface IPlanStatisticsProvider
{
    // Get top N queries by CPU, duration, or reads
    Task<IReadOnlyList<QueryStatisticsResult>> GetTopQueriesAsync(
        Guid databaseId,
        int topN,
        TimeWindow window,
        QueryOrderBy orderBy = QueryOrderBy.TotalCpuTime,
        CancellationToken cancellationToken = default);

    // Get stats for a specific query
    Task<QueryStatisticsResult?> GetQueryStatisticsAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default);

    // Get all plans for a query (for regression detection)
    Task<IReadOnlyList<PlanStatisticsResult>> GetPlanStatisticsAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default);

    // Check if Query Store is available
    Task<bool> IsQueryStoreEnabledAsync(
        Guid databaseId,
        CancellationToken cancellationToken = default);
}
```

**Why an interface?**
- Application layer doesn't know about SQL Server specifics
- Could swap in PostgreSQL, MySQL, or mock implementation
- Enables unit testing without a real database

#### `QueryOrderBy` Enum
```csharp
public enum QueryOrderBy
{
    TotalCpuTime,      // Most CPU-hungry overall
    TotalDuration,     // Longest running
    TotalLogicalReads, // Most IO
    ExecutionCount,    // Most frequently run
    AvgCpuTime,        // Worst per-execution CPU
    AvgDuration,       // Worst per-execution time
    AvgLogicalReads    // Worst per-execution IO
}
```

#### `QueryStatisticsResult` Class
Aggregated stats for a query pattern:
```csharp
public class QueryStatisticsResult
{
    public required string QueryHash { get; init; }    // "0x1234ABCD..."
    public required string QueryText { get; init; }    // The actual SQL
    public string? ObjectName { get; init; }           // Stored proc name
    public double TotalCpuTimeMs { get; init; }
    public double TotalDurationMs { get; init; }
    public long TotalLogicalReads { get; init; }
    public long ExecutionCount { get; init; }
    public DateTime? LastExecutionTimeUtc { get; init; }
    
    // Computed
    public double AvgCpuTimeMs => ExecutionCount > 0 ? TotalCpuTimeMs / ExecutionCount : 0;
}
```

#### `PlanStatisticsResult` Class
Stats for a specific execution plan (one query can have multiple plans):
```csharp
public class PlanStatisticsResult
{
    public required string QueryHash { get; init; }
    public required string PlanHash { get; init; }     // Identifies this plan
    public byte[]? PlanHandle { get; init; }           // For fetching XML
    public long? QueryStorePlanId { get; init; }       // Query Store ID
    public double TotalCpuTimeMs { get; init; }
    public double? MinCpuTimeMs { get; init; }
    public double? MaxCpuTimeMs { get; init; }
    // ...more stats
}
```

---

#### `IPlanDetailsProvider.cs`
**Path:** `src/DbExecPlanMonitor.Application/Interfaces/IPlanDetailsProvider.cs`

Interface for fetching the actual **execution plan XML**.

```csharp
public interface IPlanDetailsProvider
{
    // Get plan by binary handle (from DMVs)
    Task<PlanDetailsResult?> GetPlanByHandleAsync(
        Guid databaseId,
        byte[] planHandle,
        CancellationToken cancellationToken = default);

    // Get plan by Query Store ID
    Task<PlanDetailsResult?> GetPlanFromQueryStoreAsync(
        Guid databaseId,
        long queryStorePlanId,
        CancellationToken cancellationToken = default);

    // Get all plans for a query
    Task<IReadOnlyList<PlanDetailsResult>> GetPlansForQueryAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default);
}
```

#### `PlanDetailsResult` Class
```csharp
public class PlanDetailsResult
{
    public required string PlanHash { get; init; }
    public required string PlanXml { get; init; }      // Full XML showplan
    public double EstimatedCost { get; init; }         // Optimizer cost
    public double? EstimatedRows { get; init; }
    public bool IsParallel { get; init; }              // Uses multiple threads?
    public int? DegreeOfParallelism { get; init; }
    public long? QueryStorePlanId { get; init; }
    public bool IsForced { get; init; }                // Plan forcing enabled?
    public DateTime? CreatedAtUtc { get; init; }
}
```

---

#### `PlanMetricsDto.cs`
**Path:** `src/DbExecPlanMonitor.Application/DTOs/PlanMetricsDto.cs`

A rich **Data Transfer Object** that unifies DMV and Query Store data into a consistent format.

**Why it exists:**
- DMVs store **totals** (total_worker_time)
- Query Store stores **averages** (avg_cpu_time)
- Units differ (microseconds vs milliseconds)
- This DTO normalizes everything

```csharp
public class PlanMetricsDto
{
    // Identification
    public string QueryHashHex { get; set; }    // "0x1234ABCD..."
    public string PlanHashHex { get; set; }
    public string SqlText { get; set; }
    public string DatabaseName { get; set; }
    
    // CPU Time (all in milliseconds)
    public double AvgCpuTimeMs { get; set; }
    public double TotalCpuTimeMs { get; set; }
    public double MinCpuTimeMs { get; set; }
    public double MaxCpuTimeMs { get; set; }
    
    // Duration, Reads, Writes, Rows, Memory...
    
    // Metadata
    public string DataSource { get; set; }      // "DMV" or "QueryStore"
    public DateTime CollectedAtUtc { get; set; }
    
    // Computed
    public bool HasHighVariability => CpuTimeVariability > 10;
}
```

---

### Infrastructure Layer

#### `ISqlConnectionFactory.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/ISqlConnectionFactory.cs`

Factory interface for creating database connections.

```csharp
public interface ISqlConnectionFactory
{
    // Connect to an instance (default database)
    Task<SqlConnection> CreateConnectionAsync(
        string instanceName,
        CancellationToken cancellationToken = default);

    // Connect to a specific database
    Task<SqlConnection> CreateConnectionForDatabaseAsync(
        string instanceName,
        string databaseName,
        CancellationToken cancellationToken = default);

    // Test connectivity
    Task<bool> TestConnectionAsync(
        string instanceName,
        CancellationToken cancellationToken = default);

    // Get enabled instances from config
    IReadOnlyList<string> GetEnabledInstanceNames();

    // Get config for an instance
    DatabaseInstanceConfig? GetInstanceConfig(string instanceName);
}
```

**Why a factory?**
- Connection strings come from configuration
- Centralizes timeout/encryption settings
- Easy to test connectivity at startup
- Mockable for unit tests

---

#### `DatabaseInstanceConfig.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/Models/DatabaseInstanceConfig.cs`

Configuration class for the **Options pattern** - maps to `appsettings.json`.

```csharp
public class DatabaseInstanceConfig
{
    public string Name { get; set; }               // "Production-SQL01"
    public string ServerName { get; set; }         // "sql01.example.com"
    public int Port { get; set; } = 1433;
    public string? InstanceName { get; set; }      // "SQLEXPRESS"
    public bool UseIntegratedSecurity { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string ApplicationName { get; set; } = "DbExecPlanMonitor";
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int CommandTimeoutSeconds { get; set; } = 120;
    public List<string> DatabaseNames { get; set; } // Databases to monitor
    public int SamplingIntervalSeconds { get; set; } = 60;
    public bool IsEnabled { get; set; } = true;
    public bool PreferQueryStore { get; set; } = true;
    public int TopQueriesCount { get; set; } = 50;
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = false;
    public List<string> Tags { get; set; }         // "production", "critical"
    
    // Builds ADO.NET connection string
    public string BuildConnectionString(string? databaseName = null);
    
    // Validates the configuration
    public List<string> Validate();
}
```

**Example appsettings.json:**
```json
{
  "Monitoring": {
    "DatabaseInstances": [
      {
        "Name": "Production-SQL01",
        "ServerName": "sql01.example.com",
        "DatabaseNames": ["AppDb", "ReportDb"],
        "PreferQueryStore": true,
        "TopQueriesCount": 50
      }
    ]
  }
}
```

---

#### `DmvQueryStatsRecord.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/Models/DmvQueryStatsRecord.cs`

Raw data model mapping directly to **DMV columns**.

```csharp
public class DmvQueryStatsRecord
{
    // Identifiers (binary)
    public byte[] SqlHandle { get; set; }
    public byte[] PlanHandle { get; set; }
    public byte[] QueryHash { get; set; }
    public byte[] QueryPlanHash { get; set; }
    
    // Query text
    public string? QueryText { get; set; }
    public string? ObjectName { get; set; }
    
    // Execution counts
    public long ExecutionCount { get; set; }
    
    // CPU time (microseconds!)
    public long TotalWorkerTime { get; set; }
    public long MinWorkerTime { get; set; }
    public long MaxWorkerTime { get; set; }
    
    // Elapsed time (microseconds!)
    public long TotalElapsedTime { get; set; }
    
    // Logical reads, physical reads, writes, rows...
    
    // Timestamps
    public DateTime? CreationTime { get; set; }
    public DateTime? LastExecutionTime { get; set; }
}
```

**Note:** All times are in **microseconds** from SQL Server. We convert to milliseconds when mapping.

---

#### `QueryStoreRecord.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/Models/QueryStoreRecord.cs`

Raw data model for **Query Store views**.

```csharp
public class QueryStoreRecord
{
    // Query identification (stable IDs)
    public long QueryId { get; set; }
    public long QueryHash { get; set; }
    public string? QueryText { get; set; }
    
    // Plan identification
    public long PlanId { get; set; }
    public long QueryPlanHash { get; set; }
    public string? PlanXml { get; set; }
    public bool IsForced { get; set; }           // Plan forcing!
    
    // Time interval
    public DateTime? IntervalStartTime { get; set; }
    public DateTime? IntervalEndTime { get; set; }
    
    // Stats (Query Store stores AVERAGES)
    public long ExecutionCount { get; set; }
    public double AvgCpuTime { get; set; }       // Not total!
    public double StdevCpuTime { get; set; }     // Standard deviation!
    public double AvgDuration { get; set; }
    public double AvgLogicalReads { get; set; }
    // ...
}
```

**Key differences from DMV:**
- Uses stable IDs (`QueryId`, `PlanId`) instead of volatile handles
- Stores **averages** not totals
- Has **standard deviation** for statistical analysis
- Tracks `IsForced` for plan forcing

---

#### `DmvQueries.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/DmvQueries.cs`

Static class containing **parameterized SQL queries** for DMVs.

```csharp
public static class DmvQueries
{
    public const string TopQueriesByCpu = @"
        SELECT TOP (@TopN)
            qs.plan_handle AS PlanHandle,
            qs.query_hash AS QueryHash,
            qs.execution_count AS ExecutionCount,
            qs.total_worker_time AS TotalCpuTime,
            -- ... more columns
            CASE 
                WHEN qs.statement_end_offset = -1 
                THEN SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, LEN(st.text))
                ELSE SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1, 
                    (qs.statement_end_offset - qs.statement_start_offset) / 2 + 1)
            END AS SqlText
        FROM sys.dm_exec_query_stats qs
        CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
        WHERE st.dbid = DB_ID(@DatabaseName)
        ORDER BY qs.total_worker_time DESC;";
    
    public const string TopQueriesByLogicalReads = @"...";
    public const string TopQueriesByElapsedTime = @"...";
    public const string GetPlanXml = @"...";
    public const string GetStatsByQueryHash = @"...";
}
```

**The CASE statement** extracts the specific SQL statement from a batch (a stored proc might have 10 statements).

---

#### `QueryStoreQueries.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/QueryStoreQueries.cs`

SQL queries for **Query Store views**.

```csharp
public static class QueryStoreQueries
{
    public const string CheckQueryStoreEnabled = @"
        SELECT actual_state_desc, current_storage_size_mb, ...
        FROM sys.database_query_store_options;";
    
    public const string TopQueriesByCpu = @"
        SELECT TOP (@TopN)
            q.query_id, q.query_hash, qt.query_sql_text,
            p.plan_id, p.is_forced_plan,
            SUM(rs.count_executions) AS ExecutionCount,
            AVG(rs.avg_cpu_time) AS AvgCpuTime,
            -- ... more columns
        FROM sys.query_store_query q
        JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
        JOIN sys.query_store_plan p ON q.query_id = p.query_id
        JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
        GROUP BY ...
        ORDER BY SUM(rs.avg_cpu_time * rs.count_executions) DESC;";
    
    public const string FindRegressedQueries = @"...";  // Plan regression detection
    public const string ForcePlan = @"EXEC sp_query_store_force_plan ...";
    public const string UnforcePlan = @"EXEC sp_query_store_unforce_plan ...";
}
```

---

#### `SqlDataReaderBase.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/SqlDataReaderBase.cs`

**Base class** with ADO.NET helper methods (Template Method pattern).

```csharp
public abstract class SqlDataReaderBase
{
    protected readonly ISqlConnectionFactory ConnectionFactory;
    protected readonly ILogger Logger;

    // Execute query and map results
    protected async Task<List<T>> ExecuteQueryAsync<T>(
        string instanceName,
        string? databaseName,
        string sql,
        Dictionary<string, object>? parameters,
        Func<SqlDataReader, T> mapper,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var connection = await ConnectionFactory.CreateConnectionForDatabaseAsync(...);
        await using var command = CreateCommand(connection, sql, parameters, commandTimeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        var results = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(mapper(reader));
        }
        return results;
    }
    
    // Helper methods for safe column reading
    protected static string? GetStringOrNull(SqlDataReader reader, string columnName);
    protected static int? GetInt32OrNull(SqlDataReader reader, string columnName);
    protected static long? GetInt64OrNull(SqlDataReader reader, string columnName);
    protected static DateTime? GetDateTimeOrNull(SqlDataReader reader, string columnName);
    protected static byte[]? GetBytesOrNull(SqlDataReader reader, string columnName);
}
```

**Why this exists:**
- Eliminates duplicate connection/command/reader code
- Handles `DBNull` safely
- Provides consistent error handling (permission denied, object not found)

---

#### `SqlConnectionFactory.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/SqlConnectionFactory.cs`

Implementation of `ISqlConnectionFactory`.

```csharp
public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly MonitoringConfiguration _config;
    private readonly Dictionary<string, DatabaseInstanceConfig> _instanceConfigs;

    public SqlConnectionFactory(IOptions<MonitoringConfiguration> options, ILogger<...> logger)
    {
        _config = options.Value;
        // Index enabled instances for O(1) lookup
        _instanceConfigs = _config.DatabaseInstances
            .Where(i => i.IsEnabled)
            .ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<SqlConnection> CreateConnectionAsync(string instanceName, ...)
    {
        var config = _instanceConfigs[instanceName];
        var connectionString = config.BuildConnectionString();
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
```

---

#### `DmvPlanStatisticsProvider.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/DmvPlanStatisticsProvider.cs`

The **main implementation** of `IPlanStatisticsProvider`.

```csharp
public class DmvPlanStatisticsProvider : SqlDataReaderBase, IPlanStatisticsProvider
{
    public async Task<IReadOnlyList<QueryStatisticsResult>> GetTopQueriesAsync(...)
    {
        // Fallback strategy
        if (config.PreferQueryStore && await IsQueryStoreEnabledAsync(databaseId))
        {
            return await GetTopQueriesFromQueryStoreAsync(...);
        }
        return await GetTopQueriesFromDmvAsync(...);
    }
    
    private async Task<IReadOnlyList<QueryStatisticsResult>> GetTopQueriesFromDmvAsync(...)
    {
        var sql = orderBy switch
        {
            QueryOrderBy.TotalCpuTime => DmvQueries.TopQueriesByCpu,
            QueryOrderBy.TotalLogicalReads => DmvQueries.TopQueriesByLogicalReads,
            _ => DmvQueries.TopQueriesByCpu
        };
        
        return await ExecuteQueryAsync(instanceName, databaseName, sql, parameters,
            MapDmvRecordToQueryResult, commandTimeout: 120, cancellationToken);
    }
    
    private QueryStatisticsResult MapDmvRecordToQueryResult(SqlDataReader reader)
    {
        return new QueryStatisticsResult
        {
            QueryHash = $"0x{GetRequiredInt64(reader, "QueryHash"):X16}",
            QueryText = GetStringOrNull(reader, "SqlText") ?? "",
            TotalCpuTimeMs = GetRequiredInt64(reader, "TotalCpuTime") / 1000.0, // μs → ms
            // ...
        };
    }
}
```

---

#### `DmvPlanDetailsProvider.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/Data/SqlServer/DmvPlanDetailsProvider.cs`

Implementation of `IPlanDetailsProvider` - fetches execution plan XML.

```csharp
public class DmvPlanDetailsProvider : SqlDataReaderBase, IPlanDetailsProvider
{
    public async Task<PlanDetailsResult?> GetPlanByHandleAsync(Guid databaseId, byte[] planHandle, ...)
    {
        var sql = "SELECT query_plan FROM sys.dm_exec_query_plan(@PlanHandle)";
        var results = await ExecuteQueryAsync(...);
        var xml = results.FirstOrDefault();
        return ParsePlanXml(xml);
    }
    
    private PlanDetailsResult? ParsePlanXml(string planXml)
    {
        var doc = XDocument.Parse(planXml);
        var stmtElement = doc.Descendants(ns + "StmtSimple").FirstOrDefault();
        
        return new PlanDetailsResult
        {
            PlanXml = planXml,
            EstimatedCost = double.Parse(stmtElement.Attribute("StatementSubTreeCost")?.Value),
            IsParallel = doc.Descendants(ns + "Parallelism").Any(),
            // ...
        };
    }
}
```

---

#### `ServiceCollectionExtensions.cs`
**Path:** `src/DbExecPlanMonitor.Infrastructure/ServiceCollectionExtensions.cs`

Dependency injection wiring.

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqlServerMonitoring(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind config
        services.Configure<MonitoringConfiguration>(
            configuration.GetSection("Monitoring"));

        // Register services
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IPlanStatisticsProvider, DmvPlanStatisticsProvider>();
        services.AddSingleton<IPlanDetailsProvider, DmvPlanDetailsProvider>();

        return services;
    }
}
```

**Usage in Program.cs:**
```csharp
builder.Services.AddSqlServerMonitoring(builder.Configuration);
```

---

## Data Flow Summary

```
┌─────────────────────────────────────────────────────────────────┐
│  appsettings.json                                               │
│  └── Monitoring.DatabaseInstances[...]                          │
└─────────────────┬───────────────────────────────────────────────┘
                  │ binds to
                  ▼
┌─────────────────────────────────────────────────────────────────┐
│  MonitoringConfiguration / DatabaseInstanceConfig               │
│  (Options pattern - strongly typed config)                      │
└─────────────────┬───────────────────────────────────────────────┘
                  │ injected into
                  ▼
┌─────────────────────────────────────────────────────────────────┐
│  SqlConnectionFactory                                           │
│  (Creates connections using config)                             │
└─────────────────┬───────────────────────────────────────────────┘
                  │ used by
                  ▼
┌─────────────────────────────────────────────────────────────────┐
│  DmvPlanStatisticsProvider / DmvPlanDetailsProvider             │
│  (Uses DmvQueries.cs or QueryStoreQueries.cs templates)         │
│  (Extends SqlDataReaderBase for ADO.NET helpers)                │
└─────────────────┬───────────────────────────────────────────────┘
                  │ executes
                  ▼
┌─────────────────────────────────────────────────────────────────┐
│  SQL Server                                                     │
│  sys.dm_exec_query_stats, sys.query_store_*, etc.              │
└─────────────────────────────────────────────────────────────────┘
```

---

## DMV vs Query Store Comparison

| Feature | DMVs | Query Store |
|---------|------|-------------|
| **Persistence** | Memory only (cleared on restart) | Persisted to disk |
| **History** | Since last restart/cache clear | Configurable retention |
| **Plan Forcing** | Not supported | Supported |
| **Editions** | All | Enterprise/Standard 2016+ |
| **Setup** | None required | Must enable per database |
| **Data Format** | Totals | Averages with StdDev |

---

## SQL Server Permissions Required

```sql
-- Server-level for DMV access
GRANT VIEW SERVER STATE TO [MonitoringUser];

-- Per-database for Query Store
USE [YourDatabase];
GRANT VIEW DATABASE STATE TO [MonitoringUser];
```

---

## Configuration Example

```json
{
  "Monitoring": {
    "DefaultSamplingIntervalSeconds": 60,
    "RetentionDays": 90,
    "DatabaseInstances": [
      {
        "Name": "Production-SQL01",
        "ServerName": "sql01.example.com",
        "Port": 1433,
        "UseIntegratedSecurity": true,
        "DatabaseNames": ["AppDb", "ReportDb"],
        "SamplingIntervalSeconds": 60,
        "IsEnabled": true,
        "PreferQueryStore": true,
        "TopQueriesCount": 50,
        "Tags": ["production", "critical"]
      }
    ]
  }
}
```
