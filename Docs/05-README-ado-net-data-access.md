# 05-README: ADO.NET Data Access Layer

## 📚 Summary

This document implements the **persistence layer** for storing our own monitoring data. While Doc 04 focused on *reading* from SQL Server's DMVs and Query Store, Doc 05 focuses on *writing* our collected metrics, baselines, and regression events to our own storage.

---

## 🏗️ Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          Application Layer                                │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────┐ ┌────────────┐ │
│  │ IQueryFinger-  │ │ IPlanMetrics-  │ │  IBaseline-    │ │IRegression-│ │
│  │ printRepository│ │   Repository   │ │   Repository   │ │EventRepo   │ │
│  └───────┬────────┘ └───────┬────────┘ └───────┬────────┘ └─────┬──────┘ │
└──────────┼──────────────────┼──────────────────┼────────────────┼────────┘
           │                  │                  │                │
           ▼                  ▼                  ▼                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                        Infrastructure Layer                               │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────┐ ┌────────────┐ │
│  │ SqlQueryFinger-│ │ SqlPlanMetrics-│ │  SqlBaseline-  │ │SqlRegress- │ │
│  │ printRepository│ │   Repository   │ │   Repository   │ │ionEventRepo│ │
│  └───────┬────────┘ └───────┬────────┘ └───────┬────────┘ └─────┬──────┘ │
│          └──────────────────┼──────────────────┼────────────────┘        │
│                             ▼                                             │
│                    ┌─────────────────┐                                   │
│                    │  RepositoryBase │                                   │
│                    └────────┬────────┘                                   │
│                             ▼                                             │
│                    ┌─────────────────┐                                   │
│                    │  SQL Server     │                                   │
│                    │ (monitoring.*)  │                                   │
│                    └─────────────────┘                                   │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## 📁 Files Created

### Application Layer - Interfaces

| File | Purpose |
|------|---------|
| `IQueryFingerprintRepository.cs` | Contract for managing query identities |
| `IPlanMetricsRepository.cs` | Contract for storing performance samples |
| `IBaselineRepository.cs` | Contract for managing performance baselines |
| `IRegressionEventRepository.cs` | Contract for tracking detected regressions |

### Infrastructure Layer - Implementations

| File | Purpose |
|------|---------|
| `RepositoryBase.cs` | Common ADO.NET patterns and helpers |
| `SqlQueryFingerprintRepository.cs` | ADO.NET fingerprint implementation |
| `SqlPlanMetricsRepository.cs` | ADO.NET metrics implementation |
| `SqlBaselineRepository.cs` | ADO.NET baseline implementation |
| `SqlRegressionEventRepository.cs` | ADO.NET regression implementation |
| `Scripts/001_CreateMonitoringSchema.sql` | Database schema creation |

---

## 📋 Detailed File Walkthrough

### 1. `IQueryFingerprintRepository.cs`

**Purpose**: Manages query fingerprints - normalized query identities for grouping executions.

```csharp
public interface IQueryFingerprintRepository
{
    // Upsert pattern - gets existing or creates new
    Task<Guid> GetOrCreateFingerprintAsync(
        byte[] queryHash,
        string queryTextSample,
        string databaseName,
        CancellationToken ct = default);
    
    // Lookup methods
    Task<QueryFingerprintRecord?> GetByIdAsync(Guid fingerprintId, CancellationToken ct = default);
    Task<QueryFingerprintRecord?> GetByHashAsync(byte[] queryHash, CancellationToken ct = default);
    
    // Query methods
    Task<IReadOnlyList<QueryFingerprintRecord>> GetByDatabaseAsync(string databaseName, ...);
    Task<IReadOnlyList<QueryFingerprintRecord>> GetActiveInWindowAsync(TimeWindow window, ...);
}
```

**Key Pattern - Upsert**: `GetOrCreateFingerprintAsync` atomically gets an existing fingerprint or creates a new one. This handles the common case where we don't know if we've seen a query before.

---

### 2. `IPlanMetricsRepository.cs`

**Purpose**: Stores point-in-time performance samples collected from SQL Server.

```csharp
public interface IPlanMetricsRepository
{
    // Batch insert for efficiency
    Task SaveSamplesAsync(string instanceName, IEnumerable<PlanMetricSampleRecord> samples, ...);
    
    // Query by various dimensions
    Task<IReadOnlyList<PlanMetricSampleRecord>> GetSamplesForFingerprintAsync(Guid fingerprintId, TimeWindow window, ...);
    Task<IReadOnlyList<PlanMetricSampleRecord>> GetSamplesForInstanceAsync(string instanceName, TimeWindow window, ...);
    
    // Aggregation for baseline calculation
    Task<AggregatedMetrics?> GetAggregatedMetricsAsync(Guid fingerprintId, TimeWindow window, ...);
    
    // Retention management
    Task<int> PurgeSamplesOlderThanAsync(DateTime olderThan, ...);
}
```

