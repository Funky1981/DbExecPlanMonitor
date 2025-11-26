using System.Data;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Infrastructure.Persistence;

/// <summary>
/// SQL Server implementation of the regression event repository.
/// Handles storage and retrieval of detected performance regressions.
/// </summary>
public sealed class SqlRegressionEventRepository : RepositoryBase, IRegressionEventRepository
{
    private readonly ILogger<SqlRegressionEventRepository> _logger;

    public SqlRegressionEventRepository(
        IOptions<MonitoringStorageOptions> options,
        ILogger<SqlRegressionEventRepository> logger)
        : base(options.Value.ConnectionString, logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveEventAsync(RegressionEventRecord regressionEvent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Saving {Severity} regression event for fingerprint {FingerprintId}",
            regressionEvent.Severity,
            regressionEvent.FingerprintId);

        const string sql = @"
            INSERT INTO monitoring.RegressionEvent (
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                QueryTextSample, BaselinePlanHash, CurrentPlanHash, IsPlanChange,
                Status, Notes
            ) VALUES (
                @Id, @FingerprintId, @InstanceName, @DatabaseName, @DetectedAtUtc,
                @RegressionType, @MetricName, @BaselineValue, @CurrentValue,
                @ChangePercent, @ThresholdPercent, @Severity,
                @QueryTextSample, @BaselinePlanHash, @CurrentPlanHash, @IsPlanChange,
                @Status, @Notes
            )";

        await ExecuteNonQueryAsync(
            sql,
            p => ConfigureEventParameters(p, regressionEvent),
            ct);

        _logger.LogWarning(
            "Regression detected: {Summary}",
            regressionEvent.Summary);
    }

