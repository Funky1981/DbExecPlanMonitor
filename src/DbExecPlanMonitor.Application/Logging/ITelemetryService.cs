namespace DbExecPlanMonitor.Application.Logging;

/// <summary>
/// Interface for telemetry/metrics collection.
/// </summary>
/// <remarks>
/// Provides an abstraction for recording metrics that can be:
/// - Logged as structured events (default)
/// - Exported to Prometheus
/// - Sent to Application Insights
/// - Integrated with other APM systems
/// 
/// All methods are fire-and-forget to avoid impacting main operations.
/// </remarks>
public interface ITelemetryService
{
    /// <summary>
    /// Records the duration of a plan collection run.
    /// </summary>
    /// <param name="instanceName">The instance name.</param>
    /// <param name="databaseName">The database name.</param>
    /// <param name="duration">The collection duration.</param>
    /// <param name="sampleCount">Number of samples collected.</param>
    /// <param name="success">Whether the collection succeeded.</param>
    void RecordPlanCollection(
        string instanceName,
        string databaseName,
        TimeSpan duration,
        int sampleCount,
        bool success);

    /// <summary>
    /// Records the duration of an analysis run.
    /// </summary>
    /// <param name="duration">The analysis duration.</param>
    /// <param name="regressionsDetected">Number of regressions detected.</param>
    /// <param name="hotspotsDetected">Number of hotspots detected.</param>
    /// <param name="success">Whether the analysis succeeded.</param>
    void RecordAnalysis(
        TimeSpan duration,
        int regressionsDetected,
        int hotspotsDetected,
        bool success);

    /// <summary>
    /// Records a regression detection event.
    /// </summary>
    /// <param name="instanceName">The instance name.</param>
    /// <param name="databaseName">The database name.</param>
    /// <param name="queryFingerprint">The query fingerprint.</param>
    /// <param name="severity">The regression severity.</param>
    /// <param name="changePercent">The percentage change.</param>
    void RecordRegressionDetected(
        string instanceName,
        string databaseName,
        string queryFingerprint,
        string severity,
        double changePercent);

    /// <summary>
    /// Records an alert being sent.
    /// </summary>
    /// <param name="channel">The alert channel (Email, Teams, Slack).</param>
    /// <param name="success">Whether the alert was sent successfully.</param>
    void RecordAlertSent(string channel, bool success);

    /// <summary>
    /// Records a remediation action.
    /// </summary>
    /// <param name="instanceName">The instance name.</param>
    /// <param name="databaseName">The database name.</param>
    /// <param name="remediationType">Type of remediation.</param>
    /// <param name="success">Whether the remediation succeeded.</param>
    /// <param name="isDryRun">Whether this was a dry run.</param>
    void RecordRemediation(
        string instanceName,
        string databaseName,
        string remediationType,
        bool success,
        bool isDryRun);

    /// <summary>
    /// Records a database connection attempt.
    /// </summary>
    /// <param name="instanceName">The instance name.</param>
    /// <param name="success">Whether the connection succeeded.</param>
    /// <param name="duration">The connection duration.</param>
    void RecordConnection(
        string instanceName,
        bool success,
        TimeSpan duration);

    /// <summary>
    /// Records a health check result.
    /// </summary>
    /// <param name="checkName">The health check name.</param>
    /// <param name="status">The health status (Healthy, Degraded, Unhealthy).</param>
    /// <param name="duration">The check duration.</param>
    void RecordHealthCheck(
        string checkName,
        string status,
        TimeSpan duration);

    /// <summary>
    /// Increments a counter metric.
    /// </summary>
    /// <param name="metricName">The metric name.</param>
    /// <param name="value">The value to add (default 1).</param>
    /// <param name="tags">Optional tags/labels.</param>
    void IncrementCounter(
        string metricName,
        long value = 1,
        IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a gauge metric value.
    /// </summary>
    /// <param name="metricName">The metric name.</param>
    /// <param name="value">The current value.</param>
    /// <param name="tags">Optional tags/labels.</param>
    void RecordGauge(
        string metricName,
        double value,
        IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a histogram observation.
    /// </summary>
    /// <param name="metricName">The metric name.</param>
    /// <param name="value">The observed value.</param>
    /// <param name="tags">Optional tags/labels.</param>
    void RecordHistogram(
        string metricName,
        double value,
        IDictionary<string, string>? tags = null);
}

/// <summary>
/// Metric names for consistency.
/// </summary>
public static class MetricNames
{
    private const string Prefix = "db_exec_monitor_";

    // Collection metrics
    public const string PlanCollectionDurationSeconds = Prefix + "plan_collection_duration_seconds";
    public const string SamplesCollectedTotal = Prefix + "samples_collected_total";
    public const string CollectionSuccessTotal = Prefix + "collection_success_total";
    public const string CollectionFailureTotal = Prefix + "collection_failure_total";

    // Analysis metrics
    public const string AnalysisDurationSeconds = Prefix + "analysis_duration_seconds";
    public const string RegressionsDetectedTotal = Prefix + "regressions_detected_total";
    public const string HotspotsDetectedTotal = Prefix + "hotspots_detected_total";

    // Alert metrics
    public const string AlertsSentTotal = Prefix + "alerts_sent_total";
    public const string AlertsFailedTotal = Prefix + "alerts_failed_total";

    // Remediation metrics
    public const string RemediationsExecutedTotal = Prefix + "remediations_executed_total";
    public const string RemediationsFailedTotal = Prefix + "remediations_failed_total";
    public const string RemediationsDryRunTotal = Prefix + "remediations_dry_run_total";

    // Connection metrics
    public const string ConnectionsOpenedTotal = Prefix + "connections_opened_total";
    public const string ConnectionsFailedTotal = Prefix + "connections_failed_total";
    public const string ConnectionDurationSeconds = Prefix + "connection_duration_seconds";

    // Health check metrics
    public const string HealthCheckDurationSeconds = Prefix + "health_check_duration_seconds";
    public const string HealthCheckStatus = Prefix + "health_check_status";

    // Active counts
    public const string ActiveRegressionsCount = Prefix + "active_regressions_count";
    public const string MonitoredInstancesCount = Prefix + "monitored_instances_count";
    public const string MonitoredDatabasesCount = Prefix + "monitored_databases_count";
}