**Key Record**: `PlanMetricSampleRecord` contains 25+ fields capturing CPU, duration, I/O, and memory metrics.

---

### 3. `IBaselineRepository.cs`

**Purpose**: Manages performance baselines used for regression detection.

```csharp
public interface IBaselineRepository
{
    // Only one active baseline per fingerprint
    Task SaveBaselineAsync(BaselineRecord baseline, ...);
    Task<BaselineRecord?> GetBaselineAsync(Guid fingerprintId, ...);
    
    // Maintenance operations
    Task<IReadOnlyList<BaselineRecord>> GetStaleBaselinesAsync(TimeSpan maxAge, ...);
    Task SaveBaselinesAsync(IEnumerable<BaselineRecord> baselines, ...);  // Batch update
}
```

**Key Concept**: `BaselineRecord` contains median, P95, P99, and standard deviation for each metric. The `IsActive` flag ensures only one baseline is current per query.

---

### 4. `IRegressionEventRepository.cs`

**Purpose**: Tracks detected performance regressions with full workflow support.

```csharp
public interface IRegressionEventRepository
{
    // Save detected regressions
    Task SaveEventAsync(RegressionEventRecord regressionEvent, ...);
    
    // Query for alerting
    Task<IReadOnlyList<RegressionEventRecord>> GetUnacknowledgedEventsAsync(...);
    Task<IReadOnlyList<RegressionEventRecord>> GetEventsBySeverityAsync(RegressionSeverity minSeverity, ...);
    
    // Workflow operations
    Task AcknowledgeEventAsync(Guid eventId, string acknowledgedBy, string? notes, ...);
    Task ResolveEventAsync(Guid eventId, string resolvedBy, string? resolutionNotes, ...);
    
    // Reporting
    Task<RegressionSummary> GetSummaryAsync(TimeWindow window, ...);
}
```

**Key Enums**:
```csharp
public enum RegressionType { MetricRegression, PlanChange, PlanChangeWithRegression }
public enum RegressionSeverity { Low, Medium, High, Critical }
public enum RegressionStatus { New, Acknowledged, Resolved, Dismissed }
```

---

### 5. `RepositoryBase.cs`

**Purpose**: Base class with common ADO.NET patterns.

```csharp
public abstract class RepositoryBase
{
    // Connection management
    protected async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct = default);
    
    // Query patterns
    protected async Task<int> ExecuteNonQueryAsync(string sql, Action<SqlParameterCollection>? configureParameters, ...);
    protected async Task<T?> ExecuteScalarAsync<T>(string sql, ...);
    protected async Task<List<T>> ExecuteQueryAsync<T>(string sql, Func<SqlDataReader, T> mapper, ...);
    
    // Transaction support
    protected async Task ExecuteInTransactionAsync(Func<SqlConnection, SqlTransaction, Task> action, ...);
    protected async Task ExecuteBatchInsertAsync<T>(IEnumerable<T> items, string insertSql, ...);
    
    // Type-safe parameter helpers
    protected static void AddGuidParameter(SqlParameterCollection p, string name, Guid value);
    protected static void AddDateTimeParameter(SqlParameterCollection p, string name, DateTime value);
    protected static void AddBigIntParameter(SqlParameterCollection p, string name, long? value);
    // ... and more
}
```

**Key Patterns**:
- **Delegate-based parameters**: `Action<SqlParameterCollection>` allows flexible parameter configuration
- **Transaction wrapper**: Automatic commit/rollback with exception handling
- **Null handling**: All helpers properly convert C# `null` to `DBNull.Value`

---

### 6. `001_CreateMonitoringSchema.sql`

**Purpose**: Creates the database schema for our monitoring data.

```sql
-- Schema
CREATE SCHEMA monitoring;

-- Tables
monitoring.QueryFingerprint    -- Query identities (hash, sample text, timestamps)
monitoring.PlanMetricSample    -- Point-in-time performance metrics
monitoring.Baseline            -- Calculated performance baselines
monitoring.RegressionEvent     -- Detected regressions with workflow

-- Stored Procedures
monitoring.usp_GetOrCreateFingerprint  -- Atomic upsert with race condition handling
monitoring.usp_PurgeSamples            -- Batch deletion for retention
monitoring.usp_GetRegressionSummary    -- Aggregated reporting
```

