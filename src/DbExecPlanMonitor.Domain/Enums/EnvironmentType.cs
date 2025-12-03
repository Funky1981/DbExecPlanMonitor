namespace DbExecPlanMonitor.Domain.Enums;

/// <summary>
/// Defines the environment type where the monitoring service is running.
/// Environment type affects safety thresholds and what operations are permitted.
/// </summary>
public enum EnvironmentType
{
    /// <summary>
    /// Development environment. Most permissive settings allowed.
    /// </summary>
    Development = 0,

    /// <summary>
    /// Test/QA environment. Slightly more restrictive than Development.
    /// </summary>
    Test = 1,

    /// <summary>
    /// Staging/Pre-production environment. Near-production restrictions.
    /// </summary>
    Staging = 2,

    /// <summary>
    /// Production environment. Most restrictive settings enforced.
    /// Additional safety checks and approvals may be required.
    /// </summary>
    Production = 3
}
