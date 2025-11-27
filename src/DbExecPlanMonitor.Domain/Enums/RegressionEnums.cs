namespace DbExecPlanMonitor.Domain.Enums;

/// <summary>
/// Severity level of a detected regression.
/// Used for prioritization and alerting thresholds.
/// </summary>
public enum RegressionSeverity
{
    /// <summary>
    /// Minor regression (< 2x increase).
    /// May not require immediate action.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Moderate regression (2x-3x increase).
    /// Should be investigated during normal work hours.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Significant regression (3x-6x increase).
    /// Should be investigated promptly.
    /// </summary>
    High = 2,

    /// <summary>
    /// Severe regression (> 6x increase).
    /// May require immediate attention.
    /// </summary>
    Critical = 3
}

/// <summary>
/// Workflow status for a regression event.
/// </summary>
public enum RegressionStatus
{
    /// <summary>
    /// Newly detected, not yet acknowledged.
    /// </summary>
    New = 0,

    /// <summary>
    /// Someone has acknowledged the regression and is investigating.
    /// </summary>
    Acknowledged = 1,

    /// <summary>
    /// The regression has been manually resolved.
    /// </summary>
    Resolved = 2,

    /// <summary>
    /// The regression resolved itself (performance returned to normal).
    /// </summary>
    AutoResolved = 3,

    /// <summary>
    /// The regression was a false positive and dismissed.
    /// </summary>
    Dismissed = 4
}
