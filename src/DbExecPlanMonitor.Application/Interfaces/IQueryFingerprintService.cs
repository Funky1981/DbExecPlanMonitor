namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Service for normalizing SQL text into stable query fingerprints.
/// A fingerprint allows grouping executions of "the same" query regardless
/// of literal values, whitespace differences, or formatting.
/// </summary>
public interface IQueryFingerprintService
{
    /// <summary>
    /// Creates a fingerprint from raw SQL text.
    /// The fingerprint includes a normalized hash and sample text.
    /// </summary>
    /// <param name="sqlText">The raw SQL text from DMVs or Query Store</param>
    /// <returns>A fingerprint containing the normalized hash and sample text</returns>
    QueryFingerprintResult CreateFingerprint(string sqlText);

    /// <summary>
    /// Creates a fingerprint from an existing query hash (from SQL Server).
    /// Used when SQL Server has already computed the hash (e.g., query_hash from DMVs).
    /// </summary>
    /// <param name="queryHash">The query hash from SQL Server</param>
    /// <param name="sqlText">The SQL text for reference</param>
    /// <returns>A fingerprint using the provided hash</returns>
    QueryFingerprintResult CreateFingerprintFromHash(byte[] queryHash, string sqlText);

    /// <summary>
    /// Normalizes SQL text by removing literals, normalizing whitespace, etc.
    /// </summary>
    /// <param name="sqlText">The raw SQL text</param>
    /// <returns>Normalized SQL text suitable for comparison</returns>
    string NormalizeSql(string sqlText);
}

/// <summary>
/// Result of fingerprint creation containing hash and normalized text.
/// </summary>
public sealed class QueryFingerprintResult
{
    /// <summary>
    /// The hash of the normalized SQL text (or SQL Server's query_hash).
    /// This is the stable identifier used to group query executions.
    /// </summary>
    public required byte[] Hash { get; init; }

    /// <summary>
    /// A sample of the SQL text for display/debugging purposes.
    /// May be truncated for very long queries.
    /// </summary>
    public required string SampleText { get; init; }

    /// <summary>
    /// The normalized SQL text (literals replaced, whitespace normalized).
    /// </summary>
    public string? NormalizedText { get; init; }

    /// <summary>
    /// Whether this fingerprint was created from SQL Server's query_hash
    /// (true) or computed by us (false).
    /// </summary>
    public bool IsFromServerHash { get; init; }

    /// <summary>
    /// Returns the hash as a hexadecimal string.
    /// </summary>
    public string HashHex => Convert.ToHexString(Hash);
}
