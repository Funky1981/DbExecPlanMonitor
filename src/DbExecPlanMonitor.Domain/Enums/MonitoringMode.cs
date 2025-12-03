namespace DbExecPlanMonitor.Domain.Enums;

/// <summary>
/// Defines the operational mode of the monitoring service, controlling what actions are permitted.
/// The mode acts as a safety rail to prevent unintended modifications to the database.
/// </summary>
public enum MonitoringMode
{
    /// <summary>
    /// Read-only mode: only collects plans, detects regressions, and sends alerts.
    /// No remediation actions are taken. This is the safest mode and the default.
    /// </summary>
    ReadOnly = 0,

    /// <summary>
    /// Suggestion mode: collects plans, detects regressions, sends alerts, and
    /// generates remediation suggestions but does not execute them automatically.
    /// Remediation scripts are logged and can be reviewed by DBAs.
    /// </summary>
    SuggestRemediation = 1,

    /// <summary>
    /// Auto-apply low-risk mode: automatically applies remediations classified as low-risk
    /// (e.g., clearing procedure cache for a specific plan, updating statistics).
    /// High-risk remediations (e.g., forcing plan guides, index changes) still require manual approval.
    /// </summary>
    AutoApplyLowRisk = 2
}
