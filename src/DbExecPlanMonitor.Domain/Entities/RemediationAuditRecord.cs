namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents an audit record for remediation actions.
/// </summary>
/// <remarks>
/// This entity captures all remediation attempts (successful or not) for:
/// - Compliance and audit trails
/// - Debugging and troubleshooting
/// - Historical analysis of automated actions
/// 
/// Even dry-run remediations are recorded to show what would have happened.
/// </remarks>
public sealed class RemediationAuditRecord
{
    /// <summary>
    /// Unique identifier for this audit record.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Timestamp when the remediation was attempted.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The SQL Server instance name.
    /// </summary>
    public required string InstanceName { get; init; }

    /// <summary>
    /// The database name.
    /// </summary>
    public required string DatabaseName { get; init; }

    /// <summary>
    /// The query fingerprint being remediated.
    /// </summary>
    public required string QueryFingerprint { get; init; }

    /// <summary>
    /// The query hash (if available).
    /// </summary>
    public string? QueryHash { get; init; }

    /// <summary>
    /// Reference to the regression event that triggered this remediation.
    /// </summary>
    public Guid? RegressionEventId { get; init; }

    /// <summary>
    /// Reference to the remediation suggestion.
    /// </summary>
    public Guid? RemediationSuggestionId { get; init; }

    /// <summary>
    /// Type of remediation action.
    /// </summary>
    public required string RemediationType { get; init; }

    /// <summary>
    /// The T-SQL statement that was (or would be) executed.
    /// Sanitized to remove sensitive data if needed.
    /// </summary>
    public required string SqlStatement { get; init; }

    /// <summary>
    /// Whether this was a dry run (logged but not executed).
    /// </summary>
    public bool IsDryRun { get; init; }

    /// <summary>
    /// Whether the remediation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the remediation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// SQL Server error number if applicable.
    /// </summary>
    public int? SqlErrorNumber { get; init; }

    /// <summary>
    /// Duration of the remediation execution.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// The user/service account that initiated the remediation.
    /// </summary>
    public string? InitiatedBy { get; init; }

    /// <summary>
    /// Additional context or notes.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Machine name where the service is running.
    /// </summary>
    public string? MachineName { get; init; }

    /// <summary>
    /// Service version for tracking.
    /// </summary>
    public string? ServiceVersion { get; init; }
}
