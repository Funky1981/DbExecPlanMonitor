using System.ComponentModel.DataAnnotations;
using DbExecPlanMonitor.Domain.Enums;

namespace DbExecPlanMonitor.Application.Configuration;

/// <summary>
/// Security and safety configuration options for the monitoring service.
/// Controls what operations are permitted and under what conditions.
/// </summary>
public class SecurityOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Monitoring:Security";

    /// <summary>
    /// The operational mode of the monitoring service.
    /// Default: ReadOnly (safest mode).
    /// </summary>
    public MonitoringMode Mode { get; set; } = MonitoringMode.ReadOnly;

    /// <summary>
    /// The environment type where the service is running.
    /// Default: Production (most restrictive).
    /// </summary>
    public EnvironmentType Environment { get; set; } = EnvironmentType.Production;

    /// <summary>
    /// Whether remediation actions are enabled at all.
    /// This is a global kill switch that overrides Mode setting.
    /// Default: false (disabled for safety).
    /// </summary>
    public bool EnableRemediation { get; set; } = false;

    /// <summary>
    /// Whether to run in dry-run mode (simulate but don't execute).
    /// When true, remediation actions are logged but not executed.
    /// Default: true (safe by default).
    /// </summary>
    public bool DryRunMode { get; set; } = true;

    /// <summary>
    /// Maximum number of remediations per hour per instance.
    /// Prevents runaway remediation in case of false positives.
    /// Default: 5.
    /// </summary>
    [Range(1, 100)]
    public int MaxRemediationsPerHour { get; set; } = 5;

    /// <summary>
    /// Cooldown period in minutes after a remediation before another can be applied
    /// to the same query/plan.
    /// Default: 60 minutes.
    /// </summary>
    [Range(1, 1440)]
    public int RemediationCooldownMinutes { get; set; } = 60;

    /// <summary>
    /// List of databases that are excluded from any remediation actions.
    /// System databases (master, msdb, model, tempdb) are always excluded.
    /// </summary>
    public List<string> ExcludedDatabases { get; set; } = new();

    /// <summary>
    /// List of query patterns (SQL text contains) that are excluded from remediation.
    /// </summary>
    public List<string> ExcludedQueryPatterns { get; set; } = new();

    /// <summary>
    /// Require explicit approval for remediations at or above this risk level.
    /// Default: Medium (Low-risk can be auto-applied if mode permits).
    /// </summary>
    public RemediationRiskLevel ApprovalThreshold { get; set; } = RemediationRiskLevel.Medium;

    /// <summary>
    /// Configuration for separation of duties between monitoring and remediation.
    /// </summary>
    public SeparationOfDutiesOptions SeparationOfDuties { get; set; } = new();

    /// <summary>
    /// Configuration for protective checks before remediation.
    /// </summary>
    public ProtectiveChecksOptions ProtectiveChecks { get; set; } = new();
}

/// <summary>
/// Options for separation of duties between monitoring and remediation accounts.
/// </summary>
public class SeparationOfDutiesOptions
{
    /// <summary>
    /// Whether to enforce separation of duties (different accounts for monitoring vs remediation).
    /// Default: false.
    /// </summary>
    public bool EnforceSeparation { get; set; } = false;

    /// <summary>
    /// Connection string name for the monitoring (read-only) account.
    /// </summary>
    public string MonitoringConnectionName { get; set; } = "MonitoringReadOnly";

    /// <summary>
    /// Connection string name for the remediation account.
    /// Only used when remediation is enabled.
    /// </summary>
    public string RemediationConnectionName { get; set; } = "RemediationWrite";
}

/// <summary>
/// Options for protective checks performed before executing remediation.
/// </summary>
public class ProtectiveChecksOptions
{
    /// <summary>
    /// Whether to check for active transactions on the affected objects before remediation.
    /// Default: true.
    /// </summary>
    public bool CheckActiveTransactions { get; set; } = true;

    /// <summary>
    /// Whether to check current server load before remediation.
    /// Default: true.
    /// </summary>
    public bool CheckServerLoad { get; set; } = true;

    /// <summary>
    /// Maximum CPU percentage threshold. Remediation is blocked if CPU exceeds this.
    /// Default: 80%.
    /// </summary>
    [Range(10, 100)]
    public int MaxCpuPercentage { get; set; } = 80;

    /// <summary>
    /// Whether to verify the plan regression still exists before remediation.
    /// Default: true.
    /// </summary>
    public bool VerifyRegressionExists { get; set; } = true;

    /// <summary>
    /// Whether to check if maintenance window is active before remediation.
    /// Default: false.
    /// </summary>
    public bool RequireMaintenanceWindow { get; set; } = false;

    /// <summary>
    /// Start hour of maintenance window (24-hour format, 0-23).
    /// Only used if RequireMaintenanceWindow is true.
    /// </summary>
    [Range(0, 23)]
    public int MaintenanceWindowStartHour { get; set; } = 2;

    /// <summary>
    /// End hour of maintenance window (24-hour format, 0-23).
    /// Only used if RequireMaintenanceWindow is true.
    /// </summary>
    [Range(0, 23)]
    public int MaintenanceWindowEndHour { get; set; } = 6;
}
