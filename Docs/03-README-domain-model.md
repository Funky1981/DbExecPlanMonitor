# 03-README: Domain Model and Ubiquitous Language

## 📚 Summary

This document defines the **domain language** used throughout the project. The goal is that DBAs, developers, and the code all use the same concepts and terminology.

---

## 🗣️ Core Concepts (Ubiquitous Language)

These are the terms we use consistently across documentation, conversations, and code:

### Database Instance
```
A monitored SQL Server instance (or cluster).
Identified by: server name, port, environment tags.
Example: "PROD-SQL-01", port 1433, tags: ["production", "us-east"]
```

### Database
```
A specific database within an instance.
Example: "AdventureWorks" on PROD-SQL-01
```

### Query Fingerprint
```
A logical representation of "the same query":
- Normalised SQL text (whitespace insensitive, literals replaced)
- Stable identifier for grouping metrics across executions
- Two queries with different literal values but same structure = same fingerprint

Example:
  Query A: "SELECT * FROM Users WHERE Id = 1"
  Query B: "SELECT * FROM Users WHERE Id = 2"
  Both have fingerprint: "SELECT * FROM Users WHERE Id = @p1"
```

### Execution Plan Snapshot
```
A captured plan for a query at a point in time:
- Plan ID (from DB engine if available)
- XML/JSON representation
- Plan hash (for comparison)
- Capture time

This allows comparing plans to detect when SQL Server chose a different strategy.
```

### Plan Metrics
```
Aggregated runtime statistics for a query + plan:
- CPU time (total/avg)
- Duration (total/avg)
- Logical reads/writes
- Execution count
- Memory grants, spills, etc.
```

### Baseline
```
A reference range of "normal" performance for a query:
- Typically median / P95 metrics over a stable period
- Used to detect regressions
- "Last week, this query averaged 50ms; today it's 500ms"
```

### Regression Event
```
A detected deterioration vs baseline:
- P95 duration increased by X% or more
- CPU per execution increased by X%
- Different plan hash used (plan change)
```

### Hotspot
```
A plan or query currently consuming disproportionate resources.
Example: "Top 10 queries by CPU in the last 15 minutes"
```

### Remediation Suggestion
```
A suggested action to improve performance:
- Update statistics
- Rebuild or add index
- Force a known good plan (Query Store)
- Change MAXDOP / query option
```

---

## 📊 Domain Entities

These concepts become classes in the `DbExecPlanMonitor.Domain` project:

| Entity | Purpose |
|--------|---------|
| `DatabaseInstance` | Represents a SQL Server instance being monitored |
| `MonitoredDatabase` | A specific database within an instance |
| `QueryFingerprint` | Normalized query identifier |
| `ExecutionPlanSnapshot` | Captured plan XML at a point in time |
| `PlanMetricSample` | Runtime statistics for a query execution |
| `PlanBaseline` | Reference "normal" metrics for comparison |
| `RegressionDetectionRule` | Configurable rules for detecting regressions |
| `RegressionEvent` | A detected performance regression |
| `HotspotDetectionRule` | Configurable rules for detecting hotspots |
| `Hotspot` | A currently hot (resource-heavy) query |
| `RemediationSuggestion` | A suggested fix for a problem |

---

## 🧩 Aggregate Roots

In Domain-Driven Design, aggregate roots are the entry points for accessing related entities:

### MonitoredDatabase Aggregate
```
MonitoredDatabase (Root)
├── Query fingerprints belonging to this database
├── Baselines for queries in this database
└── Detection rules applied to this database
```

### QueryFingerprint Aggregate
```
QueryFingerprint (Root)
├── Plan snapshots for this query
├── Metric samples over time
├── Baseline for this query
└── Regression events detected
```

---

## ⚙️ Domain Services

Pure business logic that doesn't belong to a single entity:

| Service Interface | Responsibility |
|-------------------|----------------|
| `IRegressionDetector` | Compares baseline + recent metrics → List of `RegressionEvent` |
| `IHotspotDetector` | Analyzes recent metrics across queries → List of `Hotspot` |
| `IRemediationAdvisor` | Given regression/hotspot + plan details → Suggested fixes |

---

## 📁 Files Implemented

### Current Status

The Domain layer is structured but entities are mostly **planned, not implemented yet**:

