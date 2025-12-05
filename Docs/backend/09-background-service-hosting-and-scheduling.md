# 09 – Background Service Hosting and Scheduling

This file describes **how we host and schedule** the monitoring jobs.

## Hosting Model

- Use .NET **Worker Service** template:
  - Can run as:
    - Windows Service
    - Linux systemd service
    - Containerised service (Kubernetes, etc.)

Project: `DbExecPlanMonitor.Worker`

## Composition Root

In `Program.cs` (or equivalent):

- Build host with:
  - Configuration from appsettings + environment.
  - Logging.
  - Dependency injection for:
    - Domain/Application/Infrastructure services.
    - Scheduled jobs.

We keep `Program.cs` minimal and push logic into:

- `Startup`/`ServiceRegistration` classes in Application/Infrastructure projects.

## Scheduling Strategy

For v1, keep it simple:

- Use an in-process scheduler such as:
  - `IHostedService` with `Timer`.
  - Or Quartz.NET (if you prefer; optional).

We define **three primary recurring jobs**:

1. `PlanCollectionJob`
   - Runs every N minutes (configurable).
2. `AnalysisJob`
   - Runs every M minutes (can be same as N).
3. `BaselineRebuildJob`
   - Runs daily (nightly).

### Example Outline (conceptual)

```csharp
public class PlanCollectionHostedService : BackgroundService
{
    private readonly IPlanCollectionOrchestrator _orchestrator;
    private readonly TimeSpan _interval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _orchestrator.RunAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }
    }
}
```

The orchestrator should:

- Enumerate configured instances + databases.
- Run collection in parallel where appropriate.
- Catch and log exceptions per instance/database, so one failure doesn’t kill the whole loop.

## Health Checks

Expose application health via:

- .NET Health Checks (`Microsoft.Extensions.Diagnostics.HealthChecks`):
  - `liveness`: service is running.
  - `readiness`: can connect to DB(s) and config storage (within timeout).

If running in Kubernetes / orchestrated environment, wire these endpoints to liveness/readiness probes.

## Graceful Shutdown

- `BackgroundService` implementations should honour `CancellationToken`.
- DB connections and long-running tasks need proper disposal and cancellation.

Next: see `10-configuration-and-secrets-management.md` for how we configure instances, thresholds, and secrets.