**Key SQL Features**:
- `NEWSEQUENTIALID()` for clustered index performance
- Filtered indexes for hot paths (unacknowledged events)
- Filtered unique constraint (one active baseline per fingerprint)
- Batch deletion to avoid lock contention

---

## 🔧 Configuration

### appsettings.json

```json
{
  "MonitoringStorage": {
    "ConnectionString": "Server=.;Database=DbExecPlanMonitor;Integrated Security=true;TrustServerCertificate=true",
    "CommandTimeoutSeconds": 60,
    "RetentionDays": 90
  }
}
```

### Dependency Injection

```csharp
// In Program.cs
builder.Services.AddMonitoringStorage(builder.Configuration);

// This registers:
// - IQueryFingerprintRepository → SqlQueryFingerprintRepository
// - IPlanMetricsRepository → SqlPlanMetricsRepository
// - IBaselineRepository → SqlBaselineRepository
// - IRegressionEventRepository → SqlRegressionEventRepository
```

---

## 🔄 Data Flow

```
1. Monitoring Cycle Starts
   │
   ▼
2. Collect from SQL Server DMVs/Query Store
   │
   ▼
3. For each query result:
   │
   ├─► GetOrCreateFingerprint (IQueryFingerprintRepository)
   │   Returns stable ID for this query
   │
   └─► SaveSamples (IPlanMetricsRepository)
       Stores current metrics with fingerprint ID
   │
   ▼
4. Analysis Engine runs:
   │
   ├─► GetBaseline (IBaselineRepository)
   │   Retrieves reference "normal" metrics
   │
   ├─► Compare current vs baseline
   │
   └─► If regression detected:
       SaveEvent (IRegressionEventRepository)
   │
   ▼
5. Alerting checks:
   │
   └─► GetUnacknowledgedEvents (IRegressionEventRepository)
       Sends notifications for new regressions
```

---

## 📊 Database Schema Diagram

```
┌─────────────────────────┐
│   QueryFingerprint      │
├─────────────────────────┤
│ Id (PK, GUID)           │
│ QueryHash (UQ, BINARY)  │
│ QueryTextSample         │
│ DatabaseName            │
│ FirstSeenUtc            │
│ LastSeenUtc             │
└───────────┬─────────────┘
            │ 1
            │
            │ *
┌───────────▼─────────────┐      ┌─────────────────────────┐
│   PlanMetricSample      │      │       Baseline          │
├─────────────────────────┤      ├─────────────────────────┤
│ Id (PK)                 │      │ Id (PK)                 │
│ FingerprintId (FK)      │      │ FingerprintId (FK, UQ)  │
│ InstanceName            │      │ MedianDurationUs        │
│ DatabaseName            │      │ P95DurationUs           │
│ SampledAtUtc            │      │ P99DurationUs           │
│ ExecutionCount          │      │ MedianCpuTimeUs         │
│ AvgCpuTimeUs            │      │ P95CpuTimeUs            │
│ AvgDurationUs           │      │ ExpectedPlanHash        │
│ AvgLogicalReads         │      │ IsActive                │
│ ...                     │      │ ...                     │
└─────────────────────────┘      └─────────────────────────┘

┌─────────────────────────┐
│   RegressionEvent       │
├─────────────────────────┤
│ Id (PK)                 │
│ FingerprintId (FK)      │
│ RegressionType          │
│ MetricName              │
│ BaselineValue           │
│ CurrentValue            │
│ ChangePercent           │
│ Severity                │
│ Status                  │
│ AcknowledgedBy          │
│ ResolvedBy              │
│ ...                     │
└─────────────────────────┘
```

---

## ✅ Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Separate storage database** | Avoids polluting monitored databases with our data |
| **GUID primary keys** | Enables distributed scenarios, no identity conflicts |
| **Soft delete for baselines** | Preserves history, simplifies "one active" constraint |
| **Batch operations** | Performance for high-volume sample insertion |
| **Stored procedures for complex ops** | Atomic upsert, efficient purge |
| **Severity enum stored as TINYINT** | Efficient storage and comparison |

---

## ➡️ Next Steps

With persistence in place, proceed to:
- **[06-plan-collection-and-sampling-engine.md](06-plan-collection-and-sampling-engine.md)** - Orchestrate the collection and storage workflow
