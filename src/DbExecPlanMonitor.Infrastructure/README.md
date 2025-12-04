# DbExecPlanMonitor.Infrastructure

This layer implements the database integration and data access functionality for the Execution Plan Monitor. It provides concrete implementations of the interfaces defined in the Application layer.

## Architecture Overview

```
 DbExecPlanMonitor.Worker  --> hosts background services
       |
       v
 DbExecPlanMonitor.Application  --> interfaces: IPlanStatisticsProvider, IPlanDetailsProvider, repositories, orchestration
       |
       v
 DbExecPlanMonitor.Infrastructure  --> SQL Server implementations
         - Data/SqlServer
             * ISqlConnectionFactory -> SqlConnectionFactory
             * IPlanStatisticsProvider -> DmvPlanStatisticsProvider
             * IPlanDetailsProvider   -> DmvPlanDetailsProvider
             * DmvQueries.cs / QueryStoreQueries.cs
             * Models/
               - DatabaseInstanceConfig
               - DmvQueryStatsRecord
               - QueryStoreRecord
         - Persistence/ (Plan/Baseline/Fingerprint repositories)
         - Messaging/ (Email/Slack/Teams)
```

## Key Components

### Connection Factory (`ISqlConnectionFactory` / `SqlConnectionFactory`)

Centralizes connection management with:
- Configuration-based connection strings
- Support for Windows Authentication and SQL Authentication
- Named instances and custom ports
- Connection testing and validation
- Safe logging (no credentials exposed)

### Plan Statistics Provider (`IPlanStatisticsProvider` / `DmvPlanStatisticsProvider`)

Collects query execution statistics with automatic fallback:
1. **Query Store** (preferred) - If enabled, provides historical data across restarts
2. **DMVs** (fallback) - Always available, real-time cache data

Key methods:
- `GetTopQueriesAsync()` - Top N queries by CPU, duration, or reads
- `GetQueryStatisticsAsync()` - Stats for a specific query hash
- `GetPlanStatisticsAsync()` - All plans for a query
- `IsQueryStoreEnabledAsync()` - Check Query Store availability

### Plan Details Provider (`IPlanDetailsProvider` / `DmvPlanDetailsProvider`)

Retrieves execution plan XML with:
- Plan cache access via plan handles
- Query Store access via plan IDs
- XML parsing for key metrics (cost, parallelism, estimated rows)

### SQL Query Templates

**`DmvQueries.cs`** - Parameterized queries for Dynamic Management Views:
- `TopQueriesByCpu` - Rank by total worker time
- `TopQueriesByLogicalReads` - Rank by IO
- `TopQueriesByElapsedTime` - Rank by duration
- `GetPlanXml` - Fetch execution plan XML
- `GetStatsByQueryHash` - Stats for specific query

**`QueryStoreQueries.cs`** - Queries for Query Store views:
- `TopQueriesByCpu` / `TopQueriesByLogicalReads`
- `GetQueryByHash` - Historical stats per interval
- `FindRegressedQueries` - Identify plan regressions
- `ForcePlan` / `UnforcePlan` - Plan forcing operations

### Data Models

**`DatabaseInstanceConfig`** - Configuration for monitored instances:
```csharp
public class DatabaseInstanceConfig
{
    public string Name { get; set; }
    public string ServerName { get; set; }
    public int Port { get; set; } = 1433;
    public bool UseIntegratedSecurity { get; set; } = true;
    public List<string> DatabaseNames { get; set; }
    public int SamplingIntervalSeconds { get; set; } = 60;
    public bool PreferQueryStore { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
}
```

**`DmvQueryStatsRecord`** - Raw data from `sys.dm_exec_query_stats`  
**`QueryStoreRecord`** - Raw data from Query Store views

## Configuration

Add to `appsettings.json`:

```json
{
  "Monitoring": {
    "DefaultSamplingIntervalSeconds": 60,
    "RetentionDays": 90,
    "LogSqlQueries": false,
    "MaxConcurrentConnections": 10,
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

## Dependency Injection

Register services in `Program.cs`:

```csharp
using DbExecPlanMonitor.Infrastructure;

builder.Services.AddSqlServerMonitoring(builder.Configuration);
builder.Services.AddMonitoringValidation(); // Optional: validates on startup
```

## SQL Server Permissions

The monitoring account needs:
```sql
-- Minimum permissions for DMV access
GRANT VIEW SERVER STATE TO [MonitoringUser];

-- Per-database for Query Store access
USE [YourDatabase];
GRANT VIEW DATABASE STATE TO [MonitoringUser];
```

## DMV vs Query Store

| Feature   | DMVs                        | Query Store               |
|-----------|----------------------------|---------------------------|
| Persistence | Memory only | Persisted to disk |
| History     | Since last restart/cache clear | Configurable retention |
| Plan Forcing | Not supported | Supported |
| Editions    | All | Enterprise/Standard 2016+ |
| Setup       | None required | Must enable per database |

## Error Handling

The providers handle common SQL Server errors:
- Error 208 - object not found (Query Store not available)
- Error 229 - permission denied (escalates with helpful message)
- Connection timeouts with retry logic
- Plan cache eviction (graceful null returns)

## Testing

Unit tests mock `ISqlConnectionFactory` to test mapping logic.  
Integration tests require a SQL Server instance with sample databases.

See `DbExecPlanMonitor.Infrastructure.Tests` for examples.
