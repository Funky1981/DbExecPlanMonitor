# Doc 09: Background Service Hosting and Scheduling

This document covers the implementation of the background service hosting model and job scheduling for the DB Exec Plan Monitor service.

## Overview

Doc 09 implements a robust hosting model using .NET Worker Service with multiple scheduled background jobs, health checks, and graceful shutdown handling.

## Architecture

### Hosting Model

The service can run as:
- **Windows Service** - Using `AddWindowsService()`
- **Linux systemd service** - Using `AddSystemd()`
- **Container** - Docker/Kubernetes with health endpoints

### Job Scheduling Strategy

Uses `BackgroundService` with configurable intervals for simplicity and reliability:

| Job | Purpose | Default Schedule |
|-----|---------|-----------------|
| `PlanCollectionHostedService` | Collects execution plan metrics | Every 5 minutes |
| `AnalysisHostedService` | Detects regressions and hotspots | Every 5 minutes (30s after collection) |
| `BaselineRebuildHostedService` | Rebuilds statistical baselines | Daily at 2:00 AM UTC |
| `DailySummaryHostedService` | Sends daily digest reports | Daily at 8:00 AM UTC |

## Components Created

### Worker Project (`DbExecPlanMonitor.Worker`)

#### `Scheduling/SchedulingOptions.cs`
Configuration for all scheduled jobs.

```csharp
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";
    
    // Collection settings
    public bool CollectionEnabled { get; set; } = true;
    public TimeSpan CollectionInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan CollectionStartupDelay { get; set; } = TimeSpan.FromSeconds(10);
    
    // Analysis settings
    public bool AnalysisEnabled { get; set; } = true;
    public TimeSpan AnalysisInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan AnalysisStartupDelay { get; set; } = TimeSpan.FromSeconds(30);
    
    // Daily jobs
    public bool BaselineRebuildEnabled { get; set; } = true;
    public TimeSpan BaselineRebuildTimeOfDay { get; set; } = TimeSpan.FromHours(2);
    public bool DailySummaryEnabled { get; set; } = true;
    public TimeSpan DailySummaryTimeOfDay { get; set; } = TimeSpan.FromHours(8);
    
    // Resilience
    public int MaxConsecutiveFailures { get; set; } = 5;
    public TimeSpan FailureBackoff { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxFailureBackoff { get; set; } = TimeSpan.FromMinutes(10);
}
```

#### `Scheduling/PlanCollectionHostedService.cs`
Runs plan collection on a configurable interval with:
- Exponential backoff on failures
- Graceful cancellation handling
- Detailed logging of collection results

#### `Scheduling/AnalysisHostedService.cs`
Runs regression and hotspot analysis with:
- Alert sending for detected regressions
- Auto-resolution checking
- Startup delay to wait for first collection

#### `Scheduling/BaselineRebuildHostedService.cs`
Rebuilds baselines daily at a configured time:
- Iterates all enabled instances and databases
- Computes fresh baselines from recent metrics
- Handles per-database failures gracefully

#### `Scheduling/DailySummaryHostedService.cs`
Sends daily summary reports:
- Aggregates new/resolved/ongoing regressions
- Includes top performance hotspots
- Calculates overall health status
- Sends to all enabled alert channels

### Health Checks (`HealthChecks/`)

#### `LivenessHealthCheck.cs`
Simple liveness check for container probes:
- Always returns healthy if service is running
- Reports uptime information

#### `StorageHealthCheck.cs`
Verifies monitoring storage database connectivity:
- Tests connection to storage database
- Validates required tables exist
- Returns degraded if schema incomplete

#### `SqlServerHealthCheck.cs`
Tests connectivity to monitored SQL Server instances:
- Checks all enabled instances
- Reports individual instance status
- Returns healthy/degraded/unhealthy based on connectivity

### Program.cs Updates

```csharp
// Register scheduling options
builder.Services.Configure<SchedulingOptions>(
    builder.Configuration.GetSection(SchedulingOptions.SectionName));

// Register hosted services
builder.Services.AddHostedService<PlanCollectionHostedService>();
builder.Services.AddHostedService<AnalysisHostedService>();
builder.Services.AddHostedService<BaselineRebuildHostedService>();
builder.Services.AddHostedService<DailySummaryHostedService>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "liveness" })
    .AddCheck<StorageHealthCheck>("storage", tags: new[] { "readiness" })
    .AddCheck<SqlServerHealthCheck>("sqlserver", tags: new[] { "readiness" });

// Enable platform-specific hosting
builder.Services.AddWindowsService(options => 
    options.ServiceName = "DbExecPlanMonitor");
builder.Services.AddSystemd();
```

### Infrastructure Updates

#### `ServiceCollectionExtensions.cs`
Added `AddAnalysis()` method for registering:
- `IRegressionDetector` / `RegressionDetector`
- `IHotspotDetector` / `HotspotDetector`
- `IBaselineService` / `BaselineService`
- `IAnalysisOrchestrator` / `AnalysisOrchestrator`

## Configuration

### appsettings.json

```json
{
  "Scheduling": {
    "CollectionEnabled": true,
    "CollectionInterval": "00:05:00",
    "CollectionStartupDelay": "00:00:10",
    "AnalysisEnabled": true,
    "AnalysisInterval": "00:05:00",
    "AnalysisStartupDelay": "00:00:30",
    "BaselineRebuildEnabled": true,
    "BaselineRebuildTimeOfDay": "02:00:00",
    "DailySummaryEnabled": true,
    "DailySummaryTimeOfDay": "08:00:00",
    "MaxConsecutiveFailures": 5,
    "FailureBackoff": "00:00:30",
    "MaxFailureBackoff": "00:10:00"
  }
}
```

## Health Check Endpoints

For Kubernetes/container deployments:

| Endpoint | Purpose | Tags |
|----------|---------|------|
| `/health/live` | Liveness probe | `liveness` |
| `/health/ready` | Readiness probe | `readiness` |
| `/health` | All checks | all |

## Resilience Features

### Exponential Backoff
After job failures, wait time increases exponentially:
```
Attempt 1: 30 seconds
Attempt 2: 60 seconds
Attempt 3: 120 seconds
Attempt 4: 240 seconds
Attempt 5+: 10 minutes (capped)
```

### Graceful Shutdown
- All services honor `CancellationToken`
- Clean disposal of database connections
- Logging of shutdown events

### Error Isolation
- Per-database error handling in baseline rebuild
- Per-channel error handling in alert sending
- Job failures don't affect other jobs

## Files Created/Modified

| File | Purpose |
|------|---------|
| `Scheduling/SchedulingOptions.cs` | Configuration options |
| `Scheduling/PlanCollectionHostedService.cs` | Collection job |
| `Scheduling/AnalysisHostedService.cs` | Analysis job |
| `Scheduling/BaselineRebuildHostedService.cs` | Baseline job |
| `Scheduling/DailySummaryHostedService.cs` | Summary job |
| `HealthChecks/LivenessHealthCheck.cs` | Liveness probe |
| `HealthChecks/StorageHealthCheck.cs` | Storage probe |
| `HealthChecks/SqlServerHealthCheck.cs` | SQL Server probe |
| `Program.cs` | Updated with new registrations |
| `appsettings.json` | Added Scheduling section |
| `ServiceCollectionExtensions.cs` | Added AddAnalysis() |

## Next Steps

- **Doc 10**: Configuration and Secrets Management
- **Doc 11**: Logging, Telemetry, and Auditing
