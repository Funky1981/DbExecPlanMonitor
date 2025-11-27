namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Provides access to execution plan details (the XML plan).
/// Implemented by infrastructure layer.
/// </summary>
public interface IPlanDetailsProvider
{
    /// <summary>
    /// Gets the execution plan XML for a specific plan.
    /// </summary>
    /// <param name="databaseId">The database ID.</param>
    /// <param name="planHandle">The plan handle from SQL Server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plan details, or null if not found/expired.</returns>
    Task<PlanDetailsResult?> GetPlanByHandleAsync(
        Guid databaseId,
        byte[] planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the execution plan from Query Store by plan ID.
    /// </summary>
    /// <param name="databaseId">The database ID.</param>
    /// <param name="queryStorePlanId">The Query Store plan_id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plan details, or null if not found.</returns>
    Task<PlanDetailsResult?> GetPlanFromQueryStoreAsync(
        Guid databaseId,
        long queryStorePlanId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all cached plans for a query hash.
    /// </summary>
    /// <param name="databaseId">The database ID.</param>
    /// <param name="queryHash">The query hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All available plans for the query.</returns>
    Task<IReadOnlyList<PlanDetailsResult>> GetPlansForQueryAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result containing execution plan details.
/// </summary>
public class PlanDetailsResult
{
    /// <summary>
    /// The plan hash (identifies this plan variant).
    /// </summary>
    public required string PlanHash { get; init; }

    /// <summary>
    /// The full execution plan XML (showplan format).
    /// </summary>
    public required string PlanXml { get; init; }

    /// <summary>
    /// The estimated subtree cost from the optimizer.
    /// </summary>
    public double EstimatedCost { get; init; }

    /// <summary>
    /// Estimated number of rows.
    /// </summary>
    public double? EstimatedRows { get; init; }

    /// <summary>
    /// Whether the plan uses parallelism.
    /// </summary>
    public bool IsParallel { get; init; }

    /// <summary>
    /// Degree of parallelism.
    /// </summary>
    public int? DegreeOfParallelism { get; init; }

    /// <summary>
    /// Query Store plan ID (if from Query Store).
    /// </summary>
    public long? QueryStorePlanId { get; init; }

    /// <summary>
    /// Whether this plan is forced in Query Store.
    /// </summary>
    public bool IsForced { get; init; }

    /// <summary>
    /// When the plan was created/compiled.
    /// </summary>
    public DateTime? CreatedAtUtc { get; init; }

    /// <summary>
    /// When the plan was last used.
    /// </summary>
    public DateTime? LastUsedAtUtc { get; init; }
}
