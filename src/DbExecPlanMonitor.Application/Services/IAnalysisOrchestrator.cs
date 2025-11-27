using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Services;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Orchestrates the analysis workflow: detecting regressions and hotspots
/// across databases and instances.
/// </summary>
public interface IAnalysisOrchestrator
{
    /// <summary>
    /// Runs regression detection for all databases in all configured instances.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Summary of the analysis run</returns>
    Task<AnalysisRunSummary> AnalyzeAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs regression detection for a specific database.
    /// </summary>
    Task<DatabaseAnalysisResult> AnalyzeDatabaseAsync(
        string instanceName,
        string databaseName,
        CancellationToken ct = default);

    /// <summary>
    /// Runs hotspot detection for a specific database.
    /// </summary>
    Task<HotspotAnalysisResult> DetectHotspotsAsync(
        string instanceName,
        string databaseName,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if any regressions have auto-resolved (performance returned to normal).
    /// </summary>
    Task<int> CheckForAutoResolutionsAsync(CancellationToken ct = default);
}

/// <summary>
/// Summary of a complete analysis run.
/// </summary>
public sealed class AnalysisRunSummary
{
    public required DateTime StartedAtUtc { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public required TimeSpan Duration { get; init; }

    public required IReadOnlyList<DatabaseAnalysisResult> DatabaseResults { get; init; }

    public int TotalDatabasesAnalyzed => DatabaseResults.Count;
    public int TotalRegressionsDetected => DatabaseResults.Sum(r => r.RegressionsDetected);
    public int TotalHotspotsDetected => DatabaseResults.Sum(r => r.HotspotsDetected);
    public int TotalErrors => DatabaseResults.Count(r => r.Error != null);

    public bool IsFullySuccessful => TotalErrors == 0;
}

/// <summary>
/// Result of analyzing a single database.
/// </summary>
public sealed class DatabaseAnalysisResult
{
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required DateTime AnalyzedAtUtc { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of fingerprints analyzed.
    /// </summary>
    public int FingerprintsAnalyzed { get; init; }

    /// <summary>
    /// Number of new regressions detected.
    /// </summary>
    public int RegressionsDetected { get; init; }

    /// <summary>
    /// Number of hotspots identified.
    /// </summary>
    public int HotspotsDetected { get; init; }

    /// <summary>
    /// The detected regression events.
    /// </summary>
    public IReadOnlyList<RegressionEvent>? Regressions { get; init; }

    /// <summary>
    /// The detected hotspots.
    /// </summary>
    public IReadOnlyList<Hotspot>? Hotspots { get; init; }

    /// <summary>
    /// Error message if analysis failed.
    /// </summary>
    public string? Error { get; init; }

    public bool IsSuccess => Error == null;
}

/// <summary>
/// Result of hotspot analysis for a database.
/// </summary>
public sealed class HotspotAnalysisResult
{
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required DateTime AnalyzedAtUtc { get; init; }
    public required TimeSpan Duration { get; init; }

    public required IReadOnlyList<Hotspot> Hotspots { get; init; }

    public int Count => Hotspots.Count;
    public string? Error { get; init; }
    public bool IsSuccess => Error == null;
}
