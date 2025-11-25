namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents a detected performance regression event.
/// Created when a query's metrics significantly deviate from its baseline.
/// </summary>
/// <remarks>
/// A regression event captures:
/// - What happened (which query, which metrics regressed)
/// - When it happened
/// - How bad it is (severity)
/// - Current status (investigating, resolved, etc.)
/// - What we're doing about it (linked remediation suggestions)
/// 
/// This is the primary entity for the alerting and workflow system.
/// </remarks>
public class RegressionEvent
{
    /// <summary>
    /// Unique identifier for this regression event.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the query fingerprint that regressed.
    /// </summary>
    public Guid QueryFingerprintId { get; private set; }

    /// <summary>
    /// Navigation property to the affected query.
    /// </summary>
    public QueryFingerprint QueryFingerprint { get; private set; } = null!;

    /// <summary>
    /// Reference to the execution plan in use when regression was detected.
    /// Often different from the baseline plan (plan change caused regression).
    /// </summary>
    public Guid CurrentPlanSnapshotId { get; private set; }

    /// <summary>
    /// Navigation property to the current plan.
    /// </summary>
    public ExecutionPlanSnapshot CurrentPlanSnapshot { get; private set; } = null!;

    /// <summary>
    /// Reference to the baseline plan (the "good" plan).
    /// NULL if this is a new query without an established baseline.
    /// </summary>
    public Guid? BaselinePlanSnapshotId { get; private set; }

    /// <summary>
    /// Navigation property to the baseline plan.
    /// </summary>
    public ExecutionPlanSnapshot? BaselinePlanSnapshot { get; private set; }

    /// <summary>
    /// When the regression was first detected.
    /// </summary>
    public DateTime DetectedAtUtc { get; private set; }

    /// <summary>
    /// When the regression was resolved (if applicable).
    /// </summary>
    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>
    /// How severe this regression is.
    /// </summary>
    public RegressionSeverity Severity { get; private set; }

    /// <summary>
    /// Current status of this regression event.
    /// </summary>
    public RegressionStatus Status { get; private set; }

    /// <summary>
    /// What type of regression this is (CPU, Duration, Reads, Plan Change, etc.)
    /// </summary>
    public RegressionType Type { get; private set; }

    /// <summary>
    /// The baseline value for the primary regressed metric.
    /// </summary>
    public double BaselineValue { get; private set; }

    /// <summary>
    /// The current (regressed) value for the primary metric.
    /// </summary>
    public double CurrentValue { get; private set; }

    /// <summary>
    /// The regression ratio (CurrentValue / BaselineValue).
    /// E.g., 5.0 means "5x slower than baseline."
    /// </summary>
    public double RegressionRatio { get; private set; }

    /// <summary>
    /// Estimated impact: TotalExecutions × (CurrentValue - BaselineValue).
    /// Helps prioritize which regressions to fix first.
    /// </summary>
    public double EstimatedImpact { get; private set; }

    /// <summary>
    /// Whether an alert was sent for this regression.
    /// </summary>
    public bool AlertSent { get; private set; }

    /// <summary>
    /// When the alert was sent.
    /// </summary>
    public DateTime? AlertSentAtUtc { get; private set; }

    /// <summary>
    /// Who acknowledged this regression (if applicable).
    /// </summary>
    public string? AcknowledgedBy { get; private set; }

    /// <summary>
    /// When it was acknowledged.
    /// </summary>
    public DateTime? AcknowledgedAtUtc { get; private set; }

    /// <summary>
    /// Free-form notes about investigation and resolution.
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// How this regression was resolved (if resolved).
    /// </summary>
    public ResolutionMethod? Resolution { get; private set; }

    /// <summary>
    /// Description of the resolution action taken.
    /// </summary>
    public string? ResolutionDetails { get; private set; }

    /// <summary>
    /// Remediation suggestions for this regression.
    /// </summary>
    private readonly List<RemediationSuggestion> _remediationSuggestions = new();
    public IReadOnlyCollection<RemediationSuggestion> RemediationSuggestions => _remediationSuggestions.AsReadOnly();

    // Private constructor for EF Core
    private RegressionEvent() { }

