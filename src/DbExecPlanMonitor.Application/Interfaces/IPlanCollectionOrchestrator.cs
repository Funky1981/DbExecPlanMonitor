using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Orchestrates the collection of execution plan metrics from SQL Server instances.
/// This is the main entry point for the collection workflow, coordinating between
/// data providers (DMVs/Query Store), fingerprinting, and storage.
/// </summary>
public interface IPlanCollectionOrchestrator
{
    /// <summary>
    /// Collects metrics from all enabled database instances.
    /// This is typically called on a timer by the background worker.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Summary of the collection run</returns>
    Task<CollectionRunSummary> CollectAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Collects metrics from a specific database instance.
    /// </summary>
    /// <param name="instanceName">The configured instance name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Summary of the collection for this instance</returns>
    Task<InstanceCollectionResult> CollectInstanceAsync(
        string instanceName,
        CancellationToken ct = default);

    /// <summary>
    /// Collects metrics from a specific database within an instance.
    /// </summary>
    /// <param name="instanceName">The configured instance name</param>
    /// <param name="databaseName">The database to collect from</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Summary of the collection for this database</returns>
    Task<DatabaseCollectionResult> CollectDatabaseAsync(
        string instanceName,
        string databaseName,
        CancellationToken ct = default);
}

/// <summary>
/// Summary of a complete collection run across all instances.
/// </summary>
public sealed class CollectionRunSummary
{
    public required DateTime StartedAtUtc { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public required TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Results for each instance that was processed.
    /// </summary>
    public required IReadOnlyList<InstanceCollectionResult> InstanceResults { get; init; }

    /// <summary>
    /// Total number of instances processed.
    /// </summary>
    public int TotalInstances => InstanceResults.Count;

    /// <summary>
    /// Number of instances that completed successfully.
    /// </summary>
    public int SuccessfulInstances => InstanceResults.Count(r => r.IsSuccess);

    /// <summary>
    /// Number of instances that had errors.
    /// </summary>
    public int FailedInstances => InstanceResults.Count(r => !r.IsSuccess);

    /// <summary>
    /// Total number of queries collected across all instances.
    /// </summary>
    public int TotalQueriesCollected => InstanceResults.Sum(r => r.TotalQueriesCollected);

    /// <summary>
    /// Total number of samples saved across all instances.
    /// </summary>
    public int TotalSamplesSaved => InstanceResults.Sum(r => r.TotalSamplesSaved);

    /// <summary>
    /// Whether the entire collection run was successful (no instance failures).
    /// </summary>
    public bool IsFullySuccessful => FailedInstances == 0;
}

/// <summary>
/// Result of collecting metrics from a single database instance.
/// </summary>
public sealed class InstanceCollectionResult
{
    public required string InstanceName { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Results for each database that was processed.
    /// </summary>
    public required IReadOnlyList<DatabaseCollectionResult> DatabaseResults { get; init; }

    /// <summary>
    /// Whether the instance collection was successful overall.
    /// </summary>
    public bool IsSuccess => Error == null && DatabaseResults.All(d => d.IsSuccess);

    /// <summary>
    /// Error message if the instance-level collection failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Total queries collected across all databases in this instance.
    /// </summary>
    public int TotalQueriesCollected => DatabaseResults.Sum(d => d.QueriesCollected);

    /// <summary>
    /// Total samples saved across all databases in this instance.
    /// </summary>
    public int TotalSamplesSaved => DatabaseResults.Sum(d => d.SamplesSaved);
}

/// <summary>
/// Result of collecting metrics from a single database.
/// </summary>
public sealed class DatabaseCollectionResult
{
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether collection was successful.
    /// </summary>
    public bool IsSuccess => Error == null;

    /// <summary>
    /// Error message if collection failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Number of queries retrieved from DMVs/Query Store.
    /// </summary>
    public int QueriesCollected { get; init; }

    /// <summary>
    /// Number of new fingerprints created (first time seeing these queries).
    /// </summary>
    public int NewFingerprintsCreated { get; init; }

    /// <summary>
    /// Number of metric samples saved to storage.
    /// </summary>
    public int SamplesSaved { get; init; }

    /// <summary>
    /// Whether Query Store was used (true) or DMVs (false).
    /// </summary>
    public bool UsedQueryStore { get; init; }

    /// <summary>
    /// The time window that was sampled.
    /// </summary>
    public TimeWindow? SampledWindow { get; init; }
}
