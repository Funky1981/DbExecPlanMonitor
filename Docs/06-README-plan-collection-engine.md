# Doc 06 - Plan Collection and Sampling Engine

## Overview

This document implements the **collection orchestration layer** - the component that coordinates fetching query statistics from SQL Server and storing them in our monitoring database.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          MonitoringWorker                                    │
│                         (Background Service)                                │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      PlanCollectionOrchestrator                             │
│                          (Coordination Hub)                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│  • Loops through configured instances                                       │
│  • Loops through databases per instance                                     │
│  • Handles failures gracefully (isolation)                                  │
│  • Aggregates results into summary objects                                  │
└─────────────────────────────────────────────────────────────────────────────┘
          │                         │                         │
          ▼                         ▼                         ▼
┌─────────────────┐    ┌─────────────────────┐    ┌─────────────────────┐
│ IPlanStatistics │    │ IQueryFingerprint   │    │ Repositories        │
│    Provider     │    │     Service         │    │                     │
├─────────────────┤    ├─────────────────────┤    ├─────────────────────┤
│ • Query DMVs    │    │ • Normalize SQL     │    │ • Fingerprint       │
│ • Query Store   │    │ • Compute hashes    │    │ • Metrics           │
│ • Returns stats │    │ • Use server hash   │    │ • Baselines         │
└─────────────────┘    └─────────────────────┘    └─────────────────────┘
```

## Files Created/Modified

### Application Layer

| File | Purpose |
|------|---------|
| `Interfaces/IQueryFingerprintService.cs` | Contract for SQL text normalization |
| `Interfaces/IPlanCollectionOrchestrator.cs` | Contract + result classes for orchestration |
| `Interfaces/IPlanStatisticsProvider.cs` | Extended with connection string overload |
| `Services/QueryFingerprintService.cs` | SQL normalization with regex patterns |
| `Orchestrators/PlanCollectionOptions.cs` | Configuration models for collection |
| `Orchestrators/PlanCollectionOrchestrator.cs` | Main orchestration implementation |

### Infrastructure Layer

| File | Purpose |
|------|---------|
| `Data/SqlServer/DmvPlanStatisticsProvider.cs` | Extended with direct connection method |
| `Persistence/SqlQueryFingerprintRepository.cs` | Added UpsertAsync implementation |
| `Persistence/Scripts/001_CreateMonitoringSchema.sql` | Added usp_UpsertQueryFingerprint |
| `ServiceCollectionExtensions.cs` | Added AddPlanCollection() method |

### Worker Layer

| File | Purpose |
|------|---------|
| `MonitoringWorker.cs` | Updated to use orchestrator |
| `Program.cs` | Added AddPlanCollection() registration |
| `appsettings.json` | Added PlanCollection and MonitoringInstances config |

## Key Concepts

### 1. Query Fingerprinting

**Problem**: The same logical query can appear with different literal values:
```sql
SELECT * FROM Users WHERE Id = 1
SELECT * FROM Users WHERE Id = 42
SELECT * FROM Users WHERE Id = 999
```

**Solution**: Normalize SQL by replacing literals with placeholders:
```sql
SELECT * FROM Users WHERE Id = #
```

The `QueryFingerprintService` handles this via:
1. Collapsing whitespace
2. Replacing string/number/GUID/datetime literals with `#`
3. Normalizing keyword case
4. Computing a hash of the normalized text

### 2. Configuration Cascade

Settings flow from global → instance → database with overrides:

```json
{
  "PlanCollection": {
    "TopNQueries": 50,        // Global default
    ...
  },
  "MonitoringInstances": {
    "Instances": [
      {
        "Name": "Prod1",
        "TopNQueries": 100,    // Instance override
        "Databases": [
          {
            "Name": "HighVolume",
            "TopNQueries": 200  // Database override
          }
        ]
      }
    ]
  }
}
```

In code:
```csharp
var effectiveTopN = dbConfig.TopNQueries 
    ?? instanceConfig.TopNQueries 
    ?? _options.CurrentValue.TopNQueries;
```

### 3. Failure Isolation

One database failing shouldn't stop collection from others:

```csharp
catch (Exception ex) when (_options.CurrentValue.ContinueOnDatabaseError)
{
    _logger.LogError(ex, "Error collecting from {Database}, continuing...", dbConfig.Name);
    
    databaseResults.Add(new DatabaseCollectionResult
    {
        Error = ex.Message,
        // ... still return a result object
    });
}
```

