namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents a normalized query pattern (fingerprint).
/// Multiple actual queries with different parameter values share the same fingerprint.
/// This is the level at which we track execution plans and detect regressions.
/// </summary>
/// <remarks>
/// Example: These two queries share ONE fingerprint:
///   SELECT * FROM Orders WHERE CustomerId = 123
///   SELECT * FROM Orders WHERE CustomerId = 456
/// 
/// SQL Server generates a query_hash that identifies this pattern.
/// </remarks>
public class QueryFingerprint
{
    /// <summary>
    /// Unique identifier for this fingerprint in our system.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the database this query runs in.
    /// </summary>
    public Guid MonitoredDatabaseId { get; private set; }

    /// <summary>
    /// Navigation property to the parent database.
    /// </summary>
    public MonitoredDatabase MonitoredDatabase { get; private set; } = null!;

    /// <summary>
    /// SQL Server's query_hash (or query_id from Query Store).
    /// This is how SQL Server identifies the query pattern.
    /// Stored as hex string for readability (e.g., "0x7A3B2C1D...").
    /// </summary>
    public string QueryHash { get; private set; }

    /// <summary>
    /// The normalized/parameterized query text.
    /// Literals are replaced with placeholders like @p1, @p2.
    /// Useful for display and understanding what the query does.
    /// </summary>
    public string NormalizedQueryText { get; private set; }

    /// <summary>
    /// Truncated version for display (first 200 chars).
    /// Queries can be very long; this is for lists/dashboards.
    /// </summary>
    public string QueryTextPreview { get; private set; }

    /// <summary>
    /// The stored procedure or function name, if this query comes from one.
    /// NULL for ad-hoc queries.
    /// </summary>
    public string? ObjectName { get; private set; }

    /// <summary>
    /// When we first observed this query pattern.
    /// </summary>
    public DateTime FirstSeenAtUtc { get; private set; }

    /// <summary>
    /// When we last saw this query executed.
    /// Queries that haven't run in a while may be stale.
    /// </summary>
    public DateTime LastSeenAtUtc { get; private set; }

    /// <summary>
    /// Total number of executions we've observed.
    /// High-frequency queries are more important to optimize.
    /// </summary>
    public long TotalExecutionCount { get; private set; }

    /// <summary>
    /// Whether this fingerprint is actively being monitored.
    /// Can be disabled if it's known to be unimportant.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Whether this query has been flagged for attention.
    /// Set when a regression or hotspot is detected.
    /// </summary>
    public bool IsFlagged { get; private set; }

    /// <summary>
    /// Optional notes from the team about this query.
    /// E.g., "Known to be slow during month-end processing"
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// The execution plan snapshots we've captured for this query.
    /// A query may have multiple different plans over time.
    /// </summary>
    private readonly List<ExecutionPlanSnapshot> _planSnapshots = new();
    public IReadOnlyCollection<ExecutionPlanSnapshot> PlanSnapshots => _planSnapshots.AsReadOnly();

    /// <summary>
    /// The baseline (expected good performance) for this query.
    /// Used to detect regressions when current metrics deviate.
    /// </summary>
    public PlanBaseline? Baseline { get; private set; }

    // Private constructor for EF Core
    private QueryFingerprint() { }

    /// <summary>
    /// Creates a new query fingerprint. Called by MonitoredDatabase.RegisterQueryFingerprint().
    /// </summary>
    internal QueryFingerprint(
        MonitoredDatabase database,
        string queryHash,
        string normalizedQueryText,
        string? objectName)
    {
        if (database == null)
            throw new ArgumentNullException(nameof(database));
        if (string.IsNullOrWhiteSpace(queryHash))
            throw new ArgumentException("Query hash is required.", nameof(queryHash));
        if (string.IsNullOrWhiteSpace(normalizedQueryText))
            throw new ArgumentException("Query text is required.", nameof(normalizedQueryText));

        Id = Guid.NewGuid();
        MonitoredDatabaseId = database.Id;
        MonitoredDatabase = database;
        QueryHash = queryHash.Trim();
        NormalizedQueryText = normalizedQueryText;
        QueryTextPreview = normalizedQueryText.Length > 200 
            ? normalizedQueryText.Substring(0, 200) + "..." 
            : normalizedQueryText;
        ObjectName = objectName?.Trim();
        FirstSeenAtUtc = DateTime.UtcNow;
        LastSeenAtUtc = DateTime.UtcNow;
        TotalExecutionCount = 0;
        IsActive = true;
        IsFlagged = false;
    }

    /// <summary>
    /// Records that this query was executed.
    /// Called during each collection cycle.
    /// </summary>
    public void RecordExecution(long executionCount)
    {
        LastSeenAtUtc = DateTime.UtcNow;
        TotalExecutionCount += executionCount;
    }

    /// <summary>
    /// Adds a new execution plan snapshot for this query.
    /// </summary>
    public ExecutionPlanSnapshot AddPlanSnapshot(
        string planHash,
        string planXml,
        double estimatedCost)
    {
        // Check if we already have this exact plan
        var existingPlan = _planSnapshots.FirstOrDefault(
            p => p.PlanHash.Equals(planHash, StringComparison.OrdinalIgnoreCase));

        if (existingPlan != null)
        {
            // Plan already captured - just mark it as seen again
            existingPlan.MarkSeen();
            return existingPlan;
        }

        var snapshot = new ExecutionPlanSnapshot(this, planHash, planXml, estimatedCost);
        _planSnapshots.Add(snapshot);

        return snapshot;
    }

    /// <summary>
    /// Gets the currently active execution plan (most recently used).
    /// </summary>
    public ExecutionPlanSnapshot? GetCurrentPlan()
    {
        return _planSnapshots
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.LastSeenAtUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Establishes or updates the performance baseline for this query.
    /// </summary>
    public void SetBaseline(
        ExecutionPlanSnapshot planSnapshot,
        double avgCpuTimeMs,
        double avgDurationMs,
        double avgLogicalReads,
        double avgRowsReturned)
    {
        Baseline = new PlanBaseline(
            this,
            planSnapshot,
            avgCpuTimeMs,
            avgDurationMs,
            avgLogicalReads,
            avgRowsReturned);
    }

    /// <summary>
    /// Flags this query for attention (regression/hotspot detected).
    /// </summary>
    public void Flag(string? reason = null)
    {
        IsFlagged = true;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Notes = string.IsNullOrWhiteSpace(Notes)
                ? reason
                : $"{Notes}\n[{DateTime.UtcNow:u}] {reason}";
        }
    }

    /// <summary>
    /// Clears the flag (issue resolved).
    /// </summary>
    public void ClearFlag()
    {
        IsFlagged = false;
    }

    /// <summary>
    /// Adds a note about this query.
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
    /// Deactivates monitoring for this fingerprint.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Reactivates monitoring for this fingerprint.
    /// </summary>
    public void Activate()
    {
        IsActive = true;
    }
}
