using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;
using Microsoft.Extensions.Logging;
using Hotspot = DbExecPlanMonitor.Domain.Services.Hotspot;

namespace DbExecPlanMonitor.Infrastructure.Messaging;

/// <summary>
/// Alert channel implementation that logs alerts instead of sending them.
/// </summary>
/// <remarks>
/// <para>
/// This is the fallback/default channel. It's always enabled and provides
/// a reliable audit trail even when other channels fail.
/// </para>
/// <para>
/// Useful for:
/// <list type="bullet">
/// <item>Development and testing environments</item>
/// <item>Backup when primary channels are unavailable</item>
/// <item>Audit trail in production</item>
/// </list>
/// </para>
/// </remarks>
public sealed class LogOnlyAlertChannel : IAlertChannel
{
    private readonly ILogger<LogOnlyAlertChannel> _logger;

    public LogOnlyAlertChannel(ILogger<LogOnlyAlertChannel> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ChannelName => "Log";

    /// <inheritdoc />
    public bool IsEnabled => true; // Always enabled as fallback

    /// <inheritdoc />
    public Task SendRegressionAlertsAsync(
        IEnumerable<RegressionEvent> regressions,
        CancellationToken ct = default)
    {
        foreach (var regression in regressions)
        {
            _logger.LogWarning(
                "[ALERT] Regression detected: {Instance}/{Database} - " +
                "Severity: {Severity}, Duration Change: {DurationChange:+#;-#;0}%, " +
                "CPU Change: {CpuChange:+#;-#;0}%, Status: {Status}",
                regression.InstanceName,
                regression.DatabaseName,
                regression.Severity,
                regression.DurationChangePercent,
                regression.CpuChangePercent,
                regression.Status);

            if (regression.Description != null)
            {
                _logger.LogInformation(
                    "Regression {Id} description: {Description}",
                    regression.Id,
                    regression.Description);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendHotspotSummaryAsync(
        IEnumerable<Hotspot> hotspots,
        CancellationToken ct = default)
    {
        var hotspotList = hotspots.ToList();

        _logger.LogInformation(
            "[HOTSPOTS] Top {Count} resource-intensive queries:",
            hotspotList.Count);

        foreach (var hotspot in hotspotList)
        {
            _logger.LogInformation(
                "  #{Rank} {Instance}/{Database}: " +
                "Executions: {Executions:N0}, " +
                "Avg Duration: {AvgDuration:N2}ms, " +
                "Avg CPU: {AvgCpu:N2}ms, " +
                "Ranked by: {RankedBy} ({RankingValue:N2})",
                hotspot.Rank,
                hotspot.InstanceName,
                hotspot.DatabaseName,
                hotspot.ExecutionCount,
                hotspot.AvgDurationMs,
                hotspot.AvgCpuTimeMs,
                hotspot.RankedBy,
                hotspot.RankingValue);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendDailySummaryAsync(
        DailySummary summary,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DAILY SUMMARY] {Date:yyyy-MM-dd}: " +
            "Status: {Status}, " +
            "New Regressions: {NewRegressions}, " +
            "Resolved: {Resolved}, " +
            "Databases: {Databases}, " +
            "Queries: {Queries}, " +
            "Hotspots: {Hotspots}",
            summary.Date,
            summary.OverallHealth,
            summary.NewRegressions.Count,
            summary.ResolvedRegressions.Count,
            summary.DatabasesAnalyzed,
            summary.QueriesAnalyzed,
            summary.TopHotspots.Count);

        // Log critical/high regressions in detail
        foreach (var regression in summary.NewRegressions
            .Where(r => r.Severity >= Domain.Enums.RegressionSeverity.High))
        {
            _logger.LogWarning(
                "  [{Severity}] Regression on {Instance}/{Database}: " +
                "Duration +{DurationChange:N0}%, CPU +{CpuChange:N0}%",
                regression.Severity,
                regression.InstanceName,
                regression.DatabaseName,
                regression.DurationChangePercent,
                regression.CpuChangePercent);
        }

        // Log top hotspots
        foreach (var hotspot in summary.TopHotspots.Take(5))
        {
            _logger.LogInformation(
                "  Hotspot #{Rank}: {Instance}/{Database} - " +
                "{Executions:N0} executions, {AvgDuration:N2}ms avg",
                hotspot.Rank,
                hotspot.InstanceName,
                hotspot.DatabaseName,
                hotspot.ExecutionCount,
                hotspot.AvgDurationMs);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Log alert channel test: OK");
        return Task.FromResult(true);
    }
}
