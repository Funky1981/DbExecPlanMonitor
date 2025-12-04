using System;
using System.Threading;
using System.Threading.Tasks;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Repository for persisting and retrieving cumulative metrics snapshots.
/// Used to compute deltas between collection cycles.
/// 
/// DMVs and Query Store return cumulative counters (total executions, total CPU, etc.)
/// that accumulate since the plan was cached. To get meaningful per-interval metrics,
/// we must track the previous cumulative values and compute deltas.
/// </summary>
public interface ICumulativeMetricsSnapshotRepository
{
    /// <summary>
    /// Gets the last snapshot for a specific query plan.
    /// Returns null if no previous snapshot exists (first collection).
    /// </summary>
    /// <param name="instanceName">The SQL Server instance</param>
    /// <param name="databaseName">The database name</param>
    /// <param name="fingerprintId">The query fingerprint ID</param>
    /// <param name="planHash">The plan hash (nullable for DMV without plan hash)</param>
    /// <param name="ct">Cancellation token</param>
    Task<CumulativeMetricsSnapshot?> GetLastSnapshotAsync(
        string instanceName,
        string databaseName,
        Guid fingerprintId,
        byte[]? planHash,
        CancellationToken ct = default);

    /// <summary>
    /// Saves or updates the cumulative metrics snapshot for a query plan.
    /// Uses upsert semantics - creates if not exists, updates if exists.
    /// </summary>
    Task SaveSnapshotAsync(CumulativeMetricsSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Deletes old snapshots to prevent unbounded growth.
    /// Removes snapshots not updated within the specified period.
    /// </summary>
    /// <param name="olderThan">Delete snapshots last updated before this date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of snapshots deleted</returns>
    Task<int> PurgeStaleSnapshotsAsync(DateTime olderThan, CancellationToken ct = default);
}

/// <summary>
/// Represents the last-seen cumulative metrics for a query plan.
/// Used as a baseline to compute deltas when new cumulative values arrive.
/// </summary>
public sealed class CumulativeMetricsSnapshot
{
    /// <summary>
    /// Unique identifier for this snapshot record.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// SQL Server instance name.
    /// </summary>
    public required string InstanceName { get; init; }

    /// <summary>
    /// Database name.
    /// </summary>
    public required string DatabaseName { get; init; }

    /// <summary>
    /// The query fingerprint this snapshot belongs to.
    /// </summary>
    public required Guid FingerprintId { get; init; }

    /// <summary>
    /// Plan hash (nullable for older DMV entries without plan hash).
    /// Combined with FingerprintId for unique identification.
    /// </summary>
    public byte[]? PlanHash { get; init; }

    /// <summary>
    /// When this snapshot was captured.
    /// </summary>
    public required DateTime SnapshotTimeUtc { get; init; }

    // Cumulative execution count
    public required long ExecutionCount { get; init; }

    // Cumulative CPU time in microseconds
    public required long TotalCpuTimeUs { get; init; }

    // Cumulative duration in microseconds
    public required long TotalDurationUs { get; init; }

    // Cumulative logical reads
    public required long TotalLogicalReads { get; init; }

    // Cumulative logical writes
    public long TotalLogicalWrites { get; init; }

    // Cumulative physical reads
    public long TotalPhysicalReads { get; init; }

    /// <summary>
    /// Computes delta metrics between this snapshot and new cumulative values.
    /// Handles counter resets (when current &lt; previous) by treating current as the delta.
    /// </summary>
    /// <param name="currentExecution">Current cumulative execution count</param>
    /// <param name="currentCpuUs">Current cumulative CPU time</param>
    /// <param name="currentDurationUs">Current cumulative duration</param>
    /// <param name="currentLogicalReads">Current cumulative logical reads</param>
    /// <param name="currentLogicalWrites">Current cumulative logical writes</param>
    /// <param name="currentPhysicalReads">Current cumulative physical reads</param>
    /// <returns>Delta values; returns current values if reset detected</returns>
    public DeltaMetrics ComputeDelta(
        long currentExecution,
        long currentCpuUs,
        long currentDurationUs,
        long currentLogicalReads,
        long currentLogicalWrites,
        long currentPhysicalReads)
    {
        // Detect counter reset: if any key counter is less than previous,
        // the plan was evicted and re-cached. In this case, use current as delta.
        bool isReset = currentExecution < ExecutionCount ||
                       currentCpuUs < TotalCpuTimeUs ||
                       currentDurationUs < TotalDurationUs;

        if (isReset)
        {
            return new DeltaMetrics
            {
                ExecutionCountDelta = currentExecution,
                CpuTimeUsDelta = currentCpuUs,
                DurationUsDelta = currentDurationUs,
                LogicalReadsDelta = currentLogicalReads,
                LogicalWritesDelta = currentLogicalWrites,
                PhysicalReadsDelta = currentPhysicalReads,
                WasReset = true
            };
        }

        return new DeltaMetrics
        {
            ExecutionCountDelta = currentExecution - ExecutionCount,
            CpuTimeUsDelta = currentCpuUs - TotalCpuTimeUs,
            DurationUsDelta = currentDurationUs - TotalDurationUs,
            LogicalReadsDelta = currentLogicalReads - TotalLogicalReads,
            LogicalWritesDelta = currentLogicalWrites - TotalLogicalWrites,
            PhysicalReadsDelta = currentPhysicalReads - TotalPhysicalReads,
            WasReset = false
        };
    }
}

/// <summary>
/// Computed delta values between two cumulative snapshots.
/// </summary>
public readonly struct DeltaMetrics
{
    public long ExecutionCountDelta { get; init; }
    public long CpuTimeUsDelta { get; init; }
    public long DurationUsDelta { get; init; }
    public long LogicalReadsDelta { get; init; }
    public long LogicalWritesDelta { get; init; }
    public long PhysicalReadsDelta { get; init; }

    /// <summary>
    /// True if a counter reset was detected (plan eviction/re-cache).
    /// When true, delta values represent the current totals, not true deltas.
    /// </summary>
    public bool WasReset { get; init; }

    /// <summary>
    /// Returns true if there was any execution activity in this period.
    /// </summary>
    public bool HasActivity => ExecutionCountDelta > 0;
}
