using DbExecPlanMonitor.Domain.Entities;
using Hotspot = DbExecPlanMonitor.Domain.Services.Hotspot;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Defines a channel for sending alerts about performance issues.
/// </summary>
/// <remarks>
/// <para>
/// This interface follows the Strategy pattern, allowing different alert
/// delivery mechanisms (email, Teams, Slack, logging) to be used interchangeably.
/// </para>
/// <para>
/// Multiple channels can be active simultaneously - for example, sending
/// critical alerts via email AND posting to a Slack channel.
/// </para>
/// <para>
/// <strong>Design Decisions:</strong>
/// <list type="bullet">
/// <item>Async-only: All alert operations may involve network I/O</item>
/// <item>Batch-oriented: Send multiple alerts efficiently in one call</item>
/// <item>No return value: Fire-and-forget semantics (failures logged internally)</item>
/// </list>
/// </para>
/// </remarks>
public interface IAlertChannel
{
    /// <summary>
    /// Gets the unique name of this alert channel (e.g., "Email", "Teams", "Slack").
    /// Used for configuration and logging.
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Gets whether this channel is currently enabled and configured.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Sends alerts for detected performance regressions.
    /// </summary>
    /// <param name="events">The regression events to alert on.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    /// <remarks>
    /// Implementations should:
    /// <list type="bullet">
    /// <item>Format the alert appropriately for the channel</item>
    /// <item>Include actionable information (query text, metrics, suggestions)</item>
    /// <item>Handle failures gracefully (log and continue)</item>
    /// <item>Respect rate limits if applicable</item>
    /// </list>
    /// </remarks>
    Task SendRegressionAlertsAsync(
        IEnumerable<RegressionEvent> events,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a summary of current performance hotspots.
    /// </summary>
    /// <param name="hotspots">The hotspots to include in the summary.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    /// <remarks>
    /// This is typically used for periodic summaries (e.g., daily digest)
    /// rather than real-time alerts.
    /// </remarks>
    Task SendHotspotSummaryAsync(
        IEnumerable<Hotspot> hotspots,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a daily summary of all detected issues.
    /// </summary>
    /// <param name="summary">The daily summary to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    Task SendDailySummaryAsync(
        DailySummary summary,
        CancellationToken ct = default);

    /// <summary>
    /// Tests the channel configuration by sending a test message.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the test message was sent successfully.</returns>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Represents a daily summary of performance monitoring results.
/// </summary>
public sealed class DailySummary
{
    /// <summary>
    /// The date this summary covers.
    /// </summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// The time period covered by this summary.
    /// </summary>
    public required DateTime PeriodStartUtc { get; init; }
    public required DateTime PeriodEndUtc { get; init; }

    /// <summary>
    /// Total number of databases analyzed.
    /// </summary>
    public int DatabasesAnalyzed { get; init; }

    /// <summary>
    /// Total number of query fingerprints analyzed.
    /// </summary>
    public int QueriesAnalyzed { get; init; }

    /// <summary>
    /// New regressions detected in this period.
    /// </summary>
    public required IReadOnlyList<RegressionEvent> NewRegressions { get; init; }

    /// <summary>
    /// Regressions that were resolved in this period.
    /// </summary>
    public required IReadOnlyList<RegressionEvent> ResolvedRegressions { get; init; }

    /// <summary>
    /// Ongoing regressions that remain unresolved.
    /// </summary>
    public required IReadOnlyList<RegressionEvent> OngoingRegressions { get; init; }

    /// <summary>
    /// Top hotspots by resource consumption.
    /// </summary>
    public required IReadOnlyList<Hotspot> TopHotspots { get; init; }

    /// <summary>
    /// Quick counts for dashboard display.
    /// </summary>
    public int NewRegressionCount => NewRegressions.Count;
    public int ResolvedRegressionCount => ResolvedRegressions.Count;
    public int OngoingRegressionCount => OngoingRegressions.Count;
    public int HotspotCount => TopHotspots.Count;

    /// <summary>
    /// Overall health status based on the summary.
    /// </summary>
    public HealthStatus OverallHealth =>
        NewRegressions.Any(r => r.Severity == Domain.Enums.RegressionSeverity.Critical)
            ? HealthStatus.Critical
            : NewRegressions.Any(r => r.Severity == Domain.Enums.RegressionSeverity.High)
                ? HealthStatus.Warning
                : NewRegressions.Any()
                    ? HealthStatus.Degraded
                    : HealthStatus.Healthy;
}

/// <summary>
/// Overall health status for a monitoring period.
/// </summary>
public enum HealthStatus
{
    /// <summary>No issues detected.</summary>
    Healthy,
    
    /// <summary>Minor issues detected.</summary>
    Degraded,
    
    /// <summary>Significant issues requiring attention.</summary>
    Warning,
    
    /// <summary>Critical issues requiring immediate action.</summary>
    Critical
}
