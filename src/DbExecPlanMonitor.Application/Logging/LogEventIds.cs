namespace DbExecPlanMonitor.Application.Logging;

/// <summary>
/// Centralized logging event IDs for structured logging.
/// </summary>
/// <remarks>
/// Using consistent event IDs enables:
/// - Filtering logs by event type
/// - Creating alerts in monitoring systems
/// - Correlating related log entries
/// 
/// Event ID ranges:
/// - 1000-1099: Plan Collection
/// - 1100-1199: Analysis (Regression/Hotspot detection)
/// - 1200-1299: Alerting
/// - 1300-1399: Remediation
/// - 1400-1499: Baseline management
/// - 1500-1599: Configuration/Startup
/// - 1600-1699: Health checks
/// - 1700-1799: Database connectivity
/// </remarks>
public static class LogEventIds
{
    // Plan Collection (1000-1099)
    public const int PlanCollectionStarted = 1000;
    public const int PlanCollectionCompleted = 1001;
    public const int PlanCollectionFailed = 1002;
    public const int InstanceCollectionStarted = 1010;
    public const int InstanceCollectionCompleted = 1011;
    public const int InstanceCollectionFailed = 1012;
    public const int DatabaseCollectionStarted = 1020;
    public const int DatabaseCollectionCompleted = 1021;
    public const int DatabaseCollectionFailed = 1022;
    public const int SamplesCollected = 1030;
    public const int QueryStoreUsed = 1040;
    public const int DmvFallback = 1041;

    // Analysis (1100-1199)
    public const int AnalysisStarted = 1100;
    public const int AnalysisCompleted = 1101;
    public const int AnalysisFailed = 1102;
    public const int RegressionDetected = 1110;
    public const int RegressionResolved = 1111;
    public const int HotspotDetected = 1120;
    public const int BaselineComputed = 1130;

    // Alerting (1200-1299)
    public const int AlertSending = 1200;
    public const int AlertSent = 1201;
    public const int AlertFailed = 1202;
    public const int AlertSkipped = 1203;
    public const int DailySummarySending = 1210;
    public const int DailySummarySent = 1211;
    public const int DailySummaryFailed = 1212;

    // Remediation (1300-1399)
    public const int RemediationAdvised = 1300;
    public const int RemediationExecuting = 1310;
    public const int RemediationExecuted = 1311;
    public const int RemediationFailed = 1312;
    public const int RemediationSkipped = 1313;
    public const int RemediationDryRun = 1314;
    public const int RemediationBlocked = 1315;

    // Baseline Management (1400-1499)
    public const int BaselineRebuildStarted = 1400;
    public const int BaselineRebuildCompleted = 1401;
    public const int BaselineRebuildFailed = 1402;
    public const int BaselineUpdated = 1410;

    // Configuration/Startup (1500-1599)
    public const int ServiceStarting = 1500;
    public const int ServiceStarted = 1501;
    public const int ServiceStopping = 1502;
    public const int ServiceStopped = 1503;
    public const int ConfigurationLoaded = 1510;
    public const int ConfigurationValidationFailed = 1511;
    public const int FeatureFlagChanged = 1520;

    // Health Checks (1600-1699)
    public const int HealthCheckPassed = 1600;
    public const int HealthCheckFailed = 1601;
    public const int HealthCheckDegraded = 1602;

    // Database Connectivity (1700-1799)
    public const int ConnectionOpened = 1700;
    public const int ConnectionFailed = 1701;
    public const int ConnectionTimeout = 1702;
    public const int QueryExecuted = 1710;
    public const int QueryTimeout = 1711;
    public const int QueryFailed = 1712;
}

/// <summary>
/// Structured log property names for consistency.
/// </summary>
public static class LogPropertyNames
{
    // Instance/Database identification
    public const string InstanceName = "InstanceName";
    public const string DatabaseName = "DatabaseName";
    public const string QueryFingerprint = "QueryFingerprint";
    public const string QueryHash = "QueryHash";
    public const string PlanHandle = "PlanHandle";

    // Metrics
    public const string Duration = "Duration";
    public const string DurationMs = "DurationMs";
    public const string SampleCount = "SampleCount";
    public const string ExecutionCount = "ExecutionCount";
    public const string AvgDurationMs = "AvgDurationMs";
    public const string AvgCpuMs = "AvgCpuMs";
    public const string ChangePercent = "ChangePercent";

    // Detection
    public const string RegressionId = "RegressionId";
    public const string RegressionSeverity = "RegressionSeverity";
    public const string HotspotRank = "HotspotRank";

    // Remediation
    public const string RemediationId = "RemediationId";
    public const string RemediationType = "RemediationType";
    public const string SqlStatement = "SqlStatement";
    public const string IsDryRun = "IsDryRun";

    // Alerting
    public const string AlertChannel = "AlertChannel";
    public const string AlertRecipient = "AlertRecipient";

    // Job execution
    public const string JobName = "JobName";
    public const string JobIteration = "JobIteration";
    public const string ConsecutiveFailures = "ConsecutiveFailures";

    // Errors
    public const string ErrorCode = "ErrorCode";
    public const string ErrorMessage = "ErrorMessage";
    public const string StackTrace = "StackTrace";
}
