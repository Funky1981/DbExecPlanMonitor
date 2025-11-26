using System.Data;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Infrastructure.Persistence;

/// <summary>
/// SQL Server implementation of the baseline repository.
/// Handles storage and retrieval of query performance baselines.
/// </summary>
public sealed class SqlBaselineRepository : RepositoryBase, IBaselineRepository
{
    private readonly ILogger<SqlBaselineRepository> _logger;

    public SqlBaselineRepository(
        IOptions<MonitoringStorageOptions> options,
        ILogger<SqlBaselineRepository> logger)
        : base(options.Value.ConnectionString, logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveBaselineAsync(BaselineRecord baseline, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Saving baseline for fingerprint {FingerprintId}",
            baseline.FingerprintId);

        // Deactivate any existing active baseline for this fingerprint
        await DeactivateExistingBaselinesAsync(baseline.FingerprintId, ct);

        const string insertSql = @"
            INSERT INTO monitoring.Baseline (
                Id, FingerprintId, DatabaseName,
                BaselineStartUtc, BaselineEndUtc, CreatedAtUtc,
                SampleCount, TotalExecutions,
                MedianDurationUs, P95DurationUs, P99DurationUs, AvgDurationUs, DurationStdDev,
                MedianCpuTimeUs, P95CpuTimeUs, AvgCpuTimeUs,
                MedianLogicalReads, P95LogicalReads, AvgLogicalReads,
                ExpectedPlanHash, Notes, IsActive
            ) VALUES (
                @Id, @FingerprintId, @DatabaseName,
                @BaselineStartUtc, @BaselineEndUtc, @CreatedAtUtc,
                @SampleCount, @TotalExecutions,
                @MedianDurationUs, @P95DurationUs, @P99DurationUs, @AvgDurationUs, @DurationStdDev,
                @MedianCpuTimeUs, @P95CpuTimeUs, @AvgCpuTimeUs,
                @MedianLogicalReads, @P95LogicalReads, @AvgLogicalReads,
                @ExpectedPlanHash, @Notes, @IsActive
            )";

        await ExecuteNonQueryAsync(
            insertSql,
            p => ConfigureBaselineParameters(p, baseline),
            ct);

        _logger.LogInformation(
            "Saved baseline for fingerprint {FingerprintId} based on {SampleCount} samples",
            baseline.FingerprintId,
            baseline.SampleCount);
    }

