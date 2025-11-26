namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents a query that is consuming disproportionate resources.
/// Unlike regressions (comparison to baseline), hotspots are about
/// absolute resource consumption - the "top N" heavy hitters.
/// </summary>
/// <remarks>
/// Hotspots answer: "What's hurting my server right now?"
/// 
/// Examples:
/// - Top 10 queries by total CPU in the last hour
/// - Queries with memory grants > 1GB
/// - Queries causing the most physical I/O
/// 
/// A query can be a hotspot without being a regression (it was always expensive)
/// and can be a regression without being a hotspot (got slower but still fast).
/// </remarks>
public class Hotspot
{
    /// <summary>
    /// Unique identifier for this hotspot detection.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the query fingerprint that's the hotspot.
    /// </summary>
    public Guid QueryFingerprintId { get; private set; }

    /// <summary>
    /// Navigation property to the query.
    /// </summary>
    public QueryFingerprint QueryFingerprint { get; private set; } = null!;

    /// <summary>
    /// Reference to the monitored database.
    /// </summary>
    public Guid MonitoredDatabaseId { get; private set; }

    /// <summary>
    /// Navigation property to the database.
    /// </summary>
    public MonitoredDatabase MonitoredDatabase { get; private set; } = null!;

    /// <summary>
    /// Reference to the current execution plan (optional).
    /// </summary>
    public Guid? CurrentPlanSnapshotId { get; private set; }

    /// <summary>
    /// Navigation property to the current plan.
    /// </summary>
    public ExecutionPlanSnapshot? CurrentPlanSnapshot { get; private set; }

    /// <summary>
    /// When this hotspot was detected.
    /// </summary>
    public DateTime DetectedAtUtc { get; private set; }

    /// <summary>
    /// When the hotspot analysis window started.
    /// E.g., for "top 10 in last hour", this is 1 hour ago.
    /// </summary>
    public DateTime AnalysisWindowStartUtc { get; private set; }

    /// <summary>
    /// When the analysis window ended.
    /// </summary>
    public DateTime AnalysisWindowEndUtc { get; private set; }

    /// <summary>
    /// What resource metric makes this a hotspot.
    /// </summary>
    public HotspotMetricType MetricType { get; private set; }

    /// <summary>
    /// The rank of this hotspot (1 = worst offender).
    /// </summary>
    public int Rank { get; private set; }

    /// <summary>
    /// The total value of the metric in the analysis window.
    /// E.g., total CPU time in ms.
    /// </summary>
    public double TotalMetricValue { get; private set; }

    /// <summary>
    /// The average value per execution.
    /// </summary>
    public double AvgMetricValue { get; private set; }

    /// <summary>
    /// Number of executions in the analysis window.
    /// </summary>
    public long ExecutionCount { get; private set; }

    /// <summary>
    /// Percentage of total server resource this query consumed.
    /// E.g., "This query used 35% of total server CPU."
    /// </summary>
    public double PercentageOfTotal { get; private set; }

    /// <summary>
    /// Current status of this hotspot.
    /// </summary>
    public HotspotStatus Status { get; private set; }

    /// <summary>
    /// Whether an alert was sent.
    /// </summary>
    public bool AlertSent { get; private set; }

    /// <summary>
    /// When the alert was sent.
    /// </summary>
    public DateTime? AlertSentAtUtc { get; private set; }

    /// <summary>
    /// Notes about this hotspot.
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// When this hotspot was resolved/cleared.
    /// </summary>
    public DateTime? ResolvedAtUtc { get; private set; }

    // Private constructor for EF Core
    private Hotspot() { }

    /// <summary>
    /// Creates a new hotspot.
    /// </summary>
    public Hotspot(
        QueryFingerprint fingerprint,
        MonitoredDatabase database,
        ExecutionPlanSnapshot? currentPlan,
        HotspotMetricType metricType,
        int rank,
        double totalMetricValue,
        double avgMetricValue,
        long executionCount,
        double percentageOfTotal,
        DateTime windowStart,
        DateTime windowEnd)
    {
        if (fingerprint == null)
            throw new ArgumentNullException(nameof(fingerprint));
        if (database == null)
            throw new ArgumentNullException(nameof(database));
        if (rank < 1)
            throw new ArgumentException("Rank must be at least 1.", nameof(rank));

        Id = Guid.NewGuid();
        QueryFingerprintId = fingerprint.Id;
        QueryFingerprint = fingerprint;
        MonitoredDatabaseId = database.Id;
        MonitoredDatabase = database;
        CurrentPlanSnapshotId = currentPlan?.Id;
        CurrentPlanSnapshot = currentPlan;

        DetectedAtUtc = DateTime.UtcNow;
        AnalysisWindowStartUtc = windowStart;
        AnalysisWindowEndUtc = windowEnd;

        MetricType = metricType;
        Rank = rank;
        TotalMetricValue = totalMetricValue;
        AvgMetricValue = avgMetricValue;
        ExecutionCount = executionCount;
        PercentageOfTotal = percentageOfTotal;

        Status = HotspotStatus.Active;
        AlertSent = false;

        // Flag the fingerprint
        fingerprint.Flag($"Hotspot detected: Rank #{rank} by {metricType}");
    }

