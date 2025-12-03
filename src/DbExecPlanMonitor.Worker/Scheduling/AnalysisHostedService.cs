using System.Diagnostics;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Application.Services;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Worker.Scheduling;

/// <summary>
/// Background service that runs regression and hotspot analysis on a configurable interval.
/// Analyzes collected plan metrics to detect performance regressions and resource hotspots.
/// </summary>
/// <remarks>
/// <para>
/// This is the second phase of the monitoring pipeline:
/// <list type="number">
/// <item>Collection - Gather metrics</item>
/// <item>Analysis - Detect regressions and hotspots (this service)</item>
/// <item>Alerting - Notify on issues</item>
/// </list>
/// </para>
/// <para>
/// Analysis runs after collection to ensure fresh data is available.
/// The startup delay should be configured to allow initial collection to complete.
/// </para>
/// </remarks>
public sealed class AnalysisHostedService : BackgroundService
{
    private readonly IAnalysisOrchestrator _analysisOrchestrator;
    private readonly IAlertChannel[] _alertChannels;
    private readonly IOptionsMonitor<SchedulingOptions> _options;
    private readonly ILogger<AnalysisHostedService> _logger;
    private int _consecutiveFailures;

    public AnalysisHostedService(
        IAnalysisOrchestrator analysisOrchestrator,
        IEnumerable<IAlertChannel> alertChannels,
        IOptionsMonitor<SchedulingOptions> options,
        ILogger<AnalysisHostedService> logger)
    {
        _analysisOrchestrator = analysisOrchestrator ?? throw new ArgumentNullException(nameof(analysisOrchestrator));
        _alertChannels = alertChannels?.ToArray() ?? Array.Empty<IAlertChannel>();
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;

        if (!options.AnalysisEnabled)
        {
            _logger.LogInformation("Analysis service is disabled in configuration");
            return;
        }

        _logger.LogInformation(
            "Analysis service starting. Interval: {Interval}, Startup delay: {Delay}",
            options.AnalysisInterval,
            options.AnalysisStartupDelay);

        // Initial startup delay - allows collection to run first
        await Task.Delay(options.AnalysisStartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = _options.CurrentValue;

            if (!currentOptions.AnalysisEnabled)
            {
                _logger.LogDebug("Analysis disabled, skipping cycle");
                await Task.Delay(currentOptions.AnalysisInterval, stoppingToken);
                continue;
            }

            await RunAnalysisCycleAsync(stoppingToken);

            // Wait for next interval
            await Task.Delay(currentOptions.AnalysisInterval, stoppingToken);
        }

        _logger.LogInformation("Analysis service stopped");
    }

    private async Task RunAnalysisCycleAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Starting analysis cycle");

            // Run regression and hotspot detection
            var summary = await _analysisOrchestrator.AnalyzeAllAsync(ct);

            stopwatch.Stop();
            _consecutiveFailures = 0;

            if (summary.IsFullySuccessful)
            {
                _logger.LogInformation(
                    "Analysis completed: {Databases} databases, {Regressions} regressions, " +
                    "{Hotspots} hotspots in {Duration:N0}ms",
                    summary.TotalDatabasesAnalyzed,
                    summary.TotalRegressionsDetected,
                    summary.TotalHotspotsDetected,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "Analysis completed with {Errors} error(s): {Databases} databases, " +
                    "{Regressions} regressions in {Duration:N0}ms",
                    summary.TotalErrors,
                    summary.TotalDatabasesAnalyzed,
                    summary.TotalRegressionsDetected,
                    stopwatch.ElapsedMilliseconds);
            }

            // Send alerts if regressions detected
            if (summary.TotalRegressionsDetected > 0)
            {
                await SendRegressionAlertsAsync(summary, ct);
            }

            // Check for auto-resolutions
            var resolvedCount = await _analysisOrchestrator.CheckForAutoResolutionsAsync(ct);
            if (resolvedCount > 0)
            {
                _logger.LogInformation("{Count} regression(s) auto-resolved", resolvedCount);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Analysis cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;

            _logger.LogError(ex,
                "Analysis failed (attempt {Attempt}/{Max}): {Message}",
                _consecutiveFailures,
                _options.CurrentValue.MaxConsecutiveFailures,
                ex.Message);

            var backoff = CalculateBackoff();
            await Task.Delay(backoff, ct);
        }
    }

    private async Task SendRegressionAlertsAsync(AnalysisRunSummary summary, CancellationToken ct)
    {
        var allRegressions = summary.DatabaseResults
            .Where(r => r.Regressions != null)
            .SelectMany(r => r.Regressions!)
            .ToList();

        if (!allRegressions.Any()) return;

        var enabledChannels = _alertChannels.Where(c => c.IsEnabled).ToList();
        
        if (!enabledChannels.Any())
        {
            _logger.LogDebug("No enabled alert channels, skipping alerts");
            return;
        }

        _logger.LogInformation(
            "Sending {Count} regression alert(s) to {Channels} channel(s)",
            allRegressions.Count,
            enabledChannels.Count);

        foreach (var channel in enabledChannels)
        {
            try
            {
                await channel.SendRegressionAlertsAsync(allRegressions, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send alerts to {Channel}: {Message}",
                    channel.ChannelName,
                    ex.Message);
            }
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
