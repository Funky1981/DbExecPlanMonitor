namespace DbExecPlanMonitor.Domain.ValueObjects;

/// <summary>
/// Identifies a database within an instance.
/// Combines server name and database name into a single identifier.
/// </summary>
public sealed class DatabaseIdentifier : IEquatable<DatabaseIdentifier>
{
    /// <summary>
    /// The SQL Server instance name (e.g., "PROD-SQL-01" or "localhost\SQLEXPRESS").
    /// </summary>
    public string ServerName { get; }

    /// <summary>
    /// The database name within the instance.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Creates a new database identifier.
    /// </summary>
    public DatabaseIdentifier(string serverName, string databaseName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Server name is required.", nameof(serverName));
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name is required.", nameof(databaseName));

        ServerName = serverName.Trim();
        DatabaseName = databaseName.Trim();
    }

    /// <summary>
    /// Parses a string in format "ServerName.DatabaseName" or "ServerName/DatabaseName".
    /// </summary>
    public static DatabaseIdentifier Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", nameof(value));

        // Try different separators
        var separators = new[] { '.', '/', '\\' };
        foreach (var sep in separators)
        {
            var parts = value.Split(sep, 2);
            if (parts.Length == 2)
            {
                return new DatabaseIdentifier(parts[0], parts[1]);
            }
        }

        throw new FormatException($"Cannot parse '{value}' as DatabaseIdentifier. Expected format: ServerName.DatabaseName");
    }

    /// <summary>
    /// Tries to parse a string, returning null on failure.
    /// </summary>
    public static DatabaseIdentifier? TryParse(string value)
    {
        try
        {
            return Parse(value);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the full identifier as "ServerName.DatabaseName".
    /// </summary>
    public string FullName => $"{ServerName}.{DatabaseName}";

    public override string ToString() => FullName;

    public bool Equals(DatabaseIdentifier? other)
    {
        if (other is null) return false;
        return string.Equals(ServerName, other.ServerName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(DatabaseName, other.DatabaseName, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as DatabaseIdentifier);

    public override int GetHashCode() => HashCode.Combine(
        ServerName.ToUpperInvariant(),
        DatabaseName.ToUpperInvariant());

    public static bool operator ==(DatabaseIdentifier? left, DatabaseIdentifier? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(DatabaseIdentifier? left, DatabaseIdentifier? right) => !(left == right);
}
