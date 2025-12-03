using System.Data;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Infrastructure.Persistence;

/// <summary>
/// SQL Server implementation of the plan metrics repository.
/// Handles storage and retrieval of query performance samples.
/// </summary>
public sealed class SqlPlanMetricsRepository : RepositoryBase, IPlanMetricsRepository
{
    private readonly ILogger<SqlPlanMetricsRepository> _logger;
    private readonly MonitoringStorageOptions _options;

    public SqlPlanMetricsRepository(
        IOptions<MonitoringStorageOptions> options,
        ILogger<SqlPlanMetricsRepository> logger)
        : base(options.Value.ConnectionString, logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveSamplesAsync(
        string instanceName,
        IEnumerable<PlanMetricSampleRecord> samples,
        CancellationToken ct = default)
    {
        var sampleList = samples.ToList();
        if (sampleList.Count == 0)
            return;

        _logger.LogDebug(
            "Saving {Count} metric samples for instance {Instance}",
            sampleList.Count,
            instanceName);

        const string insertSql = @"
            INSERT INTO monitoring.PlanMetricSample (
                Id, FingerprintId, InstanceName, DatabaseName, SampledAtUtc,
                PlanHash, QueryStoreQueryId, QueryStorePlanId,
                ExecutionCount, ExecutionCountDelta,
                TotalCpuTimeUs, AvgCpuTimeUs, MinCpuTimeUs, MaxCpuTimeUs,
                TotalDurationUs, AvgDurationUs, MinDurationUs, MaxDurationUs,
                TotalLogicalReads, AvgLogicalReads, TotalLogicalWrites, TotalPhysicalReads,
                AvgMemoryGrantKb, MaxMemoryGrantKb, AvgSpillsKb
            ) VALUES (
                @Id, @FingerprintId, @InstanceName, @DatabaseName, @SampledAtUtc,
                @PlanHash, @QueryStoreQueryId, @QueryStorePlanId,
                @ExecutionCount, @ExecutionCountDelta,
                @TotalCpuTimeUs, @AvgCpuTimeUs, @MinCpuTimeUs, @MaxCpuTimeUs,
                @TotalDurationUs, @AvgDurationUs, @MinDurationUs, @MaxDurationUs,
                @TotalLogicalReads, @AvgLogicalReads, @TotalLogicalWrites, @TotalPhysicalReads,
                @AvgMemoryGrantKb, @MaxMemoryGrantKb, @AvgSpillsKb
            )";

        await ExecuteBatchInsertAsync(sampleList, insertSql, ConfigureSampleParameters, ct);

        _logger.LogInformation(
            "Saved {Count} metric samples for instance {Instance}",
            sampleList.Count,
            instanceName);
    }

    /// <inheritdoc />
    public async Task SaveSampleAsync(
        string instanceName,
        PlanMetricSampleRecord sample,
        CancellationToken ct = default)
    {
        await SaveSamplesAsync(instanceName, new[] { sample }, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlanMetricSampleRecord>> GetSamplesForFingerprintAsync(
        Guid fingerprintId,
        TimeWindow window,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, SampledAtUtc,
                PlanHash, QueryStoreQueryId, QueryStorePlanId,
                ExecutionCount, ExecutionCountDelta,
                TotalCpuTimeUs, AvgCpuTimeUs, MinCpuTimeUs, MaxCpuTimeUs,
                TotalDurationUs, AvgDurationUs, MinDurationUs, MaxDurationUs,
                TotalLogicalReads, AvgLogicalReads, TotalLogicalWrites, TotalPhysicalReads,
                AvgMemoryGrantKb, MaxMemoryGrantKb, AvgSpillsKb
            FROM monitoring.PlanMetricSample
            WHERE FingerprintId = @FingerprintId
              AND SampledAtUtc BETWEEN @StartUtc AND @EndUtc
            ORDER BY SampledAtUtc DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapSample,
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
    public async Task<IReadOnlyList<PlanMetricSampleRecord>> GetSamplesForInstanceAsync(
        string instanceName,
        TimeWindow window,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                Id, FingerprintId, InstanceName, DatabaseName, SampledAtUtc,
                PlanHash, QueryStoreQueryId, QueryStorePlanId,
                ExecutionCount, ExecutionCountDelta,
                TotalCpuTimeUs, AvgCpuTimeUs, MinCpuTimeUs, MaxCpuTimeUs,
                TotalDurationUs, AvgDurationUs, MinDurationUs, MaxDurationUs,
                TotalLogicalReads, AvgLogicalReads, TotalLogicalWrites, TotalPhysicalReads,
                AvgMemoryGrantKb, MaxMemoryGrantKb, AvgSpillsKb
            FROM monitoring.PlanMetricSample
            WHERE InstanceName = @InstanceName
              AND SampledAtUtc BETWEEN @StartUtc AND @EndUtc
            ORDER BY SampledAtUtc DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapSample,
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
    public async Task<IReadOnlyList<PlanMetricSampleRecord>> GetLatestSamplesPerFingerprintAsync(
        string databaseName,
        int topN = 100,
        CancellationToken ct = default)
    {
        // Uses ROW_NUMBER to get the most recent sample per fingerprint
        const string sql = @"
            WITH RankedSamples AS (
                SELECT 
                    Id, FingerprintId, InstanceName, DatabaseName, SampledAtUtc,
                    PlanHash, QueryStoreQueryId, QueryStorePlanId,
                    ExecutionCount, ExecutionCountDelta,
                    TotalCpuTimeUs, AvgCpuTimeUs, MinCpuTimeUs, MaxCpuTimeUs,
                    TotalDurationUs, AvgDurationUs, MinDurationUs, MaxDurationUs,
                    TotalLogicalReads, AvgLogicalReads, TotalLogicalWrites, TotalPhysicalReads,
                    AvgMemoryGrantKb, MaxMemoryGrantKb, AvgSpillsKb,
                    ROW_NUMBER() OVER (PARTITION BY FingerprintId ORDER BY SampledAtUtc DESC) AS rn
                FROM monitoring.PlanMetricSample
                WHERE DatabaseName = @DatabaseName
            )
            SELECT TOP (@TopN)
                Id, FingerprintId, InstanceName, DatabaseName, SampledAtUtc,
                PlanHash, QueryStoreQueryId, QueryStorePlanId,
                ExecutionCount, ExecutionCountDelta,
                TotalCpuTimeUs, AvgCpuTimeUs, MinCpuTimeUs, MaxCpuTimeUs,
                TotalDurationUs, AvgDurationUs, MinDurationUs, MaxDurationUs,
                TotalLogicalReads, AvgLogicalReads, TotalLogicalWrites, TotalPhysicalReads,
                AvgMemoryGrantKb, MaxMemoryGrantKb, AvgSpillsKb
            FROM RankedSamples
            WHERE rn = 1
            ORDER BY TotalCpuTimeUs DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapSample,
            p =>
            {
                AddStringParameter(p, "@DatabaseName", databaseName, 128);
                AddIntParameter(p, "@TopN", topN);
            },
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlanMetricSampleRecord>> GetLatestSamplesPerFingerprintAsync(
        string databaseName,
        TimeWindow window,
        int topN = 100,
        CancellationToken ct = default)
    {
        // Uses ROW_NUMBER to get the most recent sample per fingerprint within the time window
        const string sql = @"
            WITH RankedSamples AS (
                SELECT 
                    Id, FingerprintId, InstanceName, DatabaseName, SampledAtUtc,
                    PlanHash, QueryStoreQueryId, QueryStorePlanId,
                    ExecutionCount, ExecutionCountDelta,
                    TotalCpuTimeUs, AvgCpuTimeUs, MinCpuTimeUs, MaxCpuTimeUs,
                    TotalDurationUs, AvgDurationUs, MinDurationUs, MaxDurationUs,
                    TotalLogicalReads, AvgLogicalReads, TotalLogicalWrites, TotalPhysicalReads,
                    AvgMemoryGrantKb, MaxMemoryGrantKb, AvgSpillsKb,
                    ROW_NUMBER() OVER (PARTITION BY FingerprintId ORDER BY SampledAtUtc DESC) AS rn
                FROM monitoring.PlanMetricSample
                WHERE DatabaseName = @DatabaseName
                  AND SampledAtUtc BETWEEN @StartUtc AND @EndUtc
            )
            SELECT TOP (@TopN)
                Id, FingerprintId, InstanceName, DatabaseName, SampledAtUtc,
                PlanHash, QueryStoreQueryId, QueryStorePlanId,
                ExecutionCount, ExecutionCountDelta,
                TotalCpuTimeUs, AvgCpuTimeUs, MinCpuTimeUs, MaxCpuTimeUs,
                TotalDurationUs, AvgDurationUs, MinDurationUs, MaxDurationUs,
                TotalLogicalReads, AvgLogicalReads, TotalLogicalWrites, TotalPhysicalReads,
                AvgMemoryGrantKb, MaxMemoryGrantKb, AvgSpillsKb
            FROM RankedSamples
            WHERE rn = 1
            ORDER BY TotalCpuTimeUs DESC";

        var results = await ExecuteQueryAsync(
            sql,
            MapSample,
            p =>
            {
                AddStringParameter(p, "@DatabaseName", databaseName, 128);
                AddDateTimeParameter(p, "@StartUtc", window.StartUtc);
                AddDateTimeParameter(p, "@EndUtc", window.EndUtc);
                AddIntParameter(p, "@TopN", topN);
            },
            ct);

        return results;
    }

    /// <inheritdoc />
    public async Task<PlanMetricSampleRecord?> GetLatestSampleForFingerprintAsync(
        Guid fingerprintId,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (1)
                Id, FingerprintId, InstanceName, DatabaseName, SampledAtUtc,
                PlanHash, QueryStoreQueryId, QueryStorePlanId,
                ExecutionCount, ExecutionCountDelta,
                TotalCpuTimeUs, AvgCpuTimeUs, MinCpuTimeUs, MaxCpuTimeUs,
                TotalDurationUs, AvgDurationUs, MinDurationUs, MaxDurationUs,
                TotalLogicalReads, AvgLogicalReads, TotalLogicalWrites, TotalPhysicalReads,
                AvgMemoryGrantKb, MaxMemoryGrantKb, AvgSpillsKb
            FROM monitoring.PlanMetricSample
            WHERE FingerprintId = @FingerprintId
            ORDER BY SampledAtUtc DESC";

        return await ExecuteQuerySingleAsync(
            sql,
            MapSample,
            p => AddGuidParameter(p, "@FingerprintId", fingerprintId),
            ct);
    }

    /// <inheritdoc />
    public async Task<AggregatedMetrics?> GetAggregatedMetricsAsync(
        Guid fingerprintId,
        TimeWindow window,
        CancellationToken ct = default)
    {
        // Calculate aggregates including percentiles using PERCENTILE_CONT
        const string sql = @"
            SELECT 
                @FingerprintId AS FingerprintId,
                COUNT(*) AS SampleCount,
                SUM(ExecutionCount) AS TotalExecutions,
                MIN(AvgDurationUs) AS MinDurationUs,
                MAX(AvgDurationUs) AS MaxDurationUs,
                AVG(AvgDurationUs) AS AvgDurationUs,
                PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY AvgDurationUs) OVER () AS P50DurationUs,
                PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY AvgDurationUs) OVER () AS P95DurationUs,
                PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY AvgDurationUs) OVER () AS P99DurationUs,
                MIN(AvgCpuTimeUs) AS MinCpuTimeUs,
                MAX(AvgCpuTimeUs) AS MaxCpuTimeUs,
                AVG(AvgCpuTimeUs) AS AvgCpuTimeUs,
                PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY AvgCpuTimeUs) OVER () AS P95CpuTimeUs,
                MIN(AvgLogicalReads) AS MinLogicalReads,
                MAX(AvgLogicalReads) AS MaxLogicalReads,
                AVG(AvgLogicalReads) AS AvgLogicalReads,
                STDEV(AvgDurationUs) AS DurationStdDev,
                AVG(TotalLogicalWrites / NULLIF(ExecutionCount, 0)) AS AvgLogicalWrites,
                MAX(TotalLogicalWrites / NULLIF(ExecutionCount, 0)) AS MaxLogicalWrites,
                AVG(TotalPhysicalReads / NULLIF(ExecutionCount, 0)) AS AvgPhysicalReads,
                AVG(AvgSpillsKb) AS AvgSpillsKb,
                MAX(AvgSpillsKb) AS MaxSpillsKb
            FROM monitoring.PlanMetricSample
            WHERE FingerprintId = @FingerprintId
              AND SampledAtUtc BETWEEN @StartUtc AND @EndUtc";

        return await ExecuteQuerySingleAsync(
            sql,
            reader => MapAggregatedMetrics(reader, window),
            p =>
            {
                AddGuidParameter(p, "@FingerprintId", fingerprintId);
                AddDateTimeParameter(p, "@StartUtc", window.StartUtc);
                AddDateTimeParameter(p, "@EndUtc", window.EndUtc);
            },
            ct);
    }

    /// <inheritdoc />
    public async Task<int> PurgeSamplesOlderThanAsync(DateTime olderThan, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Purging metric samples older than {OlderThan}",
            olderThan);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        
        command.CommandText = "monitoring.usp_PurgeSamples";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = 600; // 10 minutes for large purges

        command.Parameters.Add(new SqlParameter("@OlderThan", SqlDbType.DateTime2) { Value = olderThan });
        command.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = 10000 });
        
