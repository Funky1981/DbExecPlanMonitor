using System.Data;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Enums;
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

    // ========== Domain Entity Methods ==========

    /// <inheritdoc />
    public async Task SaveAsync(Domain.Entities.RegressionEvent regressionEvent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Saving RegressionEvent {Id} for fingerprint {FingerprintId}",
            regressionEvent.Id,
            regressionEvent.FingerprintId);

        const string sql = @"
            INSERT INTO monitoring.RegressionEvent (
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                RegressionType, MetricName, BaselineValue, CurrentValue,
                ChangePercent, ThresholdPercent, Severity,
                Status, Notes,
                SampleWindowStart, SampleWindowEnd,
                BaselineP95DurationUs, CurrentP95DurationUs, DurationChangePercent,
                BaselineP95CpuTimeUs, CurrentP95CpuTimeUs, CpuChangePercent,
                BaselineAvgLogicalReads, CurrentAvgLogicalReads
            ) VALUES (
                @Id, @FingerprintId, @InstanceName, @DatabaseName, @DetectedAtUtc,
                @RegressionType, @MetricName, @BaselineValue, @CurrentValue,
                @ChangePercent, @ThresholdPercent, @Severity,
                @Status, @Notes,
                @SampleWindowStart, @SampleWindowEnd,
                @BaselineP95DurationUs, @CurrentP95DurationUs, @DurationChangePercent,
                @BaselineP95CpuTimeUs, @CurrentP95CpuTimeUs, @CpuChangePercent,
                @BaselineAvgLogicalReads, @CurrentAvgLogicalReads
            )";

        await ExecuteNonQueryAsync(sql, p => ConfigureRegressionEventParameters(p, regressionEvent), ct);

        _logger.LogWarning(
            "Regression saved: {Severity} regression for {Instance}/{Database}",
            regressionEvent.Severity,
            regressionEvent.InstanceName,
            regressionEvent.DatabaseName);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Domain.Entities.RegressionEvent regressionEvent, CancellationToken ct = default)
    {
        _logger.LogDebug("Updating RegressionEvent {Id}", regressionEvent.Id);

        const string sql = @"
            UPDATE monitoring.RegressionEvent
            SET Status = @Status,
                AcknowledgedAtUtc = @AcknowledgedAtUtc,
                AcknowledgedBy = @AcknowledgedBy,
                ResolvedAtUtc = @ResolvedAtUtc,
                ResolvedBy = @ResolvedBy,
                ResolutionNotes = @ResolutionNotes,
                Notes = @Notes
            WHERE Id = @Id";

        await ExecuteNonQueryAsync(sql, p =>
        {
            AddGuidParameter(p, "@Id", regressionEvent.Id);
            p.Add(new SqlParameter("@Status", SqlDbType.TinyInt) 
            { 
                Value = (byte)regressionEvent.Status 
            });
            p.Add(new SqlParameter("@AcknowledgedAtUtc", SqlDbType.DateTime2)
            {
                Value = regressionEvent.AcknowledgedAtUtc.HasValue 
                    ? regressionEvent.AcknowledgedAtUtc.Value 
                    : DBNull.Value
            });
            AddNullableStringParameter(p, "@AcknowledgedBy", regressionEvent.AcknowledgedBy, 256);
            p.Add(new SqlParameter("@ResolvedAtUtc", SqlDbType.DateTime2)
            {
                Value = regressionEvent.ResolvedAtUtc.HasValue 
                    ? regressionEvent.ResolvedAtUtc.Value 
                    : DBNull.Value
            });
            AddNullableStringParameter(p, "@ResolvedBy", regressionEvent.ResolvedBy, 256);
            AddNullableStringParameter(p, "@ResolutionNotes", regressionEvent.ResolutionNotes, -1);
            AddNullableStringParameter(p, "@Notes", regressionEvent.Description, -1);
        }, ct);

        _logger.LogInformation("Updated RegressionEvent {Id} to status {Status}", 
            regressionEvent.Id, regressionEvent.Status);
    }

    /// <inheritdoc />
    public async Task<Domain.Entities.RegressionEvent?> GetActiveByFingerprintIdAsync(
        Guid fingerprintId,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                Severity, Status, Description, 
                BaselineP95DurationUs, BaselineP95CpuTimeUs, BaselineAvgLogicalReads,
                CurrentP95DurationUs, CurrentP95CpuTimeUs, CurrentAvgLogicalReads,
                DurationChangePercent, CpuChangePercent,
                SampleWindowStart, SampleWindowEnd,
                AcknowledgedAtUtc, AcknowledgedBy,
                ResolvedAtUtc, ResolvedBy, ResolutionNotes
            FROM monitoring.RegressionEvent
            WHERE FingerprintId = @FingerprintId 
              AND Status < 2  -- Not resolved
            ORDER BY DetectedAtUtc DESC";

        return await ExecuteQuerySingleAsync(
            sql,
            MapRegressionEvent,
            p => AddGuidParameter(p, "@FingerprintId", fingerprintId),
            ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Domain.Entities.RegressionEvent>> GetActiveAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, DetectedAtUtc,
                Severity, Status, Description, 
                BaselineP95DurationUs, BaselineP95CpuTimeUs, BaselineAvgLogicalReads,
                CurrentP95DurationUs, CurrentP95CpuTimeUs, CurrentAvgLogicalReads,
                DurationChangePercent, CpuChangePercent,
                SampleWindowStart, SampleWindowEnd,
                AcknowledgedAtUtc, AcknowledgedBy,
                ResolvedAtUtc, ResolvedBy, ResolutionNotes
            FROM monitoring.RegressionEvent
            WHERE Status < 2  -- Not resolved
            ORDER BY Severity DESC, DetectedAtUtc DESC";

        return await ExecuteQueryAsync(sql, MapRegressionEvent, null, ct);
    }

    /// <summary>
    /// Configures parameters for inserting a RegressionEvent domain entity.
    /// </summary>
    private static void ConfigureRegressionEventParameters(
        SqlParameterCollection p, 
        Domain.Entities.RegressionEvent evt)
    {
        AddGuidParameter(p, "@Id", evt.Id);
        AddGuidParameter(p, "@FingerprintId", evt.FingerprintId);
        AddStringParameter(p, "@InstanceName", evt.InstanceName, 256);
        AddStringParameter(p, "@DatabaseName", evt.DatabaseName, 128);
        AddDateTimeParameter(p, "@DetectedAtUtc", evt.DetectedAtUtc);

        // Use a default regression type
        p.Add(new SqlParameter("@RegressionType", SqlDbType.TinyInt) { Value = (byte)0 });
        AddStringParameter(p, "@MetricName", "P95Duration", 50);

        // For the legacy columns, use duration values
        AddNullableBigIntParameter(p, "@BaselineValue", evt.BaselineP95DurationUs);
        AddNullableBigIntParameter(p, "@CurrentValue", evt.CurrentP95DurationUs);
        AddNullableFloatParameter(p, "@ChangePercent", (double?)evt.DurationChangePercent);
        AddFloatParameter(p, "@ThresholdPercent", 50.0); // Default threshold
        p.Add(new SqlParameter("@Severity", SqlDbType.TinyInt) { Value = (byte)evt.Severity });
        p.Add(new SqlParameter("@Status", SqlDbType.TinyInt) { Value = (byte)evt.Status });
        AddNullableStringParameter(p, "@Notes", evt.Description, -1);

        p.Add(new SqlParameter("@SampleWindowStart", SqlDbType.DateTime2)
        {
            Value = evt.SampleWindowStart.HasValue ? evt.SampleWindowStart.Value : DBNull.Value
        });
        p.Add(new SqlParameter("@SampleWindowEnd", SqlDbType.DateTime2)
        {
            Value = evt.SampleWindowEnd.HasValue ? evt.SampleWindowEnd.Value : DBNull.Value
        });

        AddNullableBigIntParameter(p, "@BaselineP95DurationUs", evt.BaselineP95DurationUs);
        AddNullableBigIntParameter(p, "@CurrentP95DurationUs", evt.CurrentP95DurationUs);
        AddNullableFloatParameter(p, "@DurationChangePercent", (double?)evt.DurationChangePercent);
        AddNullableBigIntParameter(p, "@BaselineP95CpuTimeUs", evt.BaselineP95CpuTimeUs);
        AddNullableBigIntParameter(p, "@CurrentP95CpuTimeUs", evt.CurrentP95CpuTimeUs);
        AddNullableFloatParameter(p, "@CpuChangePercent", (double?)evt.CpuChangePercent);
        AddNullableBigIntParameter(p, "@BaselineAvgLogicalReads", evt.BaselineAvgLogicalReads);
        AddNullableBigIntParameter(p, "@CurrentAvgLogicalReads", evt.CurrentAvgLogicalReads);
    }

    /// <summary>
    /// Maps a SqlDataReader row to a RegressionEvent domain entity.
    /// </summary>
    private static Domain.Entities.RegressionEvent MapRegressionEvent(SqlDataReader reader)
    {
        return new Domain.Entities.RegressionEvent
        {
            Id = reader.GetGuid(0),
            FingerprintId = reader.GetGuid(1),
            InstanceName = reader.GetString(2),
            DatabaseName = reader.GetString(3),
            DetectedAtUtc = reader.GetDateTime(4),
            Severity = (Domain.Enums.RegressionSeverity)reader.GetByte(5),
            Status = (Domain.Enums.RegressionStatus)reader.GetByte(6),
            Description = reader.IsDBNull(7) ? null : reader.GetString(7),
            BaselineP95DurationUs = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            BaselineP95CpuTimeUs = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            BaselineAvgLogicalReads = reader.IsDBNull(10) ? null : reader.GetInt64(10),
            CurrentP95DurationUs = reader.IsDBNull(11) ? null : reader.GetInt64(11),
            CurrentP95CpuTimeUs = reader.IsDBNull(12) ? null : reader.GetInt64(12),
            CurrentAvgLogicalReads = reader.IsDBNull(13) ? null : reader.GetInt64(13),
            DurationChangePercent = reader.IsDBNull(14) ? null : (decimal)reader.GetDouble(14),
            CpuChangePercent = reader.IsDBNull(15) ? null : (decimal)reader.GetDouble(15),
            SampleWindowStart = reader.IsDBNull(16) ? null : reader.GetDateTime(16),
            SampleWindowEnd = reader.IsDBNull(17) ? null : reader.GetDateTime(17),
            AcknowledgedAtUtc = reader.IsDBNull(18) ? null : reader.GetDateTime(18),
            AcknowledgedBy = reader.IsDBNull(19) ? null : reader.GetString(19),
            ResolvedAtUtc = reader.IsDBNull(20) ? null : reader.GetDateTime(20),
            ResolvedBy = reader.IsDBNull(21) ? null : reader.GetString(21),
            ResolutionNotes = reader.IsDBNull(22) ? null : reader.GetString(22)
        };
    }

    /// <summary>
    /// Adds a nullable bigint parameter.
    /// </summary>
    private static void AddNullableBigIntParameter(SqlParameterCollection p, string name, long? value)
    {
        p.Add(new SqlParameter(name, SqlDbType.BigInt)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });
    }

    /// <summary>
    /// Adds a nullable string parameter.
    /// </summary>
    private static void AddNullableStringParameter(SqlParameterCollection p, string name, string? value, int maxLength)
    {
        p.Add(new SqlParameter(name, SqlDbType.NVarChar, maxLength)
        {
            Value = value ?? (object)DBNull.Value
        });
    }

    /// <summary>
    /// Adds a nullable float parameter.
    /// </summary>
    private static void AddNullableFloatParameter(SqlParameterCollection p, string name, double? value)
    {
        p.Add(new SqlParameter(name, SqlDbType.Float)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });
    }
}