    /// <summary>
    /// Creates a new regression event.
    /// </summary>
    public RegressionEvent(
        QueryFingerprint fingerprint,
        ExecutionPlanSnapshot currentPlan,
        ExecutionPlanSnapshot? baselinePlan,
        RegressionType type,
        double baselineValue,
        double currentValue,
        long executionCount)
    {
        if (fingerprint == null)
            throw new ArgumentNullException(nameof(fingerprint));
        if (currentPlan == null)
            throw new ArgumentNullException(nameof(currentPlan));

        Id = Guid.NewGuid();
        QueryFingerprintId = fingerprint.Id;
        QueryFingerprint = fingerprint;
        CurrentPlanSnapshotId = currentPlan.Id;
        CurrentPlanSnapshot = currentPlan;
        BaselinePlanSnapshotId = baselinePlan?.Id;
        BaselinePlanSnapshot = baselinePlan;

        DetectedAtUtc = DateTime.UtcNow;
        Type = type;
        BaselineValue = baselineValue;
        CurrentValue = currentValue;
        RegressionRatio = baselineValue > 0 ? currentValue / baselineValue : 0;

        // Calculate impact: how much extra time/resources is this costing?
        EstimatedImpact = executionCount * (currentValue - baselineValue);

        // Determine severity based on ratio and impact
        Severity = CalculateSeverity(RegressionRatio, EstimatedImpact);
        Status = RegressionStatus.New;
        AlertSent = false;

        // Flag the fingerprint
        fingerprint.Flag($"Regression detected: {type} {RegressionRatio:F1}x");
    }

    /// <summary>
    /// Calculates severity based on regression magnitude and impact.
    /// </summary>
    private static RegressionSeverity CalculateSeverity(double ratio, double impact)
    {
        // Critical: >10x regression OR massive impact
        if (ratio >= 10.0 || impact >= 1_000_000)
            return RegressionSeverity.Critical;

        // High: 5-10x regression OR significant impact
        if (ratio >= 5.0 || impact >= 100_000)
            return RegressionSeverity.High;

        // Medium: 3-5x regression
        if (ratio >= 3.0 || impact >= 10_000)
            return RegressionSeverity.Medium;

        // Low: 2-3x regression
        return RegressionSeverity.Low;
    }

    /// <summary>
    /// Marks that an alert was sent for this regression.
    /// </summary>
    public void MarkAlertSent()
    {
        AlertSent = true;
        AlertSentAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Acknowledges this regression (someone is looking at it).
    /// </summary>
    public void Acknowledge(string acknowledgedBy)
    {
        if (string.IsNullOrWhiteSpace(acknowledgedBy))
            throw new ArgumentException("Acknowledged by is required.", nameof(acknowledgedBy));

        AcknowledgedBy = acknowledgedBy;
        AcknowledgedAtUtc = DateTime.UtcNow;
        Status = RegressionStatus.Acknowledged;
    }

    /// <summary>
    /// Marks this regression as being investigated.
    /// </summary>
    public void StartInvestigation(string? notes = null)
    {
        Status = RegressionStatus.Investigating;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            AddNote(notes);
        }
    }

    /// <summary>
    /// Marks this regression as resolved.
    /// </summary>
    public void Resolve(ResolutionMethod resolution, string? details = null)
    {
        Status = RegressionStatus.Resolved;
        ResolvedAtUtc = DateTime.UtcNow;
        Resolution = resolution;
        ResolutionDetails = details;

        // Clear the flag on the fingerprint
        QueryFingerprint?.ClearFlag();
    }

    /// <summary>
    /// Marks this regression as a false positive.
    /// </summary>
    public void MarkAsFalsePositive(string? reason = null)
    {
        Status = RegressionStatus.FalsePositive;
        ResolvedAtUtc = DateTime.UtcNow;
        Resolution = ResolutionMethod.FalsePositive;
        ResolutionDetails = reason;

        // Clear the flag on the fingerprint
        QueryFingerprint?.ClearFlag();
    }

    /// <summary>
    /// Reopens a previously resolved regression.
    /// </summary>
    public void Reopen(string? reason = null)
    {
        Status = RegressionStatus.Reopened;
        ResolvedAtUtc = null;
        Resolution = null;
        ResolutionDetails = null;

        if (!string.IsNullOrWhiteSpace(reason))
        {
            AddNote($"Reopened: {reason}");
        }

        // Re-flag the fingerprint
        QueryFingerprint?.Flag("Regression reopened");
    }

