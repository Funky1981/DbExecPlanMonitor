using Microsoft.Extensions.Logging;
using DbExecPlanMonitor.Application.Logging;

namespace DbExecPlanMonitor.Infrastructure.Logging;

/// <summary>
/// Telemetry service implementation that logs metrics as structured events.
/// </summary>
/// <remarks>
/// This implementation uses Serilog's structured logging to record metrics.
/// These can later be:
/// - Scraped by Prometheus via log parsing
/// - Sent to Application Insights
/// - Aggregated by log management tools (ELK, Splunk, etc.)
/// 
/// For production use with Prometheus, consider adding:
/// - prometheus-net package for direct metrics exposure
/// - OpenTelemetry for standardized telemetry
/// </remarks>
public sealed class LoggingTelemetryService : ITelemetryService
{
    private readonly ILogger<LoggingTelemetryService> _logger;

    public LoggingTelemetryService(ILogger<LoggingTelemetryService> logger)
    {
        _logger = logger;
    }

    public void RecordPlanCollection(
        string instanceName,
        string databaseName,
        TimeSpan duration,
        int sampleCount,
        bool success)
    {
        if (success)
        {
            _logger.LogInformation(
                new EventId(LogEventIds.PlanCollectionCompleted, "PlanCollectionCompleted"),
                "Plan collection completed for {InstanceName}/{DatabaseName}. " +
                "Duration: {DurationMs}ms, Samples: {SampleCount}",
                instanceName,
                databaseName,
                duration.TotalMilliseconds,
                sampleCount);
        }
        else
        {
            _logger.LogWarning(
                new EventId(LogEventIds.PlanCollectionFailed, "PlanCollectionFailed"),
                "Plan collection failed for {InstanceName}/{DatabaseName}. " +
                "Duration: {DurationMs}ms",
                instanceName,
                databaseName,
                duration.TotalMilliseconds);
        }

        // Log as metric for aggregation
        LogMetric(
            MetricNames.PlanCollectionDurationSeconds,
            duration.TotalSeconds,
            new Dictionary<string, string>
            {
                ["instance"] = instanceName,
                ["database"] = databaseName,
                ["success"] = success.ToString().ToLowerInvariant()
            });

        IncrementCounter(
            success ? MetricNames.CollectionSuccessTotal : MetricNames.CollectionFailureTotal,
            1,
            new Dictionary<string, string>
            {
                ["instance"] = instanceName,
                ["database"] = databaseName
            });

        if (success && sampleCount > 0)
        {
            IncrementCounter(
                MetricNames.SamplesCollectedTotal,
                sampleCount,
                new Dictionary<string, string>
                {
                    ["instance"] = instanceName,
                    ["database"] = databaseName
                });
        }
    }

    public void RecordAnalysis(
        TimeSpan duration,
        int regressionsDetected,
        int hotspotsDetected,
        bool success)
    {
        if (success)
        {
            _logger.LogInformation(
                new EventId(LogEventIds.AnalysisCompleted, "AnalysisCompleted"),
                "Analysis completed. Duration: {DurationMs}ms, " +
                "Regressions: {RegressionsDetected}, Hotspots: {HotspotsDetected}",
                duration.TotalMilliseconds,
                regressionsDetected,
                hotspotsDetected);
        }
        else
        {
            _logger.LogWarning(
                new EventId(LogEventIds.AnalysisFailed, "AnalysisFailed"),
                "Analysis failed. Duration: {DurationMs}ms",
                duration.TotalMilliseconds);
        }

        LogMetric(
            MetricNames.AnalysisDurationSeconds,
            duration.TotalSeconds,
            new Dictionary<string, string> { ["success"] = success.ToString().ToLowerInvariant() });

        if (regressionsDetected > 0)
        {
            IncrementCounter(MetricNames.RegressionsDetectedTotal, regressionsDetected);
        }

        if (hotspotsDetected > 0)
        {
            IncrementCounter(MetricNames.HotspotsDetectedTotal, hotspotsDetected);
        }
    }

    public void RecordRegressionDetected(
        string instanceName,
        string databaseName,
        string queryFingerprint,
        string severity,
        double changePercent)
    {
        _logger.LogWarning(
            new EventId(LogEventIds.RegressionDetected, "RegressionDetected"),
            "Regression detected: {InstanceName}/{DatabaseName}, " +
            "Fingerprint: {QueryFingerprint}, Severity: {Severity}, Change: {ChangePercent:F1}%",
            instanceName,
            databaseName,
            queryFingerprint,
            severity,
            changePercent);

        IncrementCounter(
            MetricNames.RegressionsDetectedTotal,
            1,
            new Dictionary<string, string>
            {
                ["instance"] = instanceName,
                ["database"] = databaseName,
                ["severity"] = severity
            });
    }