    /// <inheritdoc />
    public async Task SaveEventsAsync(
        IEnumerable<RegressionEventRecord> events,
        CancellationToken ct = default)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0)
            return;

        _logger.LogDebug("Saving {Count} regression events", eventList.Count);

        const string sql = @"
            INSERT INTO monitoring.RegressionEvent (
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                QueryTextSample, BaselinePlanHash, CurrentPlanHash, IsPlanChange,
                Status, Notes
            ) VALUES (
                @Id, @FingerprintId, @InstanceName, @DatabaseName, @DetectedAtUtc,
                @RegressionType, @MetricName, @BaselineValue, @CurrentValue,
                @ChangePercent, @ThresholdPercent, @Severity,
                @QueryTextSample, @BaselinePlanHash, @CurrentPlanHash, @IsPlanChange,
                @Status, @Notes
            )";

        await ExecuteBatchInsertAsync(eventList, sql, ConfigureEventParameters, ct);

        foreach (var evt in eventList)
        {
            _logger.LogWarning("Regression detected: {Summary}", evt.Summary);
        }
    }

    /// <inheritdoc />
    public async Task<RegressionEventRecord?> GetByIdAsync(Guid eventId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                QueryTextSample, BaselinePlanHash, CurrentPlanHash, IsPlanChange,
                Status, AcknowledgedAtUtc, AcknowledgedBy,
                ResolvedAtUtc, ResolvedBy, Notes
            FROM monitoring.RegressionEvent
            WHERE Id = @Id";

        return await ExecuteQuerySingleAsync(
            sql,
            MapEvent,
            p => AddGuidParameter(p, "@Id", eventId),
            ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegressionEventRecord>> GetRecentEventsAsync(
        TimeWindow window,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                QueryTextSample, BaselinePlanHash, CurrentPlanHash, IsPlanChange,
                Status, AcknowledgedAtUtc, AcknowledgedBy,
                ResolvedAtUtc, ResolvedBy, Notes
            FROM monitoring.RegressionEvent
            WHERE DetectedAtUtc BETWEEN @StartUtc AND @EndUtc
            ORDER BY DetectedAtUtc DESC, Severity DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapEvent,
            p =>
            {
                AddDateTimeParameter(p, "@StartUtc", window.StartUtc);
                AddDateTimeParameter(p, "@EndUtc", window.EndUtc);
            },
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegressionEventRecord>> GetEventsForInstanceAsync(
        string instanceName,
        TimeWindow window,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                QueryTextSample, BaselinePlanHash, CurrentPlanHash, IsPlanChange,
                Status, AcknowledgedAtUtc, AcknowledgedBy,
                ResolvedAtUtc, ResolvedBy, Notes
            FROM monitoring.RegressionEvent
            WHERE InstanceName = @InstanceName
              AND DetectedAtUtc BETWEEN @StartUtc AND @EndUtc
            ORDER BY DetectedAtUtc DESC, Severity DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapEvent,
            p =>
            {
                AddStringParameter(p, "@InstanceName", instanceName, 256);
                AddDateTimeParameter(p, "@StartUtc", window.StartUtc);
                AddDateTimeParameter(p, "@EndUtc", window.EndUtc);
            },
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegressionEventRecord>> GetEventsForFingerprintAsync(
        Guid fingerprintId,
        TimeWindow window,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                QueryTextSample, BaselinePlanHash, CurrentPlanHash, IsPlanChange,
                Status, AcknowledgedAtUtc, AcknowledgedBy,
                ResolvedAtUtc, ResolvedBy, Notes
            FROM monitoring.RegressionEvent
            WHERE FingerprintId = @FingerprintId
              AND DetectedAtUtc BETWEEN @StartUtc AND @EndUtc
            ORDER BY DetectedAtUtc DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapEvent,
            p =>
            {
                AddGuidParameter(p, "@FingerprintId", fingerprintId);
                AddDateTimeParameter(p, "@StartUtc", window.StartUtc);
                AddDateTimeParameter(p, "@EndUtc", window.EndUtc);
            },
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegressionEventRecord>> GetUnacknowledgedEventsAsync(
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                QueryTextSample, BaselinePlanHash, CurrentPlanHash, IsPlanChange,
                Status, AcknowledgedAtUtc, AcknowledgedBy,
                ResolvedAtUtc, ResolvedBy, Notes
            FROM monitoring.RegressionEvent
            WHERE Status = 0
            ORDER BY Severity DESC, DetectedAtUtc DESC";

        var results = await ExecuteQueryAsync(sql, MapEvent, null, ct);
        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegressionEventRecord>> GetEventsBySeverityAsync(
        RegressionSeverity minSeverity,
        TimeWindow window,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                QueryTextSample, BaselinePlanHash, CurrentPlanHash, IsPlanChange,
                Status, AcknowledgedAtUtc, AcknowledgedBy,
                ResolvedAtUtc, ResolvedBy, Notes
            FROM monitoring.RegressionEvent
            WHERE Severity >= @MinSeverity
              AND DetectedAtUtc BETWEEN @StartUtc AND @EndUtc
            ORDER BY Severity DESC, DetectedAtUtc DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapEvent,
            p =>
            {
                p.Add(new SqlParameter("@MinSeverity", SqlDbType.TinyInt) { Value = (byte)minSeverity });
                AddDateTimeParameter(p, "@StartUtc", window.StartUtc);
                AddDateTimeParameter(p, "@EndUtc", window.EndUtc);
            },
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task AcknowledgeEventAsync(
        Guid eventId,
        string acknowledgedBy,
        string? notes = null,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE monitoring.RegressionEvent
            SET Status = 1,
                AcknowledgedAtUtc = @AcknowledgedAtUtc,
                AcknowledgedBy = @AcknowledgedBy,
                Notes = COALESCE(@Notes, Notes)
            WHERE Id = @Id";

        await ExecuteNonQueryAsync(
            sql,
            p =>
            {
                AddGuidParameter(p, "@Id", eventId);
                AddDateTimeParameter(p, "@AcknowledgedAtUtc", DateTime.UtcNow);
                AddStringParameter(p, "@AcknowledgedBy", acknowledgedBy, 256);
                AddStringParameter(p, "@Notes", notes, -1);
            },
            ct);

        _logger.LogInformation(
            "Regression event {EventId} acknowledged by {User}",
            eventId,
            acknowledgedBy);
    }

    /// <inheritdoc />
    public async Task ResolveEventAsync(
        Guid eventId,
        string resolvedBy,
        string? resolutionNotes = null,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE monitoring.RegressionEvent
            SET Status = 2,
                ResolvedAtUtc = @ResolvedAtUtc,
                ResolvedBy = @ResolvedBy,
                Notes = CASE 
                    WHEN @ResolutionNotes IS NOT NULL 
                    THEN COALESCE(Notes + CHAR(13) + CHAR(10), '') + @ResolutionNotes
                    ELSE Notes
                END
            WHERE Id = @Id";

        await ExecuteNonQueryAsync(
            sql,
            p =>
            {
                AddGuidParameter(p, "@Id", eventId);
                AddDateTimeParameter(p, "@ResolvedAtUtc", DateTime.UtcNow);
                AddStringParameter(p, "@ResolvedBy", resolvedBy, 256);
                AddStringParameter(p, "@ResolutionNotes", resolutionNotes, -1);
            },
            ct);

        _logger.LogInformation(
            "Regression event {EventId} resolved by {User}",
            eventId,
            resolvedBy);
    }

    /// <inheritdoc />
    public async Task<RegressionSummary> GetSummaryAsync(
        TimeWindow window,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        
        command.CommandText = "monitoring.usp_GetRegressionSummary";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = CommandTimeoutSeconds;
        
        command.Parameters.Add(new SqlParameter("@StartUtc", SqlDbType.DateTime2) { Value = window.StartUtc });
        command.Parameters.Add(new SqlParameter("@EndUtc", SqlDbType.DateTime2) { Value = window.EndUtc });

        await using var reader = await command.ExecuteReaderAsync(ct);
        
        if (await reader.ReadAsync(ct))
        {
            return new RegressionSummary
            {
                Window = window,
                TotalEvents = reader.GetInt32(0),
                NewEvents = reader.GetInt32(1),
                AcknowledgedEvents = reader.GetInt32(2),
                ResolvedEvents = reader.GetInt32(3),
                CriticalEvents = reader.GetInt32(4),
                HighEvents = reader.GetInt32(5),
                MediumEvents = reader.GetInt32(6),
                LowEvents = reader.GetInt32(7),
                UniqueQueriesAffected = reader.GetInt32(8),
                UniqueDatabasesAffected = reader.GetInt32(9)
            };
        }

        // Return empty summary if no data
        return new RegressionSummary
        {
            Window = window,
            TotalEvents = 0,
            NewEvents = 0,
            AcknowledgedEvents = 0,
            ResolvedEvents = 0,
            CriticalEvents = 0,
            HighEvents = 0,
            MediumEvents = 0,
            LowEvents = 0,
            UniqueQueriesAffected = 0,
            UniqueDatabasesAffected = 0
        };
    }

    /// <inheritdoc />
    public async Task<int> PurgeEventsOlderThanAsync(DateTime olderThan, CancellationToken ct = default)
    {
        _logger.LogInformation("Purging regression events older than {OlderThan}", olderThan);

        const string sql = @"
            DELETE FROM monitoring.RegressionEvent
            WHERE DetectedAtUtc < @OlderThan";

        var deleted = await ExecuteNonQueryAsync(
            sql,
            p => AddDateTimeParameter(p, "@OlderThan", olderThan),
            ct);

        _logger.LogInformation("Purged {Count} old regression events", deleted);
        return deleted;
    }

    /// <summary>
    /// Configures parameters for inserting a regression event.
    /// </summary>
    private static void ConfigureEventParameters(SqlParameterCollection p, RegressionEventRecord evt)
    {
        AddGuidParameter(p, "@Id", evt.Id);
        AddGuidParameter(p, "@FingerprintId", evt.FingerprintId);
        AddStringParameter(p, "@InstanceName", evt.InstanceName, 256);
        AddStringParameter(p, "@DatabaseName", evt.DatabaseName, 128);
        AddDateTimeParameter(p, "@DetectedAtUtc", evt.DetectedAtUtc);
        
        p.Add(new SqlParameter("@RegressionType", SqlDbType.TinyInt) { Value = (byte)evt.Type });
        AddStringParameter(p, "@MetricName", evt.MetricName, 50);
        AddBigIntParameter(p, "@BaselineValue", evt.BaselineValue);
        AddBigIntParameter(p, "@CurrentValue", evt.CurrentValue);
        AddFloatParameter(p, "@ChangePercent", evt.ChangePercent);
        AddFloatParameter(p, "@ThresholdPercent", evt.ThresholdPercent);
        p.Add(new SqlParameter("@Severity", SqlDbType.TinyInt) { Value = (byte)evt.Severity });
        
        AddStringParameter(p, "@QueryTextSample", evt.QueryTextSample, -1);
        AddBinaryParameter(p, "@BaselinePlanHash", evt.BaselinePlanHash, 32);
        AddBinaryParameter(p, "@CurrentPlanHash", evt.CurrentPlanHash, 32);
        AddBoolParameter(p, "@IsPlanChange", evt.IsPlanChange);
        
        p.Add(new SqlParameter("@Status", SqlDbType.TinyInt) { Value = (byte)evt.Status });
        AddStringParameter(p, "@Notes", evt.Notes, -1);
    }

    /// <summary>
    /// Maps a SqlDataReader row to a RegressionEventRecord.
    /// </summary>
    private static RegressionEventRecord MapEvent(SqlDataReader reader)
    {
        return new RegressionEventRecord
        {
            Id = reader.GetGuid(0),
            FingerprintId = reader.GetGuid(1),
            InstanceName = reader.GetString(2),
            DatabaseName = reader.GetString(3),
            DetectedAtUtc = reader.GetDateTime(4),
            Type = (RegressionType)reader.GetByte(5),
            MetricName = reader.GetString(6),
            BaselineValue = reader.GetInt64(7),
            CurrentValue = reader.GetInt64(8),
            ChangePercent = reader.GetDouble(9),
            ThresholdPercent = reader.GetDouble(10),
            Severity = (RegressionSeverity)reader.GetByte(11),
            QueryTextSample = reader.IsDBNull(12) ? null : reader.GetString(12),
            BaselinePlanHash = reader.IsDBNull(13) ? null : GetBytes(reader, 13),
            CurrentPlanHash = reader.IsDBNull(14) ? null : GetBytes(reader, 14),
            IsPlanChange = reader.GetBoolean(15),
            Status = (RegressionStatus)reader.GetByte(16),
            AcknowledgedAtUtc = reader.IsDBNull(17) ? null : reader.GetDateTime(17),
            AcknowledgedBy = reader.IsDBNull(18) ? null : reader.GetString(18),
            ResolvedAtUtc = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
            ResolvedBy = reader.IsDBNull(20) ? null : reader.GetString(20),
            Notes = reader.IsDBNull(21) ? null : reader.GetString(21)
        };
    }

    /// <summary>
    /// Safely reads binary data from a SqlDataReader.
    /// </summary>
    private static byte[] GetBytes(SqlDataReader reader, int ordinal)
    {
        var length = (int)reader.GetBytes(ordinal, 0, null, 0, 0);
        var buffer = new byte[length];
        reader.GetBytes(ordinal, 0, buffer, 0, length);
        return buffer;
    }
}