```
src/DbExecPlanMonitor.Domain/
├── DbExecPlanMonitor.Domain.csproj
├── Entities/
│   └── .gitkeep              # Placeholder (entities to be created)
├── ValueObjects/
│   └── TimeWindow.cs         # ✅ Implemented
├── Enums/
│   └── .gitkeep              # Placeholder
├── Interfaces/
│   └── .gitkeep              # Placeholder
└── Services/
    └── .gitkeep              # Placeholder
```

---

### ValueObjects/TimeWindow.cs

The one value object created so far - used for time-based filtering:

```csharp
namespace DbExecPlanMonitor.Domain.ValueObjects;

/// <summary>
/// Immutable value object representing a time window for sampling.
/// Used for DMV queries and Query Store time-based filtering.
/// </summary>
public readonly struct TimeWindow : IEquatable<TimeWindow>
{
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }

    public TimeWindow(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc < startUtc)
            throw new ArgumentException("EndUtc must be >= StartUtc");

        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    // Factory methods for common time windows
    public static TimeWindow LastHours(int hours) => new(
        DateTime.UtcNow.AddHours(-hours),
        DateTime.UtcNow);

    public static TimeWindow LastMinutes(int minutes) => new(
        DateTime.UtcNow.AddMinutes(-minutes),
        DateTime.UtcNow);

    public TimeSpan Duration => EndUtc - StartUtc;

    public bool Contains(DateTime utcTime) => 
        utcTime >= StartUtc && utcTime <= EndUtc;

    // Value object equality
    public bool Equals(TimeWindow other) => 
        StartUtc == other.StartUtc && EndUtc == other.EndUtc;
    
    public override bool Equals(object? obj) => 
        obj is TimeWindow other && Equals(other);
    
    public override int GetHashCode() => 
        HashCode.Combine(StartUtc, EndUtc);
    
    public static bool operator ==(TimeWindow left, TimeWindow right) => 
        left.Equals(right);
    
    public static bool operator !=(TimeWindow left, TimeWindow right) => 
        !left.Equals(right);
}
```

**Why `readonly struct`?**
- Value semantics (compared by value, not reference)
- Immutable (thread-safe, no side effects)
- Stack-allocated (better performance for small types)
- No identity - two TimeWindows with same dates are equal

---

## 🗺️ Entities to Be Created

These are the domain entities that will be implemented in future docs:

```csharp
// Entities/DatabaseInstance.cs
public class DatabaseInstance
{
    public Guid Id { get; }
    public string ServerName { get; }
    public int Port { get; }
    public string[] Tags { get; }
    public IReadOnlyList<MonitoredDatabase> Databases { get; }
}

// Entities/QueryFingerprint.cs
public class QueryFingerprint
{
    public Guid Id { get; }
    public string NormalizedSqlHash { get; }
    public string SampleSqlText { get; }
    public DateTime FirstSeenUtc { get; }
    public IReadOnlyList<ExecutionPlanSnapshot> Plans { get; }
}

// Entities/ExecutionPlanSnapshot.cs
public class ExecutionPlanSnapshot
{
    public Guid Id { get; }
    public string PlanXml { get; }
    public string PlanHash { get; }
    public DateTime CapturedAtUtc { get; }
    public bool IsForced { get; }
}

// Entities/PlanMetricSample.cs
public class PlanMetricSample
{
    public Guid Id { get; }
    public Guid QueryFingerprintId { get; }
    public Guid PlanSnapshotId { get; }
    public DateTime SampledAtUtc { get; }
    public long ExecutionCount { get; }
    public long TotalCpuTimeUs { get; }
    public long TotalDurationUs { get; }
    public long TotalLogicalReads { get; }
}

// Entities/RegressionEvent.cs
public class RegressionEvent
{
    public Guid Id { get; }
    public Guid QueryFingerprintId { get; }
    public DateTime DetectedAtUtc { get; }
    public string MetricName { get; }  // "Duration", "CpuTime", etc.
    public decimal BaselineValue { get; }
    public decimal CurrentValue { get; }
    public decimal ChangePercent { get; }
}
```

---

## ➡️ Next Steps

With the domain language established, proceed to:
- **[04-database-integration-and-metadata-model.md](04-database-integration-and-metadata-model.md)** - Connect to SQL Server and read DMVs

**Note**: The full domain entities will be implemented incrementally as we build out the analysis and storage capabilities in later documents.
