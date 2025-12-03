using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;

namespace DbExecPlanMonitor.Application.Tests.Fakes;

/// <summary>
/// In-memory implementation of IRemediationAuditRepository for testing.
/// </summary>
public class InMemoryRemediationAuditRepository : IRemediationAuditRepository
{
    private readonly List<RemediationAuditRecord> _records = new();

    /// <summary>
    /// Gets all stored records (for test assertions).
    /// </summary>
    public IReadOnlyList<RemediationAuditRecord> AllRecords => _records.AsReadOnly();

    /// <summary>
    /// Clears all records (for test isolation).
    /// </summary>
    public void Clear() => _records.Clear();

    /// <summary>
    /// Adds records for testing scenarios.
    /// </summary>
    public void AddRecord(RemediationAuditRecord record) => _records.Add(record);

    /// <summary>
    /// Adds multiple records for testing scenarios.
    /// </summary>
    public void AddRecords(IEnumerable<RemediationAuditRecord> records) => _records.AddRange(records);

    /// <inheritdoc />
    public Task SaveAsync(RemediationAuditRecord record, CancellationToken cancellationToken = default)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemediationAuditRecord>> GetByInstanceAsync(
        string instanceName,
        string databaseName,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var result = _records
            .Where(r => r.InstanceName.Equals(instanceName, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.DatabaseName.Equals(databaseName, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .OrderByDescending(r => r.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<RemediationAuditRecord>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemediationAuditRecord>> GetByQueryFingerprintAsync(
        string queryFingerprint,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = _records
            .Where(r => r.QueryFingerprint.Equals(queryFingerprint, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Timestamp)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<RemediationAuditRecord>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemediationAuditRecord>> GetRecentFailuresAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var result = _records
            .Where(r => !r.Success && r.Timestamp >= since)
            .OrderByDescending(r => r.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<RemediationAuditRecord>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemediationAuditRecord>> GetRecentAsync(
        string instanceName,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        var result = _records
            .Where(r => r.InstanceName.Equals(instanceName, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Timestamp >= cutoff)
            .OrderByDescending(r => r.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<RemediationAuditRecord>>(result);
    }

    /// <inheritdoc />
    public Task<RemediationAuditSummary> GetSummaryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var recordsInRange = _records
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .ToList();

        var byType = recordsInRange
            .GroupBy(r => r.RemediationType)
            .ToDictionary(g => g.Key, g => g.Count());

        var byInstance = recordsInRange
            .GroupBy(r => r.InstanceName)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult(new RemediationAuditSummary
        {
            TotalAttempts = recordsInRange.Count,
            SuccessCount = recordsInRange.Count(r => r.Success && !r.IsDryRun),
            FailureCount = recordsInRange.Count(r => !r.Success),
            DryRunCount = recordsInRange.Count(r => r.IsDryRun),
            ByType = byType,
            ByInstance = byInstance
        });
    }
}
