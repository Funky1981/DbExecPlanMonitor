namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents a captured execution plan for a query.
/// SQL Server may generate different plans for the same query over time,
/// based on statistics, parameter values, memory pressure, etc.
/// </summary>
/// <remarks>
/// The execution plan XML contains the full details of HOW SQL Server
/// will execute the query: which indexes to use, join strategies,
/// parallelism, memory grants, etc.
/// 
/// Plan changes are often the root cause of performance regressions.
/// </remarks>
public class ExecutionPlanSnapshot
{
    /// <summary>
    /// Unique identifier for this plan snapshot.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the query fingerprint this plan belongs to.
    /// </summary>
    public Guid QueryFingerprintId { get; private set; }

    /// <summary>
    /// Navigation property to the parent fingerprint.
    /// </summary>
    public QueryFingerprint QueryFingerprint { get; private set; } = null!;

    /// <summary>
    /// SQL Server's plan_hash (or plan_id from Query Store).
    /// Different plans for the same query have different hashes.
    /// Stored as hex string (e.g., "0x1A2B3C4D...").
    /// </summary>
    public string PlanHash { get; private set; }

    /// <summary>
    /// The full execution plan in XML format (showplan XML).
    /// This can be opened in SSMS or Plan Explorer for visualization.
    /// </summary>
    public string PlanXml { get; private set; }

    /// <summary>
    /// The estimated subtree cost from the optimizer.
    /// This is the optimizer's guess at how expensive the plan is.
    /// Lower is generally better, but it's an estimate, not reality.
    /// </summary>
    public double EstimatedCost { get; private set; }

    /// <summary>
    /// Estimated number of rows the query will return.
    /// Large differences between estimated and actual = bad statistics.
    /// </summary>
    public double? EstimatedRows { get; private set; }

    /// <summary>
    /// The degree of parallelism (DOP) for this plan.
    /// 1 = serial execution, >1 = parallel execution.
    /// </summary>
    public int? DegreeOfParallelism { get; private set; }

    /// <summary>
    /// Whether this plan requests a memory grant for sorts/hashes.
    /// Memory grants can cause RESOURCE_SEMAPHORE waits if too high.
    /// </summary>
    public bool HasMemoryGrant { get; private set; }

    /// <summary>
    /// The requested memory grant in KB, if applicable.
    /// </summary>
    public long? MemoryGrantKb { get; private set; }

    /// <summary>
    /// Whether this plan uses an index seek (good) vs scan (often bad).
    /// Extracted from plan analysis.
    /// </summary>
    public bool UsesIndexSeek { get; private set; }

    /// <summary>
    /// Whether this plan has a table/clustered index scan.
    /// Scans on large tables are often problematic.
    /// </summary>
    public bool HasTableScan { get; private set; }

    /// <summary>
    /// Whether there are missing index recommendations in the plan.
    /// SQL Server embeds these hints in the XML.
    /// </summary>
    public bool HasMissingIndexHint { get; private set; }

    /// <summary>
    /// Whether there are implicit conversions causing issues.
    /// Often caused by parameter type mismatches.
    /// </summary>
    public bool HasImplicitConversion { get; private set; }

    /// <summary>
    /// Whether this plan uses parallelism.
    /// </summary>
    public bool IsParallel { get; private set; }

    /// <summary>
    /// Whether this plan is currently being used by SQL Server.
    /// Plans can be evicted from cache but we keep the snapshot.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// When we first captured this plan.
    /// </summary>
    public DateTime CapturedAtUtc { get; private set; }

    /// <summary>
    /// When we last observed this plan being used.
    /// </summary>
    public DateTime LastSeenAtUtc { get; private set; }

    /// <summary>
    /// The performance metrics samples for this specific plan.
    /// Tracks how the plan performs over time.
    /// </summary>
    private readonly List<PlanMetricSample> _metricSamples = new();
    public IReadOnlyCollection<PlanMetricSample> MetricSamples => _metricSamples.AsReadOnly();

    // Private constructor for EF Core
    private ExecutionPlanSnapshot()
    {
        PlanHash = null!;
        PlanXml = null!;
    }

