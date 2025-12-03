namespace DbExecPlanMonitor.Application.Configuration;

/// <summary>
/// Root configuration for the monitoring system.
/// This is the unified entry point for all monitoring-related configuration.
/// </summary>
/// <remarks>
/// Bound from appsettings.json "Monitoring" section.
/// 
/// Example:
/// <code>
/// {
///   "Monitoring": {
///     "Instances": [...],
///     "Jobs": {...},
///     "Analysis": {...},
///     "FeatureFlags": {...}
///   }
/// }
/// </code>
/// </remarks>
public sealed class MonitoringOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Monitoring";

    /// <summary>
    /// Global sampling interval override (seconds).
    /// Instance-level settings take precedence if specified.
    /// </summary>
    public int DefaultSamplingIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// How many days of historical data to retain in our own storage.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Enable debug logging for SQL queries.
    /// </summary>
    public bool LogSqlQueries { get; set; } = false;

    /// <summary>
    /// Maximum concurrent connections across all instances.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 10;

    /// <summary>
    /// Feature flags for controlling system behavior.
    /// </summary>
    public FeatureFlagOptions FeatureFlags { get; set; } = new();
}

/// <summary>
/// Feature flags for controlling system behavior at runtime.
/// </summary>
/// <remarks>
/// Feature flags provide safety rails for:
/// - Enabling/disabling new features
/// - Controlling remediation execution
/// - Environment-specific behavior
/// 
/// Can be configured via:
/// - appsettings.json
/// - Environment variables (e.g., MONITORING__FEATUREFLAGS__ENABLEREMEDIATION=true)
/// - Azure Key Vault or other secret stores
/// </remarks>
public sealed class FeatureFlagOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Monitoring:FeatureFlags";

    /// <summary>
    /// Whether the plan collection job is enabled.
    /// Default: true.
    /// </summary>
    public bool EnablePlanCollection { get; set; } = true;

    /// <summary>
    /// Whether the analysis job (regression/hotspot detection) is enabled.
    /// Default: true.
    /// </summary>
    public bool EnableAnalysis { get; set; } = true;

    /// <summary>
    /// Whether baseline rebuild is enabled.
    /// Default: true.
    /// </summary>
    public bool EnableBaselineRebuild { get; set; } = true;

    /// <summary>
    /// Whether daily summary reports are enabled.
    /// Default: true.
    /// </summary>
    public bool EnableDailySummary { get; set; } = true;

    /// <summary>
    /// Whether alerting is enabled globally.
    /// Default: true.
    /// </summary>
    public bool EnableAlerting { get; set; } = true;

    /// <summary>
    /// Whether automated remediation execution is enabled.
    /// Should be false in production until thoroughly tested.
    /// Default: false.
    /// </summary>
    public bool EnableRemediation { get; set; } = false;

    /// <summary>
    /// Whether to allow remediation on production-tagged instances.
    /// Additional safety rail for production environments.
    /// Default: false.
    /// </summary>
    public bool AllowProductionRemediation { get; set; } = false;

    /// <summary>
    /// Whether to enable dry-run mode for remediation.
    /// When true, remediation actions are logged but not executed.
    /// Default: true.
    /// </summary>
    public bool RemediationDryRun { get; set; } = true;

    /// <summary>
    /// Whether to enable Query Store as the preferred data source.
    /// Falls back to DMVs if disabled.
    /// Default: true.
    /// </summary>
    public bool PreferQueryStore { get; set; } = true;

    /// <summary>
    /// Whether to enable health check endpoints.
    /// Default: true.
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// Whether to enable detailed error messages in responses.
    /// Should be false in production.
    /// Default: false.
    /// </summary>
    public bool EnableDetailedErrors { get; set; } = false;
}
