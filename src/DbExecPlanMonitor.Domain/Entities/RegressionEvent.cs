using DbExecPlanMonitor.Domain.Enums;

namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents a performance regression detected for a query.
/// This is a domain entity that tracks the lifecycle of a regression
/// from detection through resolution.
/// </summary>
public sealed class RegressionEvent
{
    /// <summary>
    /// Unique identifier for this regression event.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The query fingerprint that regressed.
    /// </summary>
    public required Guid FingerprintId { get; init; }

    /// <summary>
    /// Database instance where the regression was detected.
    /// </summary>
    public required string InstanceName { get; init; }

    /// <summary>
    /// Database name where the regression was detected.
    /// </summary>
    public required string DatabaseName { get; init; }

    /// <summary>
    /// When the regression was first detected.
    /// </summary>
    public required DateTime DetectedAtUtc { get; init; }

    /// <summary>
    /// Severity of the regression.
    /// </summary>
    public required RegressionSeverity Severity { get; init; }

    /// <summary>
    /// Current status in the workflow.
    /// </summary>
    public RegressionStatus Status { get; set; }

    // Baseline metrics (what we expected)
    public long? BaselineP95DurationUs { get; init; }
    public long? BaselineP95CpuTimeUs { get; init; }
    public long? BaselineAvgLogicalReads { get; init; }

    // Current metrics (what we observed)
    public long? CurrentP95DurationUs { get; init; }
    public long? CurrentP95CpuTimeUs { get; init; }
    public long? CurrentAvgLogicalReads { get; init; }

    // Change percentages
    public decimal? DurationChangePercent { get; init; }
    public decimal? CpuChangePercent { get; init; }

    /// <summary>
    /// Human-readable description of the regression.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Start of the sample window that triggered detection.
    /// </summary>
    public DateTime? SampleWindowStart { get; init; }

    /// <summary>
    /// End of the sample window that triggered detection.
    /// </summary>
    public DateTime? SampleWindowEnd { get; init; }

    /// <summary>
    /// Plan hash before regression (if detected).
    /// </summary>
    public byte[]? OldPlanHash { get; init; }

    /// <summary>
    /// Plan hash after regression (if different).
    /// </summary>
    public byte[]? NewPlanHash { get; init; }

    /// <summary>
    /// Whether the plan changed (potential plan regression).
    /// </summary>
    public bool IsPlanChange => OldPlanHash != null && NewPlanHash != null 
        && !OldPlanHash.SequenceEqual(NewPlanHash);

    // Workflow tracking
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }

    /// <summary>
    /// Acknowledges the regression event.
    /// </summary>
    public void Acknowledge(string acknowledgedBy)
    {
        if (Status != RegressionStatus.New)
            throw new InvalidOperationException($"Cannot acknowledge regression in status {Status}");

        Status = RegressionStatus.Acknowledged;
        AcknowledgedAtUtc = DateTime.UtcNow;
        AcknowledgedBy = acknowledgedBy;
    }

    /// <summary>
    /// Marks the regression as resolved.
    /// </summary>
    public void Resolve(string resolvedBy, string? notes = null)
    {
        if (Status == RegressionStatus.Resolved)
            throw new InvalidOperationException("Regression is already resolved");

        Status = RegressionStatus.Resolved;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedBy = resolvedBy;
        ResolutionNotes = notes;
    }

    /// <summary>
    /// Marks the regression as auto-resolved (performance returned to normal).
    /// </summary>
    public void AutoResolve()
    {
        Status = RegressionStatus.AutoResolved;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedBy = "System";
        ResolutionNotes = "Performance returned to baseline levels";
    }
}
