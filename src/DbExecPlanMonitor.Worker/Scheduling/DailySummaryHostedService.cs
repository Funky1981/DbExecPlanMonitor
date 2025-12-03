using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Application.Services;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using DbExecPlanMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Hotspot = DbExecPlanMonitor.Domain.Services.Hotspot;

namespace DbExecPlanMonitor.Worker.Scheduling;

/// <summary>
/// Background service that sends daily summary reports at a configured time.
/// Aggregates the day's monitoring activity into a single digest.
/// </summary>
public sealed class DailySummaryHostedService : BackgroundService
{
    private readonly IRegressionEventRepository _regressionRepository;
    private readonly IAnalysisOrchestrator _analysisOrchestrator;
    private readonly IEnumerable<IAlertChannel> _alertChannels;
    private readonly IOptionsMonitor<SchedulingOptions> _options;
    private readonly ILogger<DailySummaryHostedService> _logger;

    public DailySummaryHostedService(
        IRegressionEventRepository regressionRepository,
        IAnalysisOrchestrator analysisOrchestrator,
        IEnumerable<IAlertChannel> alertChannels,
        IOptionsMonitor<SchedulingOptions> options,
        ILogger<DailySummaryHostedService> logger)
    {
        _regressionRepository = regressionRepository ?? throw new ArgumentNullException(nameof(regressionRepository));
        _analysisOrchestrator = analysisOrchestrator ?? throw new ArgumentNullException(nameof(analysisOrchestrator));
        _alertChannels = alertChannels ?? Array.Empty<IAlertChannel>();
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;

        if (!options.DailySummaryEnabled)
        {
            _logger.LogInformation("Daily summary is disabled in configuration");
            return;
        }

        _logger.LogInformation(
            "Daily summary service starting. Scheduled time: {Time} UTC",
            options.DailySummaryTimeOfDay);

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = _options.CurrentValue;

            if (!currentOptions.DailySummaryEnabled)
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                continue;
            }

            var delay = CalculateDelayUntilNextRun(currentOptions.DailySummaryTimeOfDay);

            _logger.LogDebug(
                "Next daily summary scheduled in {Delay} at {Time:u}",
                delay,
                DateTime.UtcNow.Add(delay));

            await Task.Delay(delay, stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
            {
                await SendDailySummaryAsync(stoppingToken);
            }
        }

        _logger.LogInformation("Daily summary service stopped");
    }

    private async Task SendDailySummaryAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Generating daily summary");

            var yesterday = DateTime.UtcNow.Date.AddDays(-1);
            var today = DateTime.UtcNow.Date;

            // Get regressions from the last 24 hours using TimeWindow
            var window = new TimeWindow(yesterday, today);
            var recentEvents = await _regressionRepository.GetRecentEventsAsync(window, ct);

            // Map records to domain entities
            var allEvents = recentEvents
                .Select(MapToRegressionEvent)
                .ToList();

            var newRegressions = allEvents
                .Where(e => e.Status == RegressionStatus.New || e.Status == RegressionStatus.Acknowledged)
                .ToList();

            var resolvedRegressions = allEvents
                .Where(e => e.Status == RegressionStatus.Resolved || e.Status == RegressionStatus.AutoResolved)
                .ToList();

            var ongoingRegressions = allEvents
                .Where(e => e.Status == RegressionStatus.New)
                .ToList();

            // Get top hotspots
            var hotspots = new List<Hotspot>();
            try
            {
                var analysisResult = await _analysisOrchestrator.AnalyzeAllAsync(ct);
                hotspots = analysisResult.DatabaseResults
                    .Where(r => r.Hotspots != null)
                    .SelectMany(r => r.Hotspots!)
                    .OrderByDescending(h => h.RankingValue)
                    .Take(10)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get hotspots for daily summary");
            }

            var summary = new DailySummary
            {
                Date = DateOnly.FromDateTime(yesterday),
                PeriodStartUtc = yesterday,
                PeriodEndUtc = today,
                DatabasesAnalyzed = 0,
                QueriesAnalyzed = 0,
                NewRegressions = newRegressions,
                ResolvedRegressions = resolvedRegressions,
                OngoingRegressions = ongoingRegressions,
                TopHotspots = hotspots
            };

            // Send to all enabled channels
            var enabledChannels = _alertChannels.Where(c => c.IsEnabled).ToList();

            if (!enabledChannels.Any())
            {
                _logger.LogDebug("No enabled alert channels for daily summary");
                return;
            }

            _logger.LogInformation(
                "Sending daily summary to {Count} channel(s): " +
                "Health={Health}, NewRegressions={New}, Resolved={Resolved}",
                enabledChannels.Count,
                summary.OverallHealth,
                newRegressions.Count,
                resolvedRegressions.Count);

            foreach (var channel in enabledChannels)
            {
                try
                {
                    await channel.SendDailySummaryAsync(summary, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send daily summary to {Channel}: {Message}",
                        channel.ChannelName,
                        ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Daily summary cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate daily summary: {Message}", ex.Message);
        }
    }

    private static RegressionEvent MapToRegressionEvent(RegressionEventRecord record)
    {
        return new RegressionEvent
        {
            Id = record.Id,
            FingerprintId = record.FingerprintId,
            InstanceName = record.InstanceName,
            DatabaseName = record.DatabaseName,
            DetectedAtUtc = record.DetectedAtUtc,
            Severity = record.Severity,
            Status = record.Status,
            DurationChangePercent = (decimal)record.ChangePercent,
            Description = record.QueryTextSample,
            OldPlanHash = record.BaselinePlanHash,
            NewPlanHash = record.CurrentPlanHash
        };
    }

    private static TimeSpan CalculateDelayUntilNextRun(TimeSpan targetTimeOfDay)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var targetToday = today.Add(targetTimeOfDay);

        if (now >= targetToday)
        {
            targetToday = targetToday.AddDays(1);
        }

        return targetToday - now;
    }
}
