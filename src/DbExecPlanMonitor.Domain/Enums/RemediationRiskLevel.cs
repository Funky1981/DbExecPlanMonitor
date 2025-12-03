namespace DbExecPlanMonitor.Domain.Enums;

/// <summary>
/// Classifies the risk level of a remediation action.
/// Used to determine whether automatic execution is permitted.
/// </summary>
public enum RemediationRiskLevel
{
    /// <summary>
    /// Low-risk remediation that is easily reversible and has minimal impact.
    /// Examples: clearing plan cache for a specific query, updating statistics.
    /// May be auto-applied in AutoApplyLowRisk mode.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Medium-risk remediation with potential side effects but generally safe.
    /// Examples: creating a plan guide, forcing a specific plan.
    /// Requires explicit approval even in AutoApplyLowRisk mode.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// High-risk remediation with significant potential impact.
    /// Examples: creating/dropping indexes, modifying stored procedures.
    /// Always requires manual approval and review.
    /// </summary>
    High = 2,

    /// <summary>
    /// Critical-risk remediation that could affect database availability or integrity.
    /// Examples: schema changes, partition modifications.
    /// Requires elevated approval and change management process.
    /// </summary>
    Critical = 3
}
