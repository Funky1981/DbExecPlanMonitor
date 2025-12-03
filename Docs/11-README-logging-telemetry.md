# Logging, Telemetry, and Auditing Implementation

This document describes the implementation of Doc 11 - Logging, Telemetry, and Auditing for DbExecPlanMonitor.

## Overview

The observability system provides:
- Structured logging with consistent event IDs
- Telemetry metrics for monitoring and alerting
- Audit trails for remediation actions
- Integration points for APM systems (Prometheus, Application Insights, etc.)

## Components

### 1. Log Event IDs (`Application/Logging/LogEventIds.cs`)

Centralized event ID constants for structured logging, organized by category:

| Range | Category |
|-------|----------|
| 1000-1099 | Plan Collection |
| 1100-1199 | Analysis (Regression/Hotspot) |
| 1200-1299 | Alerting |
| 1300-1399 | Remediation |
| 1400-1499 | Baseline Management |
| 1500-1599 | Configuration/Startup |
| 1600-1699 | Health Checks |
| 1700-1799 | Database Connectivity |

Example usage:
```csharp
_logger.LogInformation(
    new EventId(LogEventIds.PlanCollectionCompleted, "PlanCollectionCompleted"),
    "Collection completed for {InstanceName}/{DatabaseName}",
    instanceName, databaseName);
```

### 2. Property Names (`Application/Logging/LogEventIds.cs`)

Consistent property names for structured logging:
- `InstanceName`, `DatabaseName`, `QueryFingerprint`
- `Duration`, `DurationMs`, `SampleCount`
- `RegressionId`, `RegressionSeverity`
- `RemediationId`, `RemediationType`, `SqlStatement`

### 3. Telemetry Service (`Application/Logging/ITelemetryService.cs`)

Interface for recording metrics:

```csharp
public interface ITelemetryService
{
    void RecordPlanCollection(string instance, string db, TimeSpan duration, int samples, bool success);
    void RecordAnalysis(TimeSpan duration, int regressions, int hotspots, bool success);
    void RecordRegressionDetected(string instance, string db, string fingerprint, string severity, double change);
    void RecordAlertSent(string channel, bool success);
    void RecordRemediation(string instance, string db, string type, bool success, bool isDryRun);
    void RecordConnection(string instance, bool success, TimeSpan duration);
    void RecordHealthCheck(string checkName, string status, TimeSpan duration);
    void IncrementCounter(string metricName, long value = 1, IDictionary<string, string>? tags = null);
    void RecordGauge(string metricName, double value, IDictionary<string, string>? tags = null);
    void RecordHistogram(string metricName, double value, IDictionary<string, string>? tags = null);
}
```

### 4. Metric Names (`Application/Logging/ITelemetryService.cs`)

Standard metric names with `db_exec_monitor_` prefix:

```
db_exec_monitor_plan_collection_duration_seconds
db_exec_monitor_samples_collected_total
db_exec_monitor_regressions_detected_total
db_exec_monitor_alerts_sent_total
db_exec_monitor_remediations_executed_total
db_exec_monitor_connections_opened_total
db_exec_monitor_health_check_duration_seconds
```

### 5. Logging Telemetry Service (`Infrastructure/Logging/LoggingTelemetryService.cs`)

Default implementation that logs metrics as structured events:

```csharp
// Metrics are logged in a parseable format
_logger.LogDebug(
    "METRIC counter {MetricName} +{Value} [{Tags}]",
    metricName, value, tagString);
```

This allows:
- Log aggregation tools to extract metrics
- Easy integration with Prometheus log parsing
- No additional dependencies for basic monitoring

### 6. Remediation Audit Entity (`Domain/Entities/RemediationAuditRecord.cs`)

Captures all remediation attempts:

```csharp
public sealed class RemediationAuditRecord
{
    public Guid Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required string QueryFingerprint { get; init; }
    public required string RemediationType { get; init; }
    public required string SqlStatement { get; init; }
    public bool IsDryRun { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan? Duration { get; init; }
    // ... additional audit fields
}
```

### 7. Audit Repository (`Application/Interfaces/IRemediationAuditRepository.cs`)

Interface for persisting audit records:

```csharp
public interface IRemediationAuditRepository
{
    Task SaveAsync(RemediationAuditRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<RemediationAuditRecord>> GetByInstanceAsync(...);
    Task<IReadOnlyList<RemediationAuditRecord>> GetByQueryFingerprintAsync(...);
    Task<IReadOnlyList<RemediationAuditRecord>> GetRecentFailuresAsync(...);
    Task<RemediationAuditSummary> GetSummaryAsync(...);
}
```

### 8. SQL Audit Repository (`Infrastructure/Logging/SqlRemediationAuditRepository.cs`)

SQL Server implementation storing records in `monitoring.RemediationAudit` table.

### 9. Database Schema (`Infrastructure/Logging/Scripts/CreateRemediationAuditTable.sql`)

SQL script to create the audit table:

```sql
CREATE TABLE monitoring.RemediationAudit (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Timestamp DATETIMEOFFSET NOT NULL,
    InstanceName NVARCHAR(256) NOT NULL,
    DatabaseName NVARCHAR(256) NOT NULL,
    QueryFingerprint NVARCHAR(256) NOT NULL,
    RemediationType NVARCHAR(100) NOT NULL,
    SqlStatement NVARCHAR(MAX) NOT NULL,
    IsDryRun BIT NOT NULL,
    Success BIT NOT NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    -- ... additional columns
);
```

## Logging Levels

| Level | Usage |
|-------|-------|
| `Debug` | Detailed troubleshooting, metrics, connection details |
| `Information` | Normal operations, job completion, alerts sent |
| `Warning` | Non-fatal issues, failed connections, degraded health |
| `Error` | Significant failures requiring attention |
| `Fatal` | Service termination |

## Serilog Configuration

Already configured in `Program.cs`:

```csharp
builder.Services.AddSerilog((services, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/dbexecplanmonitor-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30);
});
```

## Registration

In `Program.cs`:

```csharp
builder.Services.AddTelemetryAndAuditing(builder.Configuration);
```

## Future Enhancements

### Prometheus Integration

Add `prometheus-net` package and expose `/metrics` endpoint:

```csharp
// In future version
services.AddSingleton<ITelemetryService, PrometheusTelemetryService>();
```

### Application Insights

Add Azure Application Insights telemetry:

```csharp
// In future version
services.AddApplicationInsightsTelemetryWorkerService();
services.AddSingleton<ITelemetryService, AppInsightsTelemetryService>();
```

### OpenTelemetry

Add standardized telemetry:

```csharp
// In future version
services.AddOpenTelemetry()
    .WithMetrics(builder => builder.AddMeter("DbExecPlanMonitor"));
```

## Files Created

| File | Purpose |
|------|---------|
| `Application/Logging/LogEventIds.cs` | Event IDs and property names |
| `Application/Logging/ITelemetryService.cs` | Telemetry interface and metric names |
| `Application/Interfaces/IRemediationAuditRepository.cs` | Audit repository interface |
| `Domain/Entities/RemediationAuditRecord.cs` | Audit entity |
| `Infrastructure/Logging/LoggingTelemetryService.cs` | Logging-based telemetry |
| `Infrastructure/Logging/SqlRemediationAuditRepository.cs` | SQL audit repository |
| `Infrastructure/Logging/LoggingServiceExtensions.cs` | DI registration |
| `Infrastructure/Logging/Scripts/CreateRemediationAuditTable.sql` | Database schema |