    /// <summary>
    /// Gets the duration of the analysis window.
    /// </summary>
    public TimeSpan GetAnalysisWindowDuration()
    {
        return AnalysisWindowEndUtc - AnalysisWindowStartUtc;
    }

    /// <summary>
    /// Marks that an alert was sent.
    /// </summary>
    public void MarkAlertSent()
    {
        AlertSent = true;
        AlertSentAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the rank (e.g., if another hotspot was resolved).
    /// </summary>
    public void UpdateRank(int newRank)
    {
        if (newRank < 1)
            throw new ArgumentException("Rank must be at least 1.", nameof(newRank));

        Rank = newRank;
    }

    /// <summary>
    /// Marks this hotspot as acknowledged.
    /// </summary>
    public void Acknowledge(string? notes = null)
    {
        Status = HotspotStatus.Acknowledged;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            AddNote(notes);
        }
    }

    /// <summary>
    /// Marks this hotspot as being investigated.
    /// </summary>
    public void StartInvestigation(string? notes = null)
    {
        Status = HotspotStatus.Investigating;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            AddNote(notes);
        }
    }

    /// <summary>
    /// Marks this hotspot as expected/accepted.
    /// Some queries are just inherently expensive.
    /// </summary>
    public void MarkAsExpected(string reason)
    {
        Status = HotspotStatus.Expected;
        AddNote($"Marked as expected: {reason}");
    }

    /// <summary>
    /// Resolves this hotspot (no longer a top consumer).
    /// </summary>
    public void Resolve(string? notes = null)
    {
        Status = HotspotStatus.Resolved;
        ResolvedAtUtc = DateTime.UtcNow;
        
        if (!string.IsNullOrWhiteSpace(notes))
        {
            AddNote(notes);
        }

        QueryFingerprint?.ClearFlag();
    }

    /// <summary>
    /// Adds a note about this hotspot.
    /// </summary>
    public void AddNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return;

        Notes = string.IsNullOrWhiteSpace(Notes)
            ? $"[{DateTime.UtcNow:u}] {note}"
            : $"{Notes}\n[{DateTime.UtcNow:u}] {note}";
    }

    /// <summary>
    /// Gets a formatted summary for display.
    /// </summary>
    public string GetSummary()
    {
        var metricDisplay = MetricType switch
        {
            HotspotMetricType.TotalCpuTime => $"{TotalMetricValue / 1000:N1}s CPU",
            HotspotMetricType.TotalDuration => $"{TotalMetricValue / 1000:N1}s duration",
            HotspotMetricType.TotalLogicalReads => $"{TotalMetricValue / 1_000_000:N2}M reads",
            HotspotMetricType.TotalPhysicalReads => $"{TotalMetricValue / 1_000_000:N2}M physical reads",
            HotspotMetricType.TotalMemoryGrant => $"{TotalMetricValue / 1024:N1}MB memory",
            HotspotMetricType.ExecutionCount => $"{ExecutionCount:N0} executions",
            _ => $"{TotalMetricValue:N0}"
        };

        return $"#{Rank}: {metricDisplay} ({PercentageOfTotal:P1} of total) over {GetAnalysisWindowDuration().TotalMinutes:N0} min";
    }
}

/// <summary>
/// The metric type that defines this hotspot.
/// </summary>
public enum HotspotMetricType
{
    /// <summary>
    /// Total CPU time consumed.
    /// </summary>
    TotalCpuTime,

    /// <summary>
    /// Total elapsed/duration time.
    /// </summary>
    TotalDuration,

    /// <summary>
    /// Total logical reads (memory I/O).
    /// </summary>
    TotalLogicalReads,

    /// <summary>
    /// Total physical reads (disk I/O).
    /// </summary>
    TotalPhysicalReads,

    /// <summary>
    /// Total memory grant size.
    /// </summary>
    TotalMemoryGrant,

    /// <summary>
    /// Number of executions (frequency).
    /// </summary>
    ExecutionCount,

    /// <summary>
    /// Average CPU per execution.
    /// </summary>
    AvgCpuTime,

    /// <summary>
    /// Average duration per execution.
    /// </summary>
    AvgDuration
}

/// <summary>
/// Status of a hotspot.
/// </summary>
public enum HotspotStatus
{
    /// <summary>
    /// Currently active hotspot.
    /// </summary>
    Active,

    /// <summary>
    /// Someone has acknowledged the hotspot.
    /// </summary>
    Acknowledged,

    /// <summary>
    /// Being investigated.
    /// </summary>
    Investigating,

    /// <summary>
    /// Known to be expensive but acceptable.
    /// </summary>
    Expected,

    /// <summary>
    /// No longer a top consumer.
    /// </summary>
    Resolved
}