        var deletedCountParam = new SqlParameter("@DeletedCount", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(deletedCountParam);

        await command.ExecuteNonQueryAsync(ct);

        var deletedCount = (int)deletedCountParam.Value;
        
        _logger.LogInformation("Purged {Count} old metric samples", deletedCount);
        return deletedCount;
    }

    /// <summary>
    /// Configures parameters for inserting a sample record.
    /// </summary>
    private static void ConfigureSampleParameters(SqlParameterCollection p, PlanMetricSampleRecord sample)
    {
        AddGuidParameter(p, "@Id", sample.Id);
        AddGuidParameter(p, "@FingerprintId", sample.FingerprintId);
        AddStringParameter(p, "@InstanceName", sample.InstanceName, 256);
        AddStringParameter(p, "@DatabaseName", sample.DatabaseName, 128);
        AddDateTimeParameter(p, "@SampledAtUtc", sample.SampledAtUtc);
        
        AddBinaryParameter(p, "@PlanHash", sample.PlanHash, 32);
        AddBigIntParameter(p, "@QueryStoreQueryId", sample.QueryStoreQueryId);
        AddBigIntParameter(p, "@QueryStorePlanId", sample.QueryStorePlanId);
        
        AddBigIntParameter(p, "@ExecutionCount", sample.ExecutionCount);
        AddBigIntParameter(p, "@ExecutionCountDelta", sample.ExecutionCountDelta);
        
        AddBigIntParameter(p, "@TotalCpuTimeUs", sample.TotalCpuTimeUs);
        AddBigIntParameter(p, "@AvgCpuTimeUs", sample.AvgCpuTimeUs);
        AddBigIntParameter(p, "@MinCpuTimeUs", sample.MinCpuTimeUs);
        AddBigIntParameter(p, "@MaxCpuTimeUs", sample.MaxCpuTimeUs);
        
        AddBigIntParameter(p, "@TotalDurationUs", sample.TotalDurationUs);
        AddBigIntParameter(p, "@AvgDurationUs", sample.AvgDurationUs);
        AddBigIntParameter(p, "@MinDurationUs", sample.MinDurationUs);
        AddBigIntParameter(p, "@MaxDurationUs", sample.MaxDurationUs);
        
        AddBigIntParameter(p, "@TotalLogicalReads", sample.TotalLogicalReads);
        AddBigIntParameter(p, "@AvgLogicalReads", sample.AvgLogicalReads);
        AddBigIntParameter(p, "@TotalLogicalWrites", sample.TotalLogicalWrites);
        AddBigIntParameter(p, "@TotalPhysicalReads", sample.TotalPhysicalReads);
        
        AddBigIntParameter(p, "@AvgMemoryGrantKb", sample.AvgMemoryGrantKb);
        AddBigIntParameter(p, "@MaxMemoryGrantKb", sample.MaxMemoryGrantKb);
        AddBigIntParameter(p, "@AvgSpillsKb", sample.AvgSpillsKb);
    }