### 4. Result Aggregation

Results bubble up through layers:
- `DatabaseCollectionResult` → queries collected from one DB
- `InstanceCollectionResult` → aggregates all databases on one instance
- `CollectionRunSummary` → aggregates all instances

```csharp
public int TotalQueriesCollected => InstanceResults.Sum(r => r.TotalQueriesCollected);
public bool IsFullySuccessful => FailedInstances == 0;
```

### 5. Query Store vs DMV Fallback

The provider prefers Query Store when available:

```csharp
var useQueryStore = await IsQueryStoreEnabledDirectAsync(connection, ct);

if (useQueryStore)
    return await GetTopQueriesFromQueryStoreDirectAsync(...);

return await GetTopQueriesFromDmvDirectAsync(...);
```

**Query Store advantages**: Historical data, persists across restarts, plan forcing support
**DMV advantages**: Always available, real-time cache data

## Configuration Reference

### PlanCollection Section

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `CollectionInterval` | TimeSpan | 5 min | Time between collection runs |
| `TopNQueries` | int | 50 | Max queries per database |
| `LookbackWindow` | TimeSpan | 15 min | Time window for stats |
| `MinimumExecutionCount` | int | 2 | Min executions to be collected |
| `MinimumElapsedTimeMs` | double | 100 | Min elapsed time (ms) |
| `PreferQueryStore` | bool | true | Use Query Store when available |
| `CollectionTimeout` | TimeSpan | 2 min | Max time per operation |
| `ContinueOnDatabaseError` | bool | true | Keep going on DB failures |
| `ContinueOnInstanceError` | bool | true | Keep going on instance failures |

### MonitoringInstances Section

```json
{
  "MonitoringInstances": {
    "Instances": [
      {
        "Name": "string",               // Friendly name
        "ConnectionString": "string",   // SQL Server connection
        "Enabled": true,                // Toggle monitoring
        "Databases": [                  // Explicit list (optional)
          { "Name": "string", "Enabled": true }
        ],
        "ExcludeDatabasePatterns": [],  // Regex patterns to exclude
        "TopNQueries": null,            // Override global
        "LookbackWindow": null          // Override global
      }
    ]
  }
}
```

## Collection Workflow

```
1. MonitoringWorker timer fires
   ↓
2. CollectAllAsync() called
   ↓
3. Loop enabled instances
   ↓
4. For each instance: CollectInstanceInternalAsync()
   ↓
5. Get databases to collect (explicit or auto-discover)
   ↓
6. For each database: CollectDatabaseInternalAsync()
   ↓
7. Resolve effective configuration (TopN, Lookback, etc.)
   ↓
8. Call IPlanStatisticsProvider.GetTopQueriesByElapsedTimeAsync()
   ↓
9. For each query:
   a. Create fingerprint via IQueryFingerprintService
   b. Upsert fingerprint via IQueryFingerprintRepository
   c. Save metrics via IPlanMetricsRepository
   ↓
10. Aggregate results, return summary
```

## SQL Normalization Examples

| Original | Normalized |
|----------|------------|
| `WHERE Id = 42` | `WHERE Id = #` |
| `WHERE Name = 'John'` | `WHERE Name = '#'` |
| `WHERE Created > '2024-01-15'` | `WHERE Created > '#DATE#'` |
| `WHERE Id = '550e8400-e29b-41d4-a716-446655440000'` | `WHERE Id = '#GUID#'` |
| `SELECT   *    FROM Users` | `SELECT * FROM Users` |
| `select * from users` | `SELECT * FROM Users` |

## Testing Approach

1. **Unit tests for QueryFingerprintService**:
   - Test normalization of various SQL patterns
   - Test hash consistency
   - Test edge cases (empty, null, very long)

2. **Integration tests for Orchestrator**:
   - Mock providers/repositories
   - Verify correct aggregation
   - Test failure isolation

3. **End-to-end with LocalDB**:
   - Create test queries
   - Run collection
   - Verify stored data

## Related Documentation

- [Doc 04 - Database Integration](./04-README-database-integration.md) - DMV/Query Store providers
- [Doc 05 - ADO.NET Data Access](./05-README-ado-net-data-access.md) - Repository implementations
- [Doc 07 - Plan Analysis](./07-plan-analysis-and-regression-detection.md) - Next phase (analysis)
