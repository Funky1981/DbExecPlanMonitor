using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DbExecPlanMonitor.Application.Interfaces;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Normalizes SQL text to produce stable query fingerprints.
/// The fingerprint allows grouping "the same query" even when literal values differ.
/// </summary>
public sealed partial class QueryFingerprintService : IQueryFingerprintService
{
    /// <summary>
    /// Creates a fingerprint by normalizing the SQL text and hashing it.
    /// </summary>
    public QueryFingerprintResult CreateFingerprint(string sqlText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlText);

        var normalized = NormalizeSql(sqlText);
        var hash = ComputeHash(normalized);

        return new QueryFingerprintResult
        {
            Hash = hash,
            SampleText = TruncateSample(sqlText),
            NormalizedText = TruncateSample(normalized),
            IsFromServerHash = false
        };
    }

    /// <summary>
    /// Creates a fingerprint using a pre-computed hash from SQL Server.
    /// This is preferred when available as it's guaranteed consistent.
    /// </summary>
    public QueryFingerprintResult CreateFingerprintFromHash(byte[] queryHash, string sqlText)
    {
        ArgumentNullException.ThrowIfNull(queryHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlText);

        // SQL Server query_hash is 8 bytes - we store as-is
        if (queryHash.Length != 8)
        {
            throw new ArgumentException(
                $"SQL Server query_hash should be 8 bytes, got {queryHash.Length}",
                nameof(queryHash));
        }

        var normalized = NormalizeSql(sqlText);

        return new QueryFingerprintResult
        {
            Hash = queryHash,
            SampleText = TruncateSample(sqlText),
            NormalizedText = TruncateSample(normalized),
            IsFromServerHash = true
        };
    }

    /// <summary>
    /// Normalizes SQL text by removing/replacing elements that vary between executions
    /// but don't change the query's logical identity.
    /// </summary>
    public string NormalizeSql(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
            return string.Empty;

        var result = sqlText;

        // 1. Collapse all whitespace to single spaces
        result = WhitespaceRegex().Replace(result, " ");

        // 2. Remove leading/trailing whitespace
        result = result.Trim();

        // 3. Replace string literals with placeholder
        result = StringLiteralRegex().Replace(result, "'#'");

        // 4. Replace Unicode string literals with placeholder
        result = UnicodeStringLiteralRegex().Replace(result, "N'#'");

        // 5. Replace numeric literals with placeholder
        // Be careful not to replace parts of identifiers (e.g., table1)
        result = NumericLiteralRegex().Replace(result, "#");

        // 6. Replace GUID literals with placeholder
        result = GuidLiteralRegex().Replace(result, "'#GUID#'");

        // 7. Replace datetime literals with placeholder
        result = DateTimeLiteralRegex().Replace(result, "'#DATE#'");

        // 8. Remove inline comments
        result = InlineCommentRegex().Replace(result, " ");

        // 9. Remove block comments
        result = BlockCommentRegex().Replace(result, " ");

        // 10. Normalize case for keywords (uppercase)
        result = NormalizeKeywords(result);

        // 11. Final whitespace cleanup
        result = WhitespaceRegex().Replace(result, " ").Trim();

        return result;
    }

    /// <summary>
    /// Computes a hash of the normalized SQL text.
    /// We use SHA256 but only keep first 8 bytes to match SQL Server's query_hash size.
    /// </summary>
    private static byte[] ComputeHash(string normalizedSql)
    {
        var bytes = Encoding.UTF8.GetBytes(normalizedSql);
        var fullHash = SHA256.HashData(bytes);
        
        // Take first 8 bytes to match SQL Server query_hash format
        var truncatedHash = new byte[8];
        Array.Copy(fullHash, truncatedHash, 8);
        
        return truncatedHash;
    }

    /// <summary>
    /// Truncates SQL text to a reasonable sample size for storage.
    /// </summary>
    private static string TruncateSample(string sqlText, int maxLength = 4000)
    {
        if (string.IsNullOrEmpty(sqlText) || sqlText.Length <= maxLength)
            return sqlText;

        return string.Concat(sqlText.AsSpan(0, maxLength - 3), "...");
    }

    /// <summary>
    /// Normalizes common SQL keywords to uppercase.
    /// </summary>
    private static string NormalizeKeywords(string sql)
    {
        // Only normalize the most common keywords to avoid false positives
        // with identifiers that happen to match keyword names
        foreach (var keyword in SqlKeywords)
        {
            sql = Regex.Replace(
                sql,
                $@"\b{keyword}\b",
                keyword.ToUpperInvariant(),
                RegexOptions.IgnoreCase);
        }
        return sql;
    }

    private static readonly string[] SqlKeywords =
    [
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "IS", "NULL",
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "ON",
        "GROUP", "BY", "HAVING", "ORDER", "ASC", "DESC",
        "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "CREATE", "ALTER", "DROP", "TABLE", "INDEX", "VIEW",
        "UNION", "ALL", "DISTINCT", "TOP", "WITH", "AS",
        "CASE", "WHEN", "THEN", "ELSE", "END",
        "EXISTS", "BETWEEN", "LIKE", "ESCAPE",
        "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION"
    ];

    // Generated regex patterns for better performance
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"'(?:[^']|'')*'", RegexOptions.Compiled)]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"N'(?:[^']|'')*'", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UnicodeStringLiteralRegex();

    [GeneratedRegex(@"(?<![a-zA-Z_@#$])\d+\.?\d*(?![a-zA-Z_@#$])", RegexOptions.Compiled)]
    private static partial Regex NumericLiteralRegex();

    [GeneratedRegex(@"'[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}'", RegexOptions.Compiled)]
    private static partial Regex GuidLiteralRegex();

    [GeneratedRegex(@"'\d{4}-\d{2}-\d{2}(?:\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?)?'", RegexOptions.Compiled)]
    private static partial Regex DateTimeLiteralRegex();

    [GeneratedRegex(@"--[^\r\n]*", RegexOptions.Compiled)]
    private static partial Regex InlineCommentRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex BlockCommentRegex();
}