    /// <inheritdoc />
    public async Task<BaselineRecord?> GetBaselineAsync(Guid fingerprintId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, DatabaseName,
                BaselineStartUtc, BaselineEndUtc, CreatedAtUtc,
                SampleCount, TotalExecutions,
                MedianDurationUs, P95DurationUs, P99DurationUs, AvgDurationUs, DurationStdDev,
                MedianCpuTimeUs, P95CpuTimeUs, AvgCpuTimeUs,
                MedianLogicalReads, P95LogicalReads, AvgLogicalReads,
                ExpectedPlanHash, Notes, IsActive
            FROM monitoring.Baseline
            WHERE FingerprintId = @FingerprintId AND IsActive = 1";

        return await ExecuteQuerySingleAsync(
            sql,
            MapBaseline,
            p => AddGuidParameter(p, "@FingerprintId", fingerprintId),
            ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BaselineRecord>> GetBaselinesForDatabaseAsync(
        string databaseName,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, DatabaseName,
                BaselineStartUtc, BaselineEndUtc, CreatedAtUtc,
                SampleCount, TotalExecutions,
                MedianDurationUs, P95DurationUs, P99DurationUs, AvgDurationUs, DurationStdDev,
                MedianCpuTimeUs, P95CpuTimeUs, AvgCpuTimeUs,
                MedianLogicalReads, P95LogicalReads, AvgLogicalReads,
                ExpectedPlanHash, Notes, IsActive
            FROM monitoring.Baseline
            WHERE DatabaseName = @DatabaseName AND IsActive = 1
            ORDER BY CreatedAtUtc DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapBaseline,
            p => AddStringParameter(p, "@DatabaseName", databaseName, 128),
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BaselineRecord>> GetStaleBaselinesAsync(
        TimeSpan maxAge,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;

        const string sql = @"
            SELECT 
                Id, FingerprintId, DatabaseName,
                BaselineStartUtc, BaselineEndUtc, CreatedAtUtc,
                SampleCount, TotalExecutions,
                MedianDurationUs, P95DurationUs, P99DurationUs, AvgDurationUs, DurationStdDev,
                MedianCpuTimeUs, P95CpuTimeUs, AvgCpuTimeUs,
                MedianLogicalReads, P95LogicalReads, AvgLogicalReads,
                ExpectedPlanHash, Notes, IsActive
            FROM monitoring.Baseline
            WHERE IsActive = 1 AND CreatedAtUtc < @Cutoff
            ORDER BY CreatedAtUtc ASC";

        var results = await ExecuteQueryAsync(
            sql,
            MapBaseline,
            p => AddDateTimeParameter(p, "@Cutoff", cutoff),
            ct);

        _logger.LogDebug("Found {Count} stale baselines older than {MaxAge}", results.Count, maxAge);
        return results;
    }

    /// <inheritdoc />
    public async Task DeleteBaselineAsync(Guid fingerprintId, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE monitoring.Baseline
            SET IsActive = 0
            WHERE FingerprintId = @FingerprintId AND IsActive = 1";

        var affected = await ExecuteNonQueryAsync(
            sql,
            p => AddGuidParameter(p, "@FingerprintId", fingerprintId),
            ct);

        _logger.LogDebug("Deactivated {Count} baselines for fingerprint {FingerprintId}", affected, fingerprintId);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid fingerprintId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM monitoring.Baseline
                WHERE FingerprintId = @FingerprintId AND IsActive = 1
            ) THEN 1 ELSE 0 END";

        var exists = await ExecuteScalarAsync<int>(
            sql,
            p => AddGuidParameter(p, "@FingerprintId", fingerprintId),
            ct);

        return exists == 1;
    }

    /// <inheritdoc />
    public async Task SaveBaselinesAsync(
        IEnumerable<BaselineRecord> baselines,
        CancellationToken ct = default)
    {
        var baselineList = baselines.ToList();
        if (baselineList.Count == 0)
            return;

        _logger.LogDebug("Saving {Count} baselines in batch", baselineList.Count);

        await ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            foreach (var baseline in baselineList)
            {
                // Deactivate existing
                await using var deactivateCmd = connection.CreateCommand();
                deactivateCmd.Transaction = transaction;
                deactivateCmd.CommandText = @"
                    UPDATE monitoring.Baseline 
                    SET IsActive = 0 
                    WHERE FingerprintId = @FingerprintId AND IsActive = 1";
                deactivateCmd.Parameters.Add(new SqlParameter("@FingerprintId", SqlDbType.UniqueIdentifier)
                {
                    Value = baseline.FingerprintId
                });
                await deactivateCmd.ExecuteNonQueryAsync(ct);

                // Insert new baseline
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT INTO monitoring.Baseline (
                        Id, FingerprintId, DatabaseName,
                        BaselineStartUtc, BaselineEndUtc, CreatedAtUtc,
                        SampleCount, TotalExecutions,
                        MedianDurationUs, P95DurationUs, P99DurationUs, AvgDurationUs, DurationStdDev,
                        MedianCpuTimeUs, P95CpuTimeUs, AvgCpuTimeUs,
                        MedianLogicalReads, P95LogicalReads, AvgLogicalReads,
                        ExpectedPlanHash, Notes, IsActive
                    ) VALUES (
                        @Id, @FingerprintId, @DatabaseName,
                        @BaselineStartUtc, @BaselineEndUtc, @CreatedAtUtc,
                        @SampleCount, @TotalExecutions,
                        @MedianDurationUs, @P95DurationUs, @P99DurationUs, @AvgDurationUs, @DurationStdDev,
                        @MedianCpuTimeUs, @P95CpuTimeUs, @AvgCpuTimeUs,
                        @MedianLogicalReads, @P95LogicalReads, @AvgLogicalReads,
                        @ExpectedPlanHash, @Notes, @IsActive
                    )";
                ConfigureBaselineParameters(insertCmd.Parameters, baseline);
                await insertCmd.ExecuteNonQueryAsync(ct);
            }
        }, ct);

        _logger.LogInformation("Saved {Count} baselines in batch", baselineList.Count);
    }

