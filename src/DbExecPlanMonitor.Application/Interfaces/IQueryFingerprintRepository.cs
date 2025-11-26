using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Repository interface for managing query fingerprints.
/// A fingerprint is a normalized representation of a query that allows
/// grouping executions of "the same" query regardless of literal values.
/// </summary>
public interface IQueryFingerprintRepository
{
    /// <summary>
    /// Gets or creates a fingerprint for the given query hash.
    /// If the fingerprint already exists, returns the existing ID.
    /// If not, creates a new fingerprint record and returns the new ID.
    /// </summary>
    /// <param name="queryHash">The SQL Server query hash (from DMV or Query Store)</param>
    /// <param name="queryTextSample">A sample of the SQL text for reference</param>
    /// <param name="databaseName">The database where the query runs</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The fingerprint ID (existing or newly created)</returns>
    Task<Guid> GetOrCreateFingerprintAsync(
        byte[] queryHash,
        string queryTextSample,
        string databaseName,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a fingerprint by its ID.
    /// </summary>
    Task<QueryFingerprintRecord?> GetByIdAsync(Guid fingerprintId, CancellationToken ct = default);

    /// <summary>
    /// Gets a fingerprint by its query hash.
    /// </summary>
    Task<QueryFingerprintRecord?> GetByHashAsync(byte[] queryHash, CancellationToken ct = default);

    /// <summary>
    /// Gets all fingerprints for a specific database.
    /// </summary>
    Task<IReadOnlyList<QueryFingerprintRecord>> GetByDatabaseAsync(
        string databaseName,
        CancellationToken ct = default);

    /// <summary>
    /// Gets fingerprints that have been active within the specified time window.
    /// </summary>
    Task<IReadOnlyList<QueryFingerprintRecord>> GetActiveInWindowAsync(
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the last seen timestamp for a fingerprint.
    /// </summary>
    Task UpdateLastSeenAsync(Guid fingerprintId, DateTime lastSeenUtc, CancellationToken ct = default);
}

/// <summary>
/// Represents a query fingerprint record from the persistence store.
/// </summary>
public sealed class QueryFingerprintRecord
{
    public required Guid Id { get; init; }
    public required byte[] QueryHash { get; init; }
    public required string QueryTextSample { get; init; }
    public required string DatabaseName { get; init; }
    public required DateTime FirstSeenUtc { get; init; }
    public required DateTime LastSeenUtc { get; init; }

    /// <summary>
    /// Converts the query hash to a hexadecimal string for display.
    /// </summary>
    public string QueryHashHex => Convert.ToHexString(QueryHash);
}
