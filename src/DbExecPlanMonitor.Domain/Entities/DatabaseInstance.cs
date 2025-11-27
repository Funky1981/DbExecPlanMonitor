namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents a SQL Server instance that we are monitoring.
/// This is the aggregate root for all monitoring operations.
/// </summary>
public class DatabaseInstance
{
    /// <summary>
    /// Unique identifier for this database instance in our system.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The SQL Server instance name (e.g., "PROD-SQL-01" or "localhost\SQLEXPRESS").
    /// </summary>
    public string ServerName { get; private set; }

    /// <summary>
    /// Friendly display name for dashboards and alerts.
    /// </summary>
    public string DisplayName { get; private set; }

    /// <summary>
    /// Environment classification (Production, Staging, Development, etc.)
    /// Helps prioritize alerts - Production issues are more urgent.
    /// </summary>
    public string Environment { get; private set; }

    /// <summary>
    /// Whether monitoring is currently active for this instance.
    /// Allows us to pause monitoring without deleting configuration.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// When this instance was registered in our system.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Last time any configuration was modified.
    /// </summary>
    public DateTime? LastModifiedAtUtc { get; private set; }

    /// <summary>
    /// The databases we're monitoring on this instance.
    /// One SQL Server instance can host many databases.
    /// </summary>
    private readonly List<MonitoredDatabase> _monitoredDatabases = new();
    public IReadOnlyCollection<MonitoredDatabase> MonitoredDatabases => _monitoredDatabases.AsReadOnly();

    // Private constructor for EF Core / deserialization
    private DatabaseInstance()
    {
        ServerName = null!;
        DisplayName = null!;
        Environment = null!;
    }

    /// <summary>
    /// Creates a new database instance to monitor.
    /// </summary>
    public DatabaseInstance(
        string serverName,
        string displayName,
        string environment)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Server name is required.", nameof(serverName));

        Id = Guid.NewGuid();
        ServerName = serverName.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? serverName : displayName.Trim();
        Environment = environment ?? "Unknown";
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a database to be monitored on this instance.
    /// </summary>
    public MonitoredDatabase AddDatabase(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name is required.", nameof(databaseName));

        // Check for duplicates
        if (_monitoredDatabases.Any(d => d.DatabaseName.Equals(databaseName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Database '{databaseName}' is already being monitored.");

        var database = new MonitoredDatabase(this, databaseName);
        _monitoredDatabases.Add(database);
        LastModifiedAtUtc = DateTime.UtcNow;

        return database;
    }

    /// <summary>
    /// Pauses monitoring for this entire instance.
    /// Useful during maintenance windows.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        LastModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Resumes monitoring after a pause.
    /// </summary>
    public void Activate()
    {
        IsActive = true;
        LastModifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the display name and environment.
    /// </summary>
    public void UpdateDetails(string displayName, string environment)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? ServerName : displayName.Trim();
        Environment = environment ?? Environment;
        LastModifiedAtUtc = DateTime.UtcNow;
    }
}
