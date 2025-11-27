using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Application service for computing and managing query baselines.
/// Coordinates between repositories and domain logic.
/// </summary>
public interface IBaselineService
{
    /// <summary>
    /// Computes a new baseline for a query fingerprint from recent samples.
    /// </summary>
    /// <param name="fingerprintId">The query fingerprint to baseline</param>
    /// <param name="lookbackDays">Number of days to include in baseline calculation</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The computed baseline, or null if insufficient data</returns>
    Task<PlanBaseline?> ComputeBaselineAsync(
        Guid fingerprintId,
        int lookbackDays = 7,
        CancellationToken ct = default);

    /// <summary>
    /// Computes baselines for all active fingerprints in a database.
    /// </summary>
    /// <param name="instanceName">Instance name</param>
    /// <param name="databaseName">Database name</param>
    /// <param name="lookbackDays">Number of days to include</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Summary of baseline computation</returns>
    Task<BaselineComputationResult> ComputeBaselinesForDatabaseAsync(
        string instanceName,
        string databaseName,
        int lookbackDays = 7,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current active baseline for a fingerprint.
    /// </summary>
    Task<PlanBaseline?> GetActiveBaselineAsync(
        Guid fingerprintId,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a fingerprint needs a baseline refresh.
    /// </summary>
    Task<bool> NeedsRefreshAsync(
        Guid fingerprintId,
        TimeSpan maxAge,
        CancellationToken ct = default);
}

/// <summary>
/// Result of computing baselines for a database.
/// </summary>
public sealed class BaselineComputationResult
{
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required DateTime ComputedAtUtc { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of fingerprints processed.
    /// </summary>
    public int FingerprintsProcessed { get; init; }

    /// <summary>
    /// Number of new baselines created.
    /// </summary>
    public int BaselinesCreated { get; init; }

    /// <summary>
    /// Number of existing baselines updated.
    /// </summary>
    public int BaselinesUpdated { get; init; }

    /// <summary>
    /// Number of fingerprints skipped (insufficient data).
    /// </summary>
    public int Skipped { get; init; }

    /// <summary>
    /// Number of errors encountered.
    /// </summary>
    public int Errors { get; init; }

    /// <summary>
    /// Error messages if any.
    /// </summary>
    public IReadOnlyList<string>? ErrorMessages { get; init; }
}
