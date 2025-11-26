using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Application.Orchestrators;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Worker;

/// <summary>
/// Background service that orchestrates execution plan monitoring.
/// Implements the Template Method pattern for recurring jobs with
/// built-in logging, timing, and error handling.
/// </summary>
public class MonitoringWorker : BackgroundService
{
    private readonly IPlanCollectionOrchestrator _orchestrator;
    private readonly IOptionsMonitor<PlanCollectionOptions> _options;
    private readonly ILogger<MonitoringWorker> _logger;

    public MonitoringWorker(
        IPlanCollectionOrchestrator orchestrator,
        IOptionsMonitor<PlanCollectionOptions> options,
        ILogger<MonitoringWorker> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DB Execution Plan Monitor started at: {Time}", DateTimeOffset.Now);

        // Allow a brief delay for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var interval = _options.CurrentValue.CollectionInterval;
                _logger.LogDebug("Starting collection cycle (interval: {Interval})", interval);

                // Execute collection workflow
                var summary = await _orchestrator.CollectAllAsync(stoppingToken);

                // Log summary
                if (summary.IsFullySuccessful)
                {
                    _logger.LogInformation(
                        "Collection cycle completed: {Queries} queries from {Instances} instances in {Duration:N0}ms",
                        summary.TotalQueriesCollected,
                        summary.TotalInstances,
                        summary.Duration.TotalMilliseconds);
                }
                else
                {
                    _logger.LogWarning(
                        "Collection cycle completed with errors: {Success}/{Total} instances successful, " +
                        "{Queries} queries collected in {Duration:N0}ms",
                        summary.SuccessfulInstances,
                        summary.TotalInstances,
                        summary.TotalQueriesCollected,
                        summary.Duration.TotalMilliseconds);
                }

                // TODO: After collection, trigger analysis phase:
                // 1. Compare current metrics to baselines
                // 2. Detect regressions
                // 3. Send alerts if thresholds exceeded

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown requested
                _logger.LogInformation("Monitoring service shutdown requested");
                break;
            }
            catch (Exception ex)
            {
                // Log error but continue running (resilient service)
                _logger.LogError(ex, "Error during monitoring cycle");
                
                // Backoff before retrying
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("DB Execution Plan Monitor stopped at: {Time}", DateTimeOffset.Now);
    }
}
