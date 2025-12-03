namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents a performance baseline for a query fingerprint.
/// Baselines are computed from historical data and used for regression detection.
/// </summary>
public sealed class PlanBaseline
{
    /// <summary>
    /// Unique identifier for this baseline.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The query fingerprint this baseline applies to.
    /// </summary>
    public required Guid FingerprintId { get; init; }

    /// <summary>
    /// Database instance.
    /// </summary>
    public required string InstanceName { get; init; }

    /// <summary>
    /// Database name.
    /// </summary>
    public required string DatabaseName { get; init; }

    /// <summary>
    /// When this baseline was computed.
    /// </summary>
    public required DateTime ComputedAtUtc { get; init; }

    /// <summary>
    /// Start of the time window used to compute this baseline.
    /// </summary>
    public required DateTime WindowStartUtc { get; init; }

    /// <summary>
    /// End of the time window used to compute this baseline.
    /// </summary>
    public required DateTime WindowEndUtc { get; init; }

    /// <summary>
    /// Number of samples used to compute this baseline.
    /// Higher = more reliable.
    /// </summary>
    public required int SampleCount { get; init; }

    /// <summary>
    /// Total executions observed in the baseline window.
    /// </summary>
    public required long TotalExecutions { get; init; }

    // Duration metrics (microseconds)
    public long MedianDurationUs { get; init; }
    public long? P95DurationUs { get; init; }
    public long? P99DurationUs { get; init; }
    public long AvgDurationUs { get; init; }
    public long? MinDurationUs { get; init; }
    public long? MaxDurationUs { get; init; }

    // CPU metrics (microseconds)
    public long MedianCpuTimeUs { get; init; }
    public long? P95CpuTimeUs { get; init; }
    public long AvgCpuTimeUs { get; init; }

    // I/O metrics
    public long AvgLogicalReads { get; init; }
    public long? MaxLogicalReads { get; init; }
    public long AvgPhysicalReads { get; init; }
    public long AvgLogicalWrites { get; init; }
    public long? MaxLogicalWrites { get; init; }
    public long? AvgSpillsKb { get; init; }
    public long? MaxSpillsKb { get; init; }

    // Standard deviation for variance detection
    public double? DurationStdDev { get; init; }
    public double? CpuTimeStdDev { get; init; }

    /// <summary>
    /// Whether this baseline is currently active (not superseded).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this baseline was superseded by a newer one.
    /// </summary>
    public DateTime? SupersededAtUtc { get; set; }

    /// <summary>
    /// Plan hash that was typical during this baseline period.
    /// </summary>
    public byte[]? TypicalPlanHash { get; init; }

    /// <summary>
    /// Convenience: median duration in milliseconds.
    /// </summary>
    public double MedianDurationMs => MedianDurationUs / 1000.0;

    /// <summary>
    /// Convenience: P95 duration in milliseconds.
    /// </summary>
    public double? P95DurationMs => P95DurationUs / 1000.0;

    /// <summary>
    /// Convenience: median CPU time in milliseconds.
    /// </summary>
    public double MedianCpuTimeMs => MedianCpuTimeUs / 1000.0;

    /// <summary>
    /// Checks if the baseline has enough data to be reliable.
    /// </summary>
    public bool IsReliable(int minimumSamples = 10) => SampleCount >= minimumSamples;

    /// <summary>
    /// Marks this baseline as superseded by a newer one.
    /// </summary>
    public void Supersede()
    {
        IsActive = false;
        SupersededAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the coefficient of variation (CV) for duration.
    /// Lower CV = more consistent performance.
    /// </summary>
    public double? DurationCoefficientOfVariation =>
        DurationStdDev.HasValue && AvgDurationUs > 0
            ? DurationStdDev.Value / AvgDurationUs
            : null;
}
