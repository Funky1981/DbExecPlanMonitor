namespace DbExecPlanMonitor.Domain.ValueObjects;

/// <summary>
/// Represents a query hash identifier from SQL Server.
/// Wraps the binary hash with helpful formatting and comparison.
/// </summary>
/// <remarks>
/// SQL Server provides query_hash and plan_hash as BINARY(8) values.
/// We store them as hex strings (e.g., "0x7A3B2C1D00000000") for:
/// - Human readability in logs and UI
/// - Easy string comparison
/// - JSON serialization
/// </remarks>
public sealed class QueryHash : IEquatable<QueryHash>
{
    /// <summary>
    /// The hash value as a hex string (e.g., "0x7A3B2C1D00000000").
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new QueryHash from a hex string.
    /// </summary>
    public QueryHash(string hexValue)
    {
        if (string.IsNullOrWhiteSpace(hexValue))
            throw new ArgumentException("Hash value is required.", nameof(hexValue));

        // Normalize: ensure it starts with 0x and is uppercase
        Value = Normalize(hexValue);
    }

    /// <summary>
    /// Creates a QueryHash from a binary byte array (from SQL Server).
    /// </summary>
    public static QueryHash FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            throw new ArgumentException("Bytes are required.", nameof(bytes));

        var hex = "0x" + BitConverter.ToString(bytes).Replace("-", "");
        return new QueryHash(hex);
    }

    /// <summary>
    /// Normalizes a hex string to consistent format.
    /// </summary>
    private static string Normalize(string hexValue)
    {
        var trimmed = hexValue.Trim();
        
        // Add 0x prefix if missing
        if (!trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "0x" + trimmed;
        }

        // Uppercase for consistency
        return trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// Gets a shortened version for display (first 10 chars).
    /// </summary>
    public string ToShortString()
    {
        return Value.Length > 10 ? Value.Substring(0, 10) + "..." : Value;
    }

    public override string ToString() => Value;

    public bool Equals(QueryHash? other)
    {
        if (other is null) return false;
        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as QueryHash);

    public override int GetHashCode() => Value.ToUpperInvariant().GetHashCode();

    public static bool operator ==(QueryHash? left, QueryHash? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(QueryHash? left, QueryHash? right) => !(left == right);

    public static implicit operator string(QueryHash hash) => hash.Value;
}

/// <summary>
/// Represents a plan hash identifier from SQL Server.
/// Similar to QueryHash but semantically different (identifies a specific plan).
/// </summary>
public sealed class PlanHash : IEquatable<PlanHash>
{
    /// <summary>
    /// The hash value as a hex string.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new PlanHash from a hex string.
    /// </summary>
    public PlanHash(string hexValue)
    {
        if (string.IsNullOrWhiteSpace(hexValue))
            throw new ArgumentException("Hash value is required.", nameof(hexValue));

        Value = Normalize(hexValue);
    }

    /// <summary>
    /// Creates a PlanHash from a binary byte array.
    /// </summary>
    public static PlanHash FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            throw new ArgumentException("Bytes are required.", nameof(bytes));

        var hex = "0x" + BitConverter.ToString(bytes).Replace("-", "");
        return new PlanHash(hex);
    }

    private static string Normalize(string hexValue)
    {
        var trimmed = hexValue.Trim();
        if (!trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "0x" + trimmed;
        }
        return trimmed.ToUpperInvariant();
    }

    public string ToShortString()
    {
        return Value.Length > 10 ? Value.Substring(0, 10) + "..." : Value;
    }

    public override string ToString() => Value;

    public bool Equals(PlanHash? other)
    {
        if (other is null) return false;
        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as PlanHash);

    public override int GetHashCode() => Value.ToUpperInvariant().GetHashCode();

    public static bool operator ==(PlanHash? left, PlanHash? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(PlanHash? left, PlanHash? right) => !(left == right);

    public static implicit operator string(PlanHash hash) => hash.Value;
}