    public void RecordAlertSent(string channel, bool success)
    {
        if (success)
        {
            _logger.LogInformation(
                new EventId(LogEventIds.AlertSent, "AlertSent"),
                "Alert sent via {AlertChannel}",
                channel);

            IncrementCounter(
                MetricNames.AlertsSentTotal,
                1,
                new Dictionary<string, string> { ["channel"] = channel });
        }
        else
        {
            _logger.LogWarning(
                new EventId(LogEventIds.AlertFailed, "AlertFailed"),
                "Alert failed via {AlertChannel}",
                channel);

            IncrementCounter(
                MetricNames.AlertsFailedTotal,
                1,
                new Dictionary<string, string> { ["channel"] = channel });
        }
    }

    public void RecordRemediation(
        string instanceName,
        string databaseName,
        string remediationType,
        bool success,
        bool isDryRun)
    {
        var eventId = isDryRun
            ? new EventId(LogEventIds.RemediationDryRun, "RemediationDryRun")
            : success
                ? new EventId(LogEventIds.RemediationExecuted, "RemediationExecuted")
                : new EventId(LogEventIds.RemediationFailed, "RemediationFailed");

        var level = isDryRun ? LogLevel.Information : success ? LogLevel.Information : LogLevel.Warning;

        _logger.Log(
            level,
            eventId,
            "Remediation {RemediationType} for {InstanceName}/{DatabaseName}: " +
            "Success={Success}, DryRun={IsDryRun}",
            remediationType,
            instanceName,
            databaseName,
            success,
            isDryRun);

        var metricName = isDryRun
            ? MetricNames.RemediationsDryRunTotal
            : success
                ? MetricNames.RemediationsExecutedTotal
                : MetricNames.RemediationsFailedTotal;

        IncrementCounter(
            metricName,
            1,
            new Dictionary<string, string>
            {
                ["instance"] = instanceName,
                ["database"] = databaseName,
                ["type"] = remediationType
            });
    }

    public void RecordConnection(
        string instanceName,
        bool success,
        TimeSpan duration)
    {
        if (success)
        {
            _logger.LogDebug(
                new EventId(LogEventIds.ConnectionOpened, "ConnectionOpened"),
                "Connection opened to {InstanceName} in {DurationMs}ms",
                instanceName,
                duration.TotalMilliseconds);

            IncrementCounter(
                MetricNames.ConnectionsOpenedTotal,
                1,
                new Dictionary<string, string> { ["instance"] = instanceName });
        }
        else
        {
            _logger.LogWarning(
                new EventId(LogEventIds.ConnectionFailed, "ConnectionFailed"),
                "Connection failed to {InstanceName} after {DurationMs}ms",
                instanceName,
                duration.TotalMilliseconds);

            IncrementCounter(
                MetricNames.ConnectionsFailedTotal,
                1,
                new Dictionary<string, string> { ["instance"] = instanceName });
        }

        LogMetric(
            MetricNames.ConnectionDurationSeconds,
            duration.TotalSeconds,
            new Dictionary<string, string>
            {
                ["instance"] = instanceName,
                ["success"] = success.ToString().ToLowerInvariant()
            });
    }

    public void RecordHealthCheck(
        string checkName,
        string status,
        TimeSpan duration)
    {
        var level = status switch
        {
            "Healthy" => LogLevel.Debug,
            "Degraded" => LogLevel.Warning,
            _ => LogLevel.Error
        };

        var eventId = status switch
        {
            "Healthy" => new EventId(LogEventIds.HealthCheckPassed, "HealthCheckPassed"),
            "Degraded" => new EventId(LogEventIds.HealthCheckDegraded, "HealthCheckDegraded"),
            _ => new EventId(LogEventIds.HealthCheckFailed, "HealthCheckFailed")
        };

        _logger.Log(
            level,
            eventId,
            "Health check {CheckName}: {Status} ({DurationMs}ms)",
            checkName,
            status,
            duration.TotalMilliseconds);

        LogMetric(
            MetricNames.HealthCheckDurationSeconds,
            duration.TotalSeconds,
            new Dictionary<string, string>
            {
                ["check"] = checkName,
                ["status"] = status.ToLowerInvariant()
            });
    }

    public void IncrementCounter(
        string metricName,
        long value = 1,
        IDictionary<string, string>? tags = null)
    {
        var tagString = tags != null && tags.Count > 0
            ? string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}"))
            : string.Empty;

        _logger.LogDebug(
            "METRIC counter {MetricName} +{Value} [{Tags}]",
            metricName,
            value,
            tagString);
    }

    public void RecordGauge(
        string metricName,
        double value,
        IDictionary<string, string>? tags = null)
    {
        var tagString = tags != null && tags.Count > 0
            ? string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}"))
            : string.Empty;

        _logger.LogDebug(
            "METRIC gauge {MetricName} = {Value:F3} [{Tags}]",
            metricName,
            value,
            tagString);
    }

    public void RecordHistogram(
        string metricName,
        double value,
        IDictionary<string, string>? tags = null)
    {
        var tagString = tags != null && tags.Count > 0
            ? string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}"))
            : string.Empty;

        _logger.LogDebug(
            "METRIC histogram {MetricName} observe {Value:F3} [{Tags}]",
            metricName,
            value,
            tagString);
    }

    private void LogMetric(
        string metricName,
        double value,
        IDictionary<string, string>? tags = null)
    {
        RecordHistogram(metricName, value, tags);
    }
}
