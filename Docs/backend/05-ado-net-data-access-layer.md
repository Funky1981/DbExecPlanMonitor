# 05 – ADO.NET Data Access Layer

This file defines **how** we use ADO.NET in a clean, testable way.

## Design Principles

- Application & Domain layers see **interfaces**, not ADO.NET types.
- All direct usage of:
  - `SqlConnection`
  - `SqlCommand`
  - `SqlDataReader`
  - `SqlParameter`

  lives in **Infrastructure**.

- Data access is **read-heavy** (monitoring), with optional write paths for:
  - Persisting our own historical metrics.
  - Logging regression events.
  - Storing configuration in DB (optional).

## Core Interfaces (Application Layer)

```csharp
public interface IPlanMetricsRepository
{
    Task SaveSamplesAsync(DatabaseInstanceId instanceId, IEnumerable<PlanMetricSample> samples, CancellationToken ct = default);
    Task<IReadOnlyList<PlanMetricSample>> GetRecentSamplesAsync(DatabaseInstanceId instanceId, TimeWindow window, CancellationToken ct = default);
}

public interface IRegressionEventRepository
{
    Task SaveRegressionEventsAsync(IEnumerable<RegressionEvent> events, CancellationToken ct = default);
    Task<IReadOnlyList<RegressionEvent>> GetRecentEventsAsync(DatabaseInstanceId instanceId, TimeWindow window, CancellationToken ct = default);
}
```

> These are examples; refine as implementation evolves.

## ADO.NET Usage Pattern

We will follow a consistent pattern for commands:

```csharp
using var connection = _connectionFactory.CreateMonitoringConnection(instanceId, databaseName);
await connection.OpenAsync(ct);

using var command = connection.CreateCommand();
command.CommandText = "...";
command.CommandType = CommandType.Text;
command.CommandTimeout = _options.CommandTimeoutSeconds;

AddParameters(command, ...);

using var reader = await command.ExecuteReaderAsync(ct);
while (await reader.ReadAsync(ct))
{
    // Map to infra model, then to domain model
}
```

- Encapsulate repetitive patterns in helper classes to:
  - Create commands
  - Map data readers to objects
  - Handle nulls safely

## Error Handling & Resilience

- Wrap DB calls in:
  - Try/catch with **logging**
  - Optional **retry** (e.g., transient network failures) using Polly or similar (in Infra layer).
- **Never** let DB errors crash the host:
  - The job should log failures and return gracefully.
  - Use circuit-breakers / backoff for repeated failures.

## Mapping Strategy

- Use small mapping functions or dedicated mappers:
  - `DmvPlanRecord MapDmvRow(SqlDataReader reader)`
  - `PlanMetricSample MapToDomain(DmvPlanRecord record)`

- Keep mapping code **local to Infra**; Domain knows nothing about SQL types.

## Internal Persistence Store

We have two options for storing our own data:

1. **Same monitored DB** (simpler):
   - Create tables in a dedicated schema (e.g., `monitoring`).
2. **Central monitoring DB** (recommended for multi-instance setups):
   - Dedicated DB where all metrics from all instances are stored.

v1 can support either via configuration.

Tables (conceptual):

- `monitoring.QueryFingerprint`
- `monitoring.PlanMetricSample`
- `monitoring.PlanBaseline`
- `monitoring.RegressionEvent`
- `monitoring.Hotspot`

The exact schema will be designed when we implement the repositories.

Next: see `06-plan-collection-and-sampling-engine.md` for how we orchestrate plan collection jobs.