    /// <summary>
    /// Maps a SqlDataReader row to a PlanMetricSampleRecord.
    /// </summary>
    private static PlanMetricSampleRecord MapSample(SqlDataReader reader)
    {
        return new PlanMetricSampleRecord
        {
            Id = reader.GetGuid(0),
            FingerprintId = reader.GetGuid(1),
            InstanceName = reader.GetString(2),
            DatabaseName = reader.GetString(3),
            SampledAtUtc = reader.GetDateTime(4),
            PlanHash = reader.IsDBNull(5) ? null : GetBytes(reader, 5),
            QueryStoreQueryId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
            QueryStorePlanId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            ExecutionCount = reader.GetInt64(8),
            ExecutionCountDelta = reader.GetInt64(9),
            TotalCpuTimeUs = reader.GetInt64(10),
            AvgCpuTimeUs = reader.GetInt64(11),
            MinCpuTimeUs = reader.IsDBNull(12) ? null : reader.GetInt64(12),
            MaxCpuTimeUs = reader.IsDBNull(13) ? null : reader.GetInt64(13),
            TotalDurationUs = reader.GetInt64(14),
            AvgDurationUs = reader.GetInt64(15),
            MinDurationUs = reader.IsDBNull(16) ? null : reader.GetInt64(16),
            MaxDurationUs = reader.IsDBNull(17) ? null : reader.GetInt64(17),
            TotalLogicalReads = reader.GetInt64(18),
            AvgLogicalReads = reader.GetInt64(19),
            TotalLogicalWrites = reader.GetInt64(20),
            TotalPhysicalReads = reader.GetInt64(21),
            AvgMemoryGrantKb = reader.IsDBNull(22) ? null : reader.GetInt64(22),
            MaxMemoryGrantKb = reader.IsDBNull(23) ? null : reader.GetInt64(23),
            AvgSpillsKb = reader.IsDBNull(24) ? null : reader.GetInt64(24)
        };
    }