    /// <summary>
    /// Adds a remediation suggestion.
    /// </summary>
    public RemediationSuggestion AddSuggestion(
        RemediationType type,
        string title,
        string description,
        string? actionScript = null)
    {
        var suggestion = new RemediationSuggestion(this, type, title, description, actionScript);
        _remediationSuggestions.Add(suggestion);
        return suggestion;
    }

    /// <summary>
    /// Adds a note about this regression.
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
    /// Updates the severity if conditions change.
    /// </summary>
    public void UpdateSeverity(RegressionSeverity newSeverity)
    {
        if (Severity != newSeverity)
        {
            var oldSeverity = Severity;
            Severity = newSeverity;
            AddNote($"Severity changed from {oldSeverity} to {newSeverity}");
        }
    }

    /// <summary>
    /// Gets the duration this regression has been open.
    /// </summary>
    public TimeSpan GetOpenDuration()
    {
        var endTime = ResolvedAtUtc ?? DateTime.UtcNow;
        return endTime - DetectedAtUtc;
    }
}

/// <summary>
/// The type of regression detected.
/// </summary>
public enum RegressionType
{
    /// <summary>
    /// CPU time increased significantly.
    /// </summary>
    CpuTime,

    /// <summary>
    /// Duration/elapsed time increased significantly.
    /// </summary>
    Duration,

    /// <summary>
    /// Logical reads increased significantly.
    /// </summary>
    LogicalReads,

    /// <summary>
    /// Physical reads increased significantly.
    /// </summary>
    PhysicalReads,

    /// <summary>
    /// Execution plan changed (may or may not cause metric regression).
    /// </summary>
    PlanChange,

    /// <summary>
    /// Multiple metrics regressed together.
    /// </summary>
    MultipleMetrics,

    /// <summary>
    /// Memory grant increased significantly.
    /// </summary>
    MemoryGrant
}

/// <summary>
/// How severe the regression is.
/// </summary>
public enum RegressionSeverity
{
    /// <summary>
    /// Minor regression (2-3x). Monitor but not urgent.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Moderate regression (3-5x). Should be investigated.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Significant regression (5-10x). Needs prompt attention.
    /// </summary>
    High = 3,

    /// <summary>
    /// Severe regression (>10x). Immediate action required.
    /// </summary>
    Critical = 4
}

/// <summary>
/// Current status of the regression event.
/// </summary>
public enum RegressionStatus
{
    /// <summary>
    /// Just detected, not yet acknowledged.
    /// </summary>
    New,

    /// <summary>
    /// Someone has acknowledged and will investigate.
    /// </summary>
    Acknowledged,

    /// <summary>
    /// Currently being investigated.
    /// </summary>
    Investigating,

    /// <summary>
    /// Issue has been resolved.
    /// </summary>
    Resolved,

    /// <summary>
    /// Determined to be a false positive.
    /// </summary>
    FalsePositive,

    /// <summary>
    /// Was resolved but has reoccurred.
    /// </summary>
    Reopened
}

/// <summary>
/// How the regression was resolved.
/// </summary>
public enum ResolutionMethod
{
    /// <summary>
    /// Forced a specific plan using Query Store.
    /// </summary>
    PlanForced,

    /// <summary>
    /// Added query hint to force behavior.
    /// </summary>
    QueryHintAdded,

    /// <summary>
    /// Created or modified an index.
    /// </summary>
    IndexChange,

    /// <summary>
    /// Updated statistics on the table.
    /// </summary>
    StatisticsUpdated,

    /// <summary>
    /// Modified the query itself.
    /// </summary>
    QueryRewritten,

    /// <summary>
    /// Updated the baseline to accept new performance.
    /// </summary>
    BaselineUpdated,

    /// <summary>
    /// Regression resolved itself (e.g., temp data skew).
    /// </summary>
    SelfResolved,

    /// <summary>
    /// Determined to not be a real regression.
    /// </summary>
    FalsePositive,

    /// <summary>
    /// Some other resolution.
    /// </summary>
    Other
}
