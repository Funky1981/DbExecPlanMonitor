namespace DbExecPlanMonitor.Domain.ValueObjects;

/// <summary>
/// Configuration for regression detection thresholds.
/// Immutable value object that can be attached to a baseline or database.
/// </summary>
public sealed class ThresholdConfiguration : IEquatable<ThresholdConfiguration>
{
    /// <summary>
    /// Multiplier for CPU time regression (e.g., 2.0 = alert at 2x baseline).
    /// </summary>
    public double CpuTimeMultiplier { get; }

    /// <summary>
    /// Multiplier for duration regression.
    /// </summary>
    public double DurationMultiplier { get; }

    /// <summary>
    /// Multiplier for logical reads regression.
    /// </summary>
    public double LogicalReadsMultiplier { get; }

    /// <summary>
    /// Multiplier for physical reads regression.
    /// </summary>
    public double PhysicalReadsMultiplier { get; }

    /// <summary>
    /// Minimum samples required before triggering regression.
    /// </summary>
    public int MinimumSamples { get; }

    /// <summary>
    /// Default thresholds (2x CPU/Duration, 3x Reads, 3 samples).
    /// </summary>
    public static ThresholdConfiguration Default => new(2.0, 2.0, 3.0, 5.0, 3);

    /// <summary>
    /// Strict thresholds for critical queries (lower tolerance).
    /// </summary>
    public static ThresholdConfiguration Strict => new(1.5, 1.5, 2.0, 3.0, 2);

    /// <summary>
    /// Relaxed thresholds for less critical queries (higher tolerance).
    /// </summary>
    public static ThresholdConfiguration Relaxed => new(3.0, 3.0, 5.0, 10.0, 5);

    /// <summary>
    /// Creates a new threshold configuration.
    /// </summary>
    public ThresholdConfiguration(
        double cpuTimeMultiplier,
        double durationMultiplier,
        double logicalReadsMultiplier,
        double physicalReadsMultiplier,
        int minimumSamples)
    {
        if (cpuTimeMultiplier < 1.0)
            throw new ArgumentException("CPU time multiplier must be at least 1.0", nameof(cpuTimeMultiplier));
        if (durationMultiplier < 1.0)
            throw new ArgumentException("Duration multiplier must be at least 1.0", nameof(durationMultiplier));
        if (logicalReadsMultiplier < 1.0)
            throw new ArgumentException("Logical reads multiplier must be at least 1.0", nameof(logicalReadsMultiplier));
        if (physicalReadsMultiplier < 1.0)
            throw new ArgumentException("Physical reads multiplier must be at least 1.0", nameof(physicalReadsMultiplier));
        if (minimumSamples < 1)
            throw new ArgumentException("Minimum samples must be at least 1", nameof(minimumSamples));

        CpuTimeMultiplier = cpuTimeMultiplier;
        DurationMultiplier = durationMultiplier;
        LogicalReadsMultiplier = logicalReadsMultiplier;
        PhysicalReadsMultiplier = physicalReadsMultiplier;
        MinimumSamples = minimumSamples;
    }

    /// <summary>
    /// Creates a new configuration with adjusted CPU threshold.
    /// </summary>
    public ThresholdConfiguration WithCpuTimeMultiplier(double multiplier)
    {
        return new ThresholdConfiguration(multiplier, DurationMultiplier, LogicalReadsMultiplier, PhysicalReadsMultiplier, MinimumSamples);
    }

    /// <summary>
    /// Creates a new configuration with adjusted duration threshold.
    /// </summary>
    public ThresholdConfiguration WithDurationMultiplier(double multiplier)
    {
        return new ThresholdConfiguration(CpuTimeMultiplier, multiplier, LogicalReadsMultiplier, PhysicalReadsMultiplier, MinimumSamples);
    }

    /// <summary>
    /// Creates a new configuration with adjusted minimum samples.
    /// </summary>
    public ThresholdConfiguration WithMinimumSamples(int samples)
    {
        return new ThresholdConfiguration(CpuTimeMultiplier, DurationMultiplier, LogicalReadsMultiplier, PhysicalReadsMultiplier, samples);
    }

    /// <summary>
    /// Checks if a ratio exceeds the threshold for a given metric.
    /// </summary>
    public bool ExceedsThreshold(MetricType metricType, double ratio)
    {
        return metricType switch
        {
            MetricType.CpuTime => ratio >= CpuTimeMultiplier,
            MetricType.Duration => ratio >= DurationMultiplier,
            MetricType.LogicalReads => ratio >= LogicalReadsMultiplier,
            MetricType.PhysicalReads => ratio >= PhysicalReadsMultiplier,
            _ => false
        };
    }

    public override string ToString()
    {
        return $"CPU: {CpuTimeMultiplier}x, Duration: {DurationMultiplier}x, Reads: {LogicalReadsMultiplier}x, Min Samples: {MinimumSamples}";
    }

    public bool Equals(ThresholdConfiguration? other)
    {
        if (other is null) return false;
        return CpuTimeMultiplier == other.CpuTimeMultiplier
            && DurationMultiplier == other.DurationMultiplier
            && LogicalReadsMultiplier == other.LogicalReadsMultiplier
            && PhysicalReadsMultiplier == other.PhysicalReadsMultiplier
            && MinimumSamples == other.MinimumSamples;
    }

    public override bool Equals(object? obj) => Equals(obj as ThresholdConfiguration);

    public override int GetHashCode() => HashCode.Combine(
        CpuTimeMultiplier, DurationMultiplier, LogicalReadsMultiplier, PhysicalReadsMultiplier, MinimumSamples);

    public static bool operator ==(ThresholdConfiguration? left, ThresholdConfiguration? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ThresholdConfiguration? left, ThresholdConfiguration? right) => !(left == right);
}

/// <summary>
/// Metric type for threshold comparison.
/// (Duplicated here for value object self-containment; also defined in PlanMetricSample)
/// </summary>
public enum MetricType
{
    CpuTime,
    Duration,
    LogicalReads,
    PhysicalReads,
    RowsReturned
}