    /// <summary>
    /// Maps aggregated metrics from a SqlDataReader.
    /// </summary>
    private static AggregatedMetrics MapAggregatedMetrics(SqlDataReader reader, TimeWindow window)
    {
        return new AggregatedMetrics
        {
            FingerprintId = reader.GetGuid(0),
            SampleCount = reader.GetInt32(1),
            TotalExecutions = reader.GetInt64(2),
            MinDurationUs = reader.GetInt64(3),
            MaxDurationUs = reader.GetInt64(4),
            AvgDurationUs = reader.GetInt64(5),
            P50DurationUs = reader.IsDBNull(6) ? null : (long)reader.GetDouble(6),
            P95DurationUs = reader.IsDBNull(7) ? null : (long)reader.GetDouble(7),
            P99DurationUs = reader.IsDBNull(8) ? null : (long)reader.GetDouble(8),
            MinCpuTimeUs = reader.GetInt64(9),
            MaxCpuTimeUs = reader.GetInt64(10),
            AvgCpuTimeUs = reader.GetInt64(11),
            P95CpuTimeUs = reader.IsDBNull(12) ? null : (long)reader.GetDouble(12),
            MinLogicalReads = reader.GetInt64(13),
            MaxLogicalReads = reader.GetInt64(14),
            AvgLogicalReads = reader.GetInt64(15),
            DurationStdDev = reader.IsDBNull(16) ? null : reader.GetDouble(16),
            AvgLogicalWrites = reader.IsDBNull(17) ? 0 : reader.GetInt64(17),
            MaxLogicalWrites = reader.IsDBNull(18) ? null : reader.GetInt64(18),
            AvgPhysicalReads = reader.IsDBNull(19) ? 0 : reader.GetInt64(19),
            AvgSpillsKb = reader.IsDBNull(20) ? null : reader.GetInt64(20),
            MaxSpillsKb = reader.IsDBNull(21) ? null : reader.GetInt64(21),
            Window = window
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
