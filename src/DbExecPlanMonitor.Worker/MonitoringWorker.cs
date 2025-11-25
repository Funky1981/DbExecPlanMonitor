namespace DbExecPlanMonitor.Worker;

/// <summary>
/// Background service that orchestrates execution plan monitoring.
/// Implements the Template Method pattern for recurring jobs with
/// built-in logging, timing, and error handling.
/// </summary>
public class MonitoringWorker : BackgroundService
{
    private readonly ILogger<MonitoringWorker> _logger;

    // TODO: Inject IMonitoringOrchestrator from Application layer
    // TODO: Inject IOptions<MonitoringSettings> for configuration

    public MonitoringWorker(ILogger<MonitoringWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DB Execution Plan Monitor started at: {Time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Starting monitoring cycle at: {Time}", DateTimeOffset.Now);

                // TODO: Call orchestrator to:
                // 1. Collect execution plans from SQL Server DMVs
                // 2. Analyze plans for regressions and hotspots
                // 3. Send alerts if thresholds exceeded
                // 4. Store metrics for historical analysis

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
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