    /// <summary>
    /// Creates a new execution plan snapshot. Called by QueryFingerprint.AddPlanSnapshot().
    /// </summary>
    internal ExecutionPlanSnapshot(
        QueryFingerprint fingerprint,
        string planHash,
        string planXml,
        double estimatedCost)
    {
        if (fingerprint == null)
            throw new ArgumentNullException(nameof(fingerprint));
        if (string.IsNullOrWhiteSpace(planHash))
            throw new ArgumentException("Plan hash is required.", nameof(planHash));
        if (string.IsNullOrWhiteSpace(planXml))
            throw new ArgumentException("Plan XML is required.", nameof(planXml));

        Id = Guid.NewGuid();
        QueryFingerprintId = fingerprint.Id;
        QueryFingerprint = fingerprint;
        PlanHash = planHash.Trim();
        PlanXml = planXml;
        EstimatedCost = estimatedCost;
        IsActive = true;
        CapturedAtUtc = DateTime.UtcNow;
        LastSeenAtUtc = DateTime.UtcNow;

        // These will be populated by plan analysis
        HasMemoryGrant = false;
        UsesIndexSeek = false;
        HasTableScan = false;
        HasMissingIndexHint = false;
        HasImplicitConversion = false;
        IsParallel = false;
    }

    /// <summary>
    /// Updates the plan characteristics from XML analysis.
    /// Called by the plan analyzer service after parsing the XML.
    /// </summary>
    public void UpdatePlanCharacteristics(
        double? estimatedRows,
        int? degreeOfParallelism,
        bool hasMemoryGrant,
        long? memoryGrantKb,
        bool usesIndexSeek,
        bool hasTableScan,
        bool hasMissingIndexHint,
        bool hasImplicitConversion,
        bool isParallel)
    {
        EstimatedRows = estimatedRows;
        DegreeOfParallelism = degreeOfParallelism;
        HasMemoryGrant = hasMemoryGrant;
        MemoryGrantKb = memoryGrantKb;
        UsesIndexSeek = usesIndexSeek;
        HasTableScan = hasTableScan;
        HasMissingIndexHint = hasMissingIndexHint;
        HasImplicitConversion = hasImplicitConversion;
        IsParallel = isParallel;
    }

    /// <summary>
    /// Records a new performance metric sample for this plan.
    /// </summary>
    public PlanMetricSample AddMetricSample(
        long executionCount,
        double avgCpuTimeMs,
        double avgDurationMs,
        double avgLogicalReads,
        double avgPhysicalReads,
        double avgRowsReturned,
        double? avgMemoryGrantKb = null)
    {
        var sample = new PlanMetricSample(
            this,
            executionCount,
            avgCpuTimeMs,
            avgDurationMs,
            avgLogicalReads,
            avgPhysicalReads,
            avgRowsReturned,
            avgMemoryGrantKb);

        _metricSamples.Add(sample);
        LastSeenAtUtc = DateTime.UtcNow;

        return sample;
    }

    /// <summary>
    /// Gets the most recent metric sample.
    /// </summary>
    public PlanMetricSample? GetLatestMetrics()
    {
        return _metricSamples
            .OrderByDescending(m => m.SampledAtUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the average metrics over a time window.
    /// Useful for smoothing out outliers.
    /// </summary>
    public (double AvgCpu, double AvgDuration, double AvgReads)? GetAverageMetrics(TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        var recentSamples = _metricSamples
            .Where(m => m.SampledAtUtc >= cutoff)
            .ToList();

        if (!recentSamples.Any())
            return null;

        return (
            AvgCpu: recentSamples.Average(m => m.AvgCpuTimeMs),
            AvgDuration: recentSamples.Average(m => m.AvgDurationMs),
            AvgReads: recentSamples.Average(m => m.AvgLogicalReads)
        );
    }

    /// <summary>
    /// Marks this plan as still being used.
    /// </summary>
    public void MarkSeen()
    {
        LastSeenAtUtc = DateTime.UtcNow;
        IsActive = true;
    }

    /// <summary>
    /// Marks this plan as no longer in the cache.
    /// We keep the snapshot for historical analysis.
    /// </summary>
    public void MarkEvicted()
    {
        IsActive = false;
    }

    /// <summary>
    /// Checks if this plan has any concerning characteristics.
    /// </summary>
    public bool HasWarnings()
    {
        return HasTableScan || HasMissingIndexHint || HasImplicitConversion;
    }
}
