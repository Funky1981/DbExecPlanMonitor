namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents a specific database within a SQL Server instance that we are monitoring.
/// Each database has its own set of query fingerprints and execution plans.
/// </summary>
public class MonitoredDatabase
{
    /// <summary>
    /// Unique identifier for this monitored database.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the parent SQL Server instance.
    /// </summary>
    public Guid DatabaseInstanceId { get; private set; }

    /// <summary>
    /// Navigation property to the parent instance.
    /// </summary>
    public DatabaseInstance DatabaseInstance { get; private set; } = null!;

    /// <summary>
    /// The actual database name in SQL Server (e.g., "OrdersDB").
    /// </summary>
    public string DatabaseName { get; private set; }

    /// <summary>
    /// Whether Query Store is enabled on this database.
    /// Query Store provides much richer plan data than DMVs alone.
    /// </summary>
    public bool IsQueryStoreEnabled { get; private set; }

    /// <summary>
    /// Whether monitoring is active for this specific database.
    /// Can be paused independently of the instance-level setting.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// How often we sample execution plans (in seconds).
    /// Lower = more granular but more overhead. Default is 60 seconds.
    /// </summary>
    public int SamplingIntervalSeconds { get; private set; }

    /// <summary>
    /// Minimum CPU time (ms) for a query to be tracked.
    /// Filters out trivial queries to reduce noise.
    /// </summary>
    public int MinimumCpuTimeMs { get; private set; }

    /// <summary>
    /// Minimum logical reads for a query to be tracked.
    /// Helps focus on queries that actually touch data.
    /// </summary>
    public int MinimumLogicalReads { get; private set; }

    /// <summary>
    /// When this database was added to monitoring.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Last successful data collection from this database.
    /// Helps detect if collection has stalled.
    /// </summary>
    public DateTime? LastCollectionAtUtc { get; private set; }

    /// <summary>
    /// The query fingerprints we've discovered in this database.
    /// A fingerprint is a normalized/parameterized query pattern.
    /// </summary>
    private readonly List<QueryFingerprint> _queryFingerprints = new();
    public IReadOnlyCollection<QueryFingerprint> QueryFingerprints => _queryFingerprints.AsReadOnly();

    // Private constructor for EF Core
    private MonitoredDatabase() { }

    /// <summary>
    /// Creates a new monitored database. Called by DatabaseInstance.AddDatabase().
    /// </summary>
    internal MonitoredDatabase(DatabaseInstance instance, string databaseName)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name is required.", nameof(databaseName));

        Id = Guid.NewGuid();
        DatabaseInstanceId = instance.Id;
        DatabaseInstance = instance;
        DatabaseName = databaseName.Trim();
        IsQueryStoreEnabled = false; // Will be detected on first collection
        IsActive = true;
        
        // Sensible defaults - can be tuned per database
        SamplingIntervalSeconds = 60;
        MinimumCpuTimeMs = 100;        // Ignore queries under 100ms CPU
        MinimumLogicalReads = 1000;    // Ignore queries reading < 1000 pages
        
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the sampling configuration for this database.
    /// </summary>
    public void ConfigureSampling(
        int samplingIntervalSeconds,
        int minimumCpuTimeMs,
        int minimumLogicalReads)
    {
        if (samplingIntervalSeconds < 10)
            throw new ArgumentException("Sampling interval must be at least 10 seconds.", nameof(samplingIntervalSeconds));
        if (samplingIntervalSeconds > 3600)
            throw new ArgumentException("Sampling interval cannot exceed 1 hour.", nameof(samplingIntervalSeconds));

        SamplingIntervalSeconds = samplingIntervalSeconds;
        MinimumCpuTimeMs = Math.Max(0, minimumCpuTimeMs);
        MinimumLogicalReads = Math.Max(0, minimumLogicalReads);
    }

    /// <summary>
    /// Records that Query Store is available on this database.
    /// Called during collection when we detect QS is enabled.
    /// </summary>
    public void MarkQueryStoreEnabled()
    {
        IsQueryStoreEnabled = true;
    }

    /// <summary>
    /// Records that Query Store is not available.
    /// We'll fall back to DMV-based collection.
    /// </summary>
    public void MarkQueryStoreDisabled()
    {
        IsQueryStoreEnabled = false;
    }

    /// <summary>
    /// Records a successful data collection.
    /// </summary>
    public void RecordCollection()
    {
        LastCollectionAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Registers a new query fingerprint discovered in this database.
    /// </summary>
    public QueryFingerprint RegisterQueryFingerprint(
        string queryHash,
        string normalizedQueryText,
        string? objectName = null)
    {
        // Check if we already have this fingerprint
        var existing = _queryFingerprints.FirstOrDefault(
            f => f.QueryHash.Equals(queryHash, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return existing;

        var fingerprint = new QueryFingerprint(this, queryHash, normalizedQueryText, objectName);
        _queryFingerprints.Add(fingerprint);

        return fingerprint;
    }

    /// <summary>
    /// Pauses monitoring for this database.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Resumes monitoring for this database.
    /// </summary>
    public void Activate()
    {
        IsActive = true;
    }

    /// <summary>
    /// Checks if this database should be collected now.
    /// </summary>
    public bool IsDueForCollection()
    {
        if (!IsActive)
            return false;

        if (LastCollectionAtUtc == null)
            return true; // Never collected

        var elapsed = DateTime.UtcNow - LastCollectionAtUtc.Value;
        return elapsed.TotalSeconds >= SamplingIntervalSeconds;
    }
}
