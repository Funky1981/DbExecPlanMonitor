using DbExecPlanMonitor.Domain.Enums;
using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Repository interface for persisting and retrieving regression events.
/// A regression event is a detected performance degradation for a query.
/// </summary>
public interface IRegressionEventRepository
{
    /// <summary>
    /// Saves a new regression event.
    /// </summary>
    Task SaveEventAsync(RegressionEventRecord regressionEvent, CancellationToken ct = default);

    /// <summary>
    /// Saves multiple regression events in a single transaction.
    /// </summary>
    Task SaveEventsAsync(
        IEnumerable<RegressionEventRecord> events,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a regression event by its ID.
    /// </summary>
    Task<RegressionEventRecord?> GetByIdAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Gets recent regression events within a time window.
    /// </summary>
    Task<IReadOnlyList<RegressionEventRecord>> GetRecentEventsAsync(
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Gets regression events for a specific database instance.
    /// </summary>
    Task<IReadOnlyList<RegressionEventRecord>> GetEventsForInstanceAsync(
        string instanceName,
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Gets regression events for a specific query fingerprint.
    /// Useful for seeing the regression history of a problematic query.
    /// </summary>
    Task<IReadOnlyList<RegressionEventRecord>> GetEventsForFingerprintAsync(
        Guid fingerprintId,
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Gets unacknowledged regression events that need human attention.
    /// </summary>
    Task<IReadOnlyList<RegressionEventRecord>> GetUnacknowledgedEventsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Gets regression events filtered by severity.
    /// </summary>
    Task<IReadOnlyList<RegressionEventRecord>> GetEventsBySeverityAsync(
        RegressionSeverity minSeverity,
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Acknowledges a regression event (marks it as reviewed by a human).
    /// </summary>
    Task AcknowledgeEventAsync(
        Guid eventId,
        string acknowledgedBy,
        string? notes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a regression event (the issue has been fixed).
    /// </summary>
    Task ResolveEventAsync(
        Guid eventId,
        string resolvedBy,
        string? resolutionNotes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets summary statistics for regression events over a time period.
    /// </summary>
    Task<RegressionSummary> GetSummaryAsync(
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes regression events older than the specified date.
    /// </summary>
    Task<int> PurgeEventsOlderThanAsync(DateTime olderThan, CancellationToken ct = default);

    /// <summary>
    /// Saves a RegressionEvent entity (domain entity version).
    /// </summary>
    Task SaveAsync(Domain.Entities.RegressionEvent regressionEvent, CancellationToken ct = default);

    /// <summary>
    /// Updates a RegressionEvent entity (domain entity version).
    /// </summary>
    Task UpdateAsync(Domain.Entities.RegressionEvent regressionEvent, CancellationToken ct = default);

    /// <summary>
    /// Gets the active regression for a fingerprint (if any).
    /// Active = not resolved or dismissed.
    /// </summary>
    Task<Domain.Entities.RegressionEvent?> GetActiveByFingerprintIdAsync(
        Guid fingerprintId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active (unresolved) regressions.
    /// </summary>
    Task<IReadOnlyList<Domain.Entities.RegressionEvent>> GetActiveAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Represents a detected performance regression.
/// </summary>
public sealed class RegressionEventRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FingerprintId { get; init; }
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required DateTime DetectedAtUtc { get; init; }
    
    /// <summary>
    /// The type of regression detected.
    /// </summary>
    public required RegressionType Type { get; init; }
    
    /// <summary>
    /// Which metric regressed (Duration, CpuTime, LogicalReads).
    /// </summary>
    public required string MetricName { get; init; }
    
    /// <summary>
    /// The baseline value we compared against.
    /// </summary>
    public required long BaselineValue { get; init; }
    
    /// <summary>
    /// The current value that triggered the regression.
    /// </summary>
    public required long CurrentValue { get; init; }
    
    /// <summary>
    /// The percentage change from baseline.
    /// Positive = regression, Negative = improvement (shouldn't happen for regressions).
    /// </summary>
    public required double ChangePercent { get; init; }
    
    /// <summary>
    /// The configured threshold percentage that was exceeded.
    /// </summary>
    public required double ThresholdPercent { get; init; }
    
    /// <summary>
    /// Calculated severity based on the magnitude of the regression.
    /// </summary>
    public required RegressionSeverity Severity { get; init; }
    
    /// <summary>
    /// A sample of the SQL text for context.
    /// </summary>
    public string? QueryTextSample { get; init; }
    
    /// <summary>
    /// The baseline plan hash (if plan change was detected).
    /// </summary>
    public byte[]? BaselinePlanHash { get; init; }
    
    /// <summary>
    /// The current plan hash (if plan change was detected).
    /// </summary>
    public byte[]? CurrentPlanHash { get; init; }
    
    /// <summary>
    /// Whether the plan changed as part of this regression.
    /// </summary>
    public bool IsPlanChange { get; init; }
    
    // Workflow status fields
    public RegressionStatus Status { get; set; } = RegressionStatus.New;
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Creates a human-readable summary of the regression.
    /// </summary>
    public string Summary => 
        $"{Severity} {Type}: {MetricName} increased {ChangePercent:F1}% " +
        $"(baseline: {BaselineValue}, current: {CurrentValue})";
}

/// <summary>
/// The type of regression detected.
/// </summary>
public enum RegressionType
{
    /// <summary>
    /// The metric (duration, CPU, etc.) has worsened beyond threshold.
    /// </summary>
    MetricRegression,
    
    /// <summary>
    /// The execution plan has changed from the baseline plan.
    /// </summary>
    PlanChange,
    
    /// <summary>
    /// Both metric regression and plan change occurred.
    /// </summary>
    PlanChangeWithRegression
}

// Note: RegressionSeverity and RegressionStatus are defined in DbExecPlanMonitor.Domain.Enums

/// <summary>
/// Summary statistics for regression events over a time period.
/// </summary>
public sealed class RegressionSummary
{
    public required TimeWindow Window { get; init; }
    public required int TotalEvents { get; init; }
    public required int NewEvents { get; init; }
    public required int AcknowledgedEvents { get; init; }
    public required int ResolvedEvents { get; init; }
    public required int CriticalEvents { get; init; }
    public required int HighEvents { get; init; }
    public required int MediumEvents { get; init; }
    public required int LowEvents { get; init; }
    public required int UniqueQueriesAffected { get; init; }
    public required int UniqueDatabasesAffected { get; init; }
}
