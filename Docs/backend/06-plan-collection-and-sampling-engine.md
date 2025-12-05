# 06 – Plan Collection and Sampling Engine

This file describes **how the service collects execution plan metrics** from SQL Server and stores them in our internal model.

## Goals

- Periodically sample:
  - Top N queries by resource usage.
  - Queries that recently changed plan.
- Normalise SQL into query fingerprints.
- Store samples for historical analysis.

## Key Use Case

**Use Case: CollectTopPlans**

- Input:
  - `DatabaseInstanceId`
  - `MonitoredDatabase`
  - `TopN` (e.g., 50)
  - `TimeWindow` (e.g., last 15 minutes)
- Output:
  - Plan metrics saved via `IPlanMetricsRepository`.

### Application Service Sketch

```csharp
public class PlanCollectionService : IPlanCollectionService
{
    private readonly IPlanStatisticsProvider _statsProvider;
    private readonly IPlanMetricsRepository _metricsRepository;

    public async Task CollectTopPlansAsync(DatabaseInstanceId instanceId, MonitoredDatabase db, CancellationToken ct)
    {
        var samples = await _statsProvider
            .GetTopPlansAsync(instanceId, db.Name, db.CollectionConfig.TopN, db.CollectionConfig.Window, ct);

        await _metricsRepository.SaveSamplesAsync(instanceId, samples, ct);
    }
}
```

> This is conceptual; actual signature may vary.

## Fingerprinting Strategy

To group “the same query,” we need a **query fingerprint**:

- Remove literal values (replace with placeholders).
- Normalise whitespace.
- Optionally:
  - Remove comments.
  - Lowercase keywords.

For v1, this can be a simple function implemented in Application or Domain as a **pure function**:

```csharp
public static QueryFingerprint FromSqlText(string sqlText)
{
    // basic normalisation, then hash
}
```

The raw SQL text is provided by the Infra layer via DMVs.

## Sampling Configuration

Each monitored database can have:

- `TopN` (e.g., 50)
- `Window` (e.g., last 15 minutes)
- `MinExecutionCount`
- `MinTotalCpuMs` or `MinTotalDurationMs`

These settings live in configuration (see later files) and are passed to the collection service.

## Multi-Instance / Multi-Database Loop

Scheduling layer will:

1. Enumerate configured instances.
2. For each instance, enumerate monitored databases.
3. Invoke `CollectTopPlansAsync` for each.

Failures should be:

- Logged per instance/database.
- Not block other instances/databases.

Next: see `07-plan-analysis-and-regression-detection.md` for how collected samples are turned into insights.
