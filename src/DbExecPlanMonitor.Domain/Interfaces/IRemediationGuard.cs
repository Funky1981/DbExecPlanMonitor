using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;

namespace DbExecPlanMonitor.Domain.Interfaces;

/// <summary>
/// Result of a remediation guard check, indicating whether an action is permitted.
/// </summary>
public record RemediationGuardResult
{
    /// <summary>
    /// Whether the remediation action is permitted to proceed.
    /// </summary>
    public bool IsPermitted { get; init; }

    /// <summary>
    /// Reason for the decision. Contains explanation if denied, or confirmation if permitted.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Suggested alternative action if the requested action is denied.
    /// </summary>
    public string? SuggestedAlternative { get; init; }

    /// <summary>
    /// Whether this is a dry-run (simulation) mode where no actual changes will be made.
    /// </summary>
    public bool IsDryRun { get; init; }

    /// <summary>
    /// Creates a permitted result.
    /// </summary>
    public static RemediationGuardResult Permitted(string reason, bool isDryRun = false) =>
        new() { IsPermitted = true, Reason = reason, IsDryRun = isDryRun };

    /// <summary>
    /// Creates a denied result.
    /// </summary>
    public static RemediationGuardResult Denied(string reason, string? suggestedAlternative = null) =>
        new() { IsPermitted = false, Reason = reason, SuggestedAlternative = suggestedAlternative };
}

/// <summary>
/// Guards remediation actions by enforcing safety rails based on monitoring mode,
/// environment type, risk level, and other protective checks.
/// </summary>
public interface IRemediationGuard
{
    /// <summary>
    /// Checks whether a remediation action is permitted based on current configuration
    /// and the properties of the remediation.
    /// </summary>
    /// <param name="instanceName">Name of the SQL Server instance.</param>
    /// <param name="databaseName">Name of the database.</param>
    /// <param name="remediationType">Type of remediation being requested.</param>
    /// <param name="riskLevel">Risk level of the remediation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether the action is permitted and why.</returns>
    Task<RemediationGuardResult> CheckAsync(
        string instanceName,
        string databaseName,
        RemediationType remediationType,
        RemediationRiskLevel riskLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current monitoring mode.
    /// </summary>
    MonitoringMode CurrentMode { get; }

    /// <summary>
    /// Gets the current environment type.
    /// </summary>
    EnvironmentType CurrentEnvironment { get; }

    /// <summary>
    /// Checks if remediation is globally enabled.
    /// </summary>
    bool IsRemediationEnabled { get; }

    /// <summary>
    /// Checks if dry-run mode is active (simulates but doesn't execute).
    /// </summary>
    bool IsDryRunMode { get; }
}
