using System.Diagnostics;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Application.Orchestrators;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Worker.Scheduling;

/// <summary>
/// Background service that runs plan collection on a configurable interval.
/// Collects execution plan metrics from all configured SQL Server instances.
/// </summary>
/// <remarks>
/// <para>
/// This is the first phase of the monitoring pipeline:
/// <list type="number">
/// <item>Collection - Gather metrics (this service)</item>
/// <item>Analysis - Detect regressions and hotspots</item>
/// <item>Alerting - Notify on issues</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PlanCollectionHostedService : BackgroundService
{
    private readonly IPlanCollectionOrchestrator _orchestrator;
    private readonly IOptionsMonitor<SchedulingOptions> _options;
    private readonly ILogger<PlanCollectionHostedService> _logger;
    private int _consecutiveFailures;

    public PlanCollectionHostedService(
        IPlanCollectionOrchestrator orchestrator,
        IOptionsMonitor<SchedulingOptions> options,
        ILogger<PlanCollectionHostedService> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;
        
        if (!options.CollectionEnabled)
        {
            _logger.LogInformation("Plan collection is disabled in configuration");
            return;
        }

        _logger.LogInformation(
            "Plan collection service starting. Interval: {Interval}, Startup delay: {Delay}",
            options.CollectionInterval,
            options.CollectionStartupDelay);

        // Initial startup delay
        await Task.Delay(options.CollectionStartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = _options.CurrentValue;
            
            if (!currentOptions.CollectionEnabled)
            {
                _logger.LogDebug("Plan collection disabled, skipping cycle");
                await Task.Delay(currentOptions.CollectionInterval, stoppingToken);
                continue;
            }

            await RunCollectionCycleAsync(stoppingToken);
            
            // Wait for next interval
            await Task.Delay(currentOptions.CollectionInterval, stoppingToken);
        }

        _logger.LogInformation("Plan collection service stopped");
    }

    private async Task RunCollectionCycleAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Starting plan collection cycle");

            var summary = await _orchestrator.CollectAllAsync(ct);

            stopwatch.Stop();
            _consecutiveFailures = 0; // Reset on success

            if (summary.IsFullySuccessful)
            {
                _logger.LogInformation(
                    "Plan collection completed successfully: {Queries} queries from {Instances} instances in {Duration:N0}ms",
                    summary.TotalQueriesCollected,
                    summary.TotalInstances,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "Plan collection completed with errors: {Success}/{Total} instances successful, " +
                    "{Queries} queries in {Duration:N0}ms. Errors: {Errors}",
                    summary.SuccessfulInstances,
                    summary.TotalInstances,
                    summary.TotalQueriesCollected,
                    stopwatch.ElapsedMilliseconds,
                    string.Join("; ", summary.InstanceResults
                        .Where(r => r.Error != null)
                        .Select(r => $"{r.InstanceName}: {r.Error}")));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Plan collection cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            
            _logger.LogError(ex,
                "Plan collection failed (attempt {Attempt}/{Max}): {Message}",
                _consecutiveFailures,
                _options.CurrentValue.MaxConsecutiveFailures,
                ex.Message);

            // Apply backoff after failure
            var backoff = CalculateBackoff();
            _logger.LogDebug("Applying failure backoff: {Backoff}", backoff);
            await Task.Delay(backoff, ct);
        }
    }

    private TimeSpan CalculateBackoff()
    {
        var options = _options.CurrentValue;
        var backoff = TimeSpan.FromTicks(
            options.FailureBackoff.Ticks * (long)Math.Pow(2, _consecutiveFailures - 1));
        
        return backoff > options.MaxFailureBackoff 
            ? options.MaxFailureBackoff 
            : backoff;
    }
}
