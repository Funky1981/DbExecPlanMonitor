using DbExecPlanMonitor.Application.Orchestrators;
using DbExecPlanMonitor.Application.Services;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Worker.Scheduling;

/// <summary>
/// Background service that rebuilds baselines daily at a configured time.
/// Baselines are statistical aggregates used for regression detection.
/// </summary>
/// <remarks>
/// <para>
/// Baseline rebuild is a heavier operation that:
/// <list type="bullet">
/// <item>Aggregates metrics over the baseline window (default 7 days)</item>
/// <item>Calculates percentile thresholds</item>
/// <item>Updates baseline records in the database</item>
/// </list>
/// </para>
/// <para>
/// This runs nightly (default 2:00 AM UTC) to minimize impact on production monitoring.
/// </para>
/// </remarks>
public sealed class BaselineRebuildHostedService : BackgroundService
{
    private readonly IBaselineService _baselineService;
    private readonly IOptionsMonitor<MonitoringInstancesOptions> _instancesOptions;
    private readonly IOptionsMonitor<SchedulingOptions> _options;
    private readonly ILogger<BaselineRebuildHostedService> _logger;

    public BaselineRebuildHostedService(
        IBaselineService baselineService,
        IOptionsMonitor<MonitoringInstancesOptions> instancesOptions,
        IOptionsMonitor<SchedulingOptions> options,
        ILogger<BaselineRebuildHostedService> logger)
    {
        _baselineService = baselineService ?? throw new ArgumentNullException(nameof(baselineService));
        _instancesOptions = instancesOptions ?? throw new ArgumentNullException(nameof(instancesOptions));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;

        if (!options.BaselineRebuildEnabled)
        {
            _logger.LogInformation("Baseline rebuild is disabled in configuration");
            return;
        }

        _logger.LogInformation(
            "Baseline rebuild service starting. Scheduled time: {Time} UTC",
            options.BaselineRebuildTimeOfDay);

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = _options.CurrentValue;
            
            if (!currentOptions.BaselineRebuildEnabled)
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                continue;
            }

            var delay = CalculateDelayUntilNextRun(currentOptions.BaselineRebuildTimeOfDay);
            
            _logger.LogDebug(
                "Next baseline rebuild scheduled in {Delay} at {Time:u}",
                delay,
                DateTime.UtcNow.Add(delay));

            await Task.Delay(delay, stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
            {
                await RunBaselineRebuildAsync(stoppingToken);
            }
        }

        _logger.LogInformation("Baseline rebuild service stopped");
    }

    private async Task RunBaselineRebuildAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Starting scheduled baseline rebuild");

            var instances = _instancesOptions.CurrentValue.Instances?
                .Where(i => i.Enabled)
                .ToList() ?? [];

            var totalUpdated = 0;
            var totalCreated = 0;
            var startTime = DateTime.UtcNow;

            foreach (var instance in instances)
            {
                foreach (var db in instance.Databases?.Where(d => d.Enabled) ?? [])
                {
                    try
                    {
                        var result = await _baselineService.ComputeBaselinesForDatabaseAsync(
                            instance.Name,
                            db.Name,
                            lookbackDays: 7,
                            ct);

                        totalUpdated += result.BaselinesUpdated;
                        totalCreated += result.BaselinesCreated;

                        _logger.LogDebug(
                            "Baseline rebuild for {Instance}/{Database}: {Created} created, {Updated} updated",
                            instance.Name,
                            db.Name,
                            result.BaselinesCreated,
                            result.BaselinesUpdated);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to rebuild baselines for {Instance}/{Database}: {Message}",
                            instance.Name,
                            db.Name,
                            ex.Message);
                    }
                }
            }

            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation(
                "Baseline rebuild completed: {Updated} updated, {Created} created in {Duration:N0}ms",
                totalUpdated,
                totalCreated,
                duration.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Baseline rebuild cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Baseline rebuild failed: {Message}", ex.Message);
            // Don't throw - we'll try again tomorrow
        }
    }

    private static TimeSpan CalculateDelayUntilNextRun(TimeSpan targetTimeOfDay)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var targetToday = today.Add(targetTimeOfDay);

        // If target time already passed today, schedule for tomorrow
        if (now >= targetToday)
        {
            targetToday = targetToday.AddDays(1);
        }

        return targetToday - now;
    }
}
