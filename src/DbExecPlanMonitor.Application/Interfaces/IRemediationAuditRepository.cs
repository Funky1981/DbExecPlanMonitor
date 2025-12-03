using DbExecPlanMonitor.Domain.Entities;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Repository for persisting remediation audit records.
/// </summary>
public interface IRemediationAuditRepository
{
    /// <summary>
    /// Saves a remediation audit record.
    /// </summary>
    /// <param name="record">The audit record to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(RemediationAuditRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit records for a specific instance and database.
    /// </summary>
    /// <param name="instanceName">The instance name.</param>
    /// <param name="databaseName">The database name.</param>
    /// <param name="from">Start of time range.</param>
    /// <param name="to">End of time range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RemediationAuditRecord>> GetByInstanceAsync(
        string instanceName,
        string databaseName,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit records for a specific query fingerprint.
    /// </summary>
    /// <param name="queryFingerprint">The query fingerprint.</param>
    /// <param name="limit">Maximum records to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RemediationAuditRecord>> GetByQueryFingerprintAsync(
        string queryFingerprint,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent failed remediations for alerting/review.
    /// </summary>
    /// <param name="since">Time to look back from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RemediationAuditRecord>> GetRecentFailuresAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets summary statistics for a time period.
    /// </summary>
    /// <param name="from">Start of time range.</param>
    /// <param name="to">End of time range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RemediationAuditSummary> GetSummaryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Summary statistics for remediation audits.
/// </summary>
public sealed record RemediationAuditSummary
{
    /// <summary>
    /// Total number of remediation attempts.
    /// </summary>
    public int TotalAttempts { get; init; }

    /// <summary>
    /// Number of successful remediations.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of failed remediations.
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Number of dry-run remediations.
    /// </summary>
    public int DryRunCount { get; init; }

    /// <summary>
    /// Success rate as a percentage.
    /// </summary>
    public double SuccessRate =>
        TotalAttempts > 0 ? (double)SuccessCount / (TotalAttempts - DryRunCount) * 100 : 0;

    /// <summary>
    /// Breakdown by remediation type.
    /// </summary>
    public IReadOnlyDictionary<string, int> ByType { get; init; } =
        new Dictionary<string, int>();

    /// <summary>
    /// Breakdown by instance.
    /// </summary>
    public IReadOnlyDictionary<string, int> ByInstance { get; init; } =
        new Dictionary<string, int>();
}