    /// <summary>
    /// Deactivates any existing active baselines for a fingerprint.
    /// </summary>
    private async Task DeactivateExistingBaselinesAsync(Guid fingerprintId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE monitoring.Baseline
            SET IsActive = 0
            WHERE FingerprintId = @FingerprintId AND IsActive = 1";

        await ExecuteNonQueryAsync(
            sql,
            p => AddGuidParameter(p, "@FingerprintId", fingerprintId),
            ct);
    }

    /// <summary>
    /// Configures parameters for inserting a baseline record.
    /// </summary>
    private static void ConfigureBaselineParameters(SqlParameterCollection p, BaselineRecord baseline)
    {
        AddGuidParameter(p, "@Id", baseline.Id);
        AddGuidParameter(p, "@FingerprintId", baseline.FingerprintId);
        AddStringParameter(p, "@DatabaseName", baseline.DatabaseName, 128);
        
        AddDateTimeParameter(p, "@BaselineStartUtc", baseline.BaselineStartUtc);
        AddDateTimeParameter(p, "@BaselineEndUtc", baseline.BaselineEndUtc);
        AddDateTimeParameter(p, "@CreatedAtUtc", baseline.CreatedAtUtc);
        
        AddIntParameter(p, "@SampleCount", baseline.SampleCount);
        AddBigIntParameter(p, "@TotalExecutions", baseline.TotalExecutions);
        
        AddBigIntParameter(p, "@MedianDurationUs", baseline.MedianDurationUs);
        AddBigIntParameter(p, "@P95DurationUs", baseline.P95DurationUs);
        AddBigIntParameter(p, "@P99DurationUs", baseline.P99DurationUs);
        AddBigIntParameter(p, "@AvgDurationUs", baseline.AvgDurationUs);
        AddFloatParameter(p, "@DurationStdDev", baseline.DurationStdDev);
        
        AddBigIntParameter(p, "@MedianCpuTimeUs", baseline.MedianCpuTimeUs);
        AddBigIntParameter(p, "@P95CpuTimeUs", baseline.P95CpuTimeUs);
        AddBigIntParameter(p, "@AvgCpuTimeUs", baseline.AvgCpuTimeUs);
        
        AddBigIntParameter(p, "@MedianLogicalReads", baseline.MedianLogicalReads);
        AddBigIntParameter(p, "@P95LogicalReads", baseline.P95LogicalReads);
        AddBigIntParameter(p, "@AvgLogicalReads", baseline.AvgLogicalReads);
        
        AddBinaryParameter(p, "@ExpectedPlanHash", baseline.ExpectedPlanHash, 32);
        AddStringParameter(p, "@Notes", baseline.Notes, 500);
        AddBoolParameter(p, "@IsActive", baseline.IsActive);
    }

    /// <summary>
    /// Maps a SqlDataReader row to a BaselineRecord.
    /// </summary>
    private static BaselineRecord MapBaseline(SqlDataReader reader)
    {
        return new BaselineRecord
        {
            Id = reader.GetGuid(0),
            FingerprintId = reader.GetGuid(1),
            DatabaseName = reader.GetString(2),
            BaselineStartUtc = reader.GetDateTime(3),
            BaselineEndUtc = reader.GetDateTime(4),
            CreatedAtUtc = reader.GetDateTime(5),
            SampleCount = reader.GetInt32(6),
            TotalExecutions = reader.GetInt64(7),
            MedianDurationUs = reader.GetInt64(8),
            P95DurationUs = reader.GetInt64(9),
            P99DurationUs = reader.GetInt64(10),
            AvgDurationUs = reader.GetInt64(11),
            DurationStdDev = reader.IsDBNull(12) ? 0 : reader.GetDouble(12),
            MedianCpuTimeUs = reader.GetInt64(13),
            P95CpuTimeUs = reader.GetInt64(14),
            AvgCpuTimeUs = reader.GetInt64(15),
            MedianLogicalReads = reader.GetInt64(16),
            P95LogicalReads = reader.GetInt64(17),
            AvgLogicalReads = reader.GetInt64(18),
            ExpectedPlanHash = reader.IsDBNull(19) ? null : GetBytes(reader, 19),
            Notes = reader.IsDBNull(20) ? null : reader.GetString(20),
            IsActive = reader.GetBoolean(21)
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
