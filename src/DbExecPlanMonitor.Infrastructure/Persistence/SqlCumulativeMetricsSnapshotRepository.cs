using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DbExecPlanMonitor.Application.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Infrastructure.Persistence;

/// <summary>
/// SQL Server implementation for persisting cumulative metrics snapshots.
/// Used to compute deltas between collection cycles.
/// </summary>
public sealed class SqlCumulativeMetricsSnapshotRepository : RepositoryBase, ICumulativeMetricsSnapshotRepository
{
    public SqlCumulativeMetricsSnapshotRepository(
        IOptions<MonitoringStorageOptions> options, 
        ILogger<SqlCumulativeMetricsSnapshotRepository> logger)
        : base(options.Value.ConnectionString, logger)
    {
    }

    /// <inheritdoc />
    public async Task<CumulativeMetricsSnapshot?> GetLastSnapshotAsync(
        string instanceName,
        string databaseName,
        Guid fingerprintId,
        byte[]? planHash,
        CancellationToken ct = default)
    {
        // Query handles NULL planHash with IS NULL comparison
        const string sql = """
            SELECT 
                Id,
                InstanceName,
                DatabaseName,
                FingerprintId,
                PlanHash,
                SnapshotTimeUtc,
                ExecutionCount,
                TotalCpuTimeUs,
                TotalDurationUs,
                TotalLogicalReads,
                TotalLogicalWrites,
                TotalPhysicalReads
            FROM monitoring.CumulativeMetricsSnapshot
            WHERE InstanceName = @InstanceName
              AND DatabaseName = @DatabaseName
              AND FingerprintId = @FingerprintId
              AND (
                  (@PlanHash IS NULL AND PlanHash IS NULL) OR
                  (@PlanHash IS NOT NULL AND PlanHash = @PlanHash)
              )
            """;

        return await ExecuteQuerySingleAsync(
            sql,
            MapSnapshot,
            p =>
            {
                AddStringParameter(p, "@InstanceName", instanceName, 256);
                AddStringParameter(p, "@DatabaseName", databaseName, 128);
                AddGuidParameter(p, "@FingerprintId", fingerprintId);
                AddBinaryParameter(p, "@PlanHash", planHash, 32);
            },
            ct);
    }

    /// <inheritdoc />
    public async Task SaveSnapshotAsync(CumulativeMetricsSnapshot snapshot, CancellationToken ct = default)
    {
        // Upsert pattern using MERGE
        const string sql = """
            MERGE monitoring.CumulativeMetricsSnapshot AS target
            USING (SELECT 
                @InstanceName AS InstanceName, 
                @DatabaseName AS DatabaseName, 
                @FingerprintId AS FingerprintId,
                @PlanHash AS PlanHash
            ) AS source
            ON target.InstanceName = source.InstanceName 
               AND target.DatabaseName = source.DatabaseName
               AND target.FingerprintId = source.FingerprintId
               AND (
                   (source.PlanHash IS NULL AND target.PlanHash IS NULL) OR
                   (source.PlanHash IS NOT NULL AND target.PlanHash = source.PlanHash)
               )
            WHEN MATCHED THEN
                UPDATE SET 
                    SnapshotTimeUtc = @SnapshotTimeUtc,
                    ExecutionCount = @ExecutionCount,
                    TotalCpuTimeUs = @TotalCpuTimeUs,
                    TotalDurationUs = @TotalDurationUs,
                    TotalLogicalReads = @TotalLogicalReads,
                    TotalLogicalWrites = @TotalLogicalWrites,
                    TotalPhysicalReads = @TotalPhysicalReads
            WHEN NOT MATCHED THEN
                INSERT (
                    Id, InstanceName, DatabaseName, FingerprintId, PlanHash,
                    SnapshotTimeUtc, ExecutionCount, TotalCpuTimeUs, TotalDurationUs,
                    TotalLogicalReads, TotalLogicalWrites, TotalPhysicalReads
                )
                VALUES (
                    @Id, @InstanceName, @DatabaseName, @FingerprintId, @PlanHash,
                    @SnapshotTimeUtc, @ExecutionCount, @TotalCpuTimeUs, @TotalDurationUs,
                    @TotalLogicalReads, @TotalLogicalWrites, @TotalPhysicalReads
                );
            """;

        await ExecuteNonQueryAsync(
            sql,
            p =>
            {
                AddGuidParameter(p, "@Id", snapshot.Id);
                AddStringParameter(p, "@InstanceName", snapshot.InstanceName, 256);
                AddStringParameter(p, "@DatabaseName", snapshot.DatabaseName, 128);
                AddGuidParameter(p, "@FingerprintId", snapshot.FingerprintId);
                AddBinaryParameter(p, "@PlanHash", snapshot.PlanHash, 32);
                AddDateTimeParameter(p, "@SnapshotTimeUtc", snapshot.SnapshotTimeUtc);
                AddBigIntParameter(p, "@ExecutionCount", snapshot.ExecutionCount);
                AddBigIntParameter(p, "@TotalCpuTimeUs", snapshot.TotalCpuTimeUs);
                AddBigIntParameter(p, "@TotalDurationUs", snapshot.TotalDurationUs);
                AddBigIntParameter(p, "@TotalLogicalReads", snapshot.TotalLogicalReads);
                AddBigIntParameter(p, "@TotalLogicalWrites", snapshot.TotalLogicalWrites);
                AddBigIntParameter(p, "@TotalPhysicalReads", snapshot.TotalPhysicalReads);
            },
            ct);

        Logger.LogDebug(
            "Saved cumulative snapshot for fingerprint {FingerprintId} on {Instance}/{Database}",
            snapshot.FingerprintId, snapshot.InstanceName, snapshot.DatabaseName);
    }

    /// <inheritdoc />
    public async Task<int> PurgeStaleSnapshotsAsync(DateTime olderThan, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM monitoring.CumulativeMetricsSnapshot
            WHERE SnapshotTimeUtc < @OlderThan
            """;

        var deleted = await ExecuteNonQueryAsync(
            sql,
            p => AddDateTimeParameter(p, "@OlderThan", olderThan),
            ct);

        if (deleted > 0)
        {
            Logger.LogInformation("Purged {Count} stale cumulative metrics snapshots older than {OlderThan:u}", 
                deleted, olderThan);
        }

        return deleted;
    }

    private static CumulativeMetricsSnapshot MapSnapshot(SqlDataReader reader)
    {
        return new CumulativeMetricsSnapshot
        {
            Id = reader.GetGuid(reader.GetOrdinal("Id")),
            InstanceName = reader.GetString(reader.GetOrdinal("InstanceName")),
            DatabaseName = reader.GetString(reader.GetOrdinal("DatabaseName")),
            FingerprintId = reader.GetGuid(reader.GetOrdinal("FingerprintId")),
            PlanHash = reader.IsDBNull(reader.GetOrdinal("PlanHash")) 
                ? null 
                : (byte[])reader["PlanHash"],
            SnapshotTimeUtc = reader.GetDateTime(reader.GetOrdinal("SnapshotTimeUtc")),
            ExecutionCount = reader.GetInt64(reader.GetOrdinal("ExecutionCount")),
            TotalCpuTimeUs = reader.GetInt64(reader.GetOrdinal("TotalCpuTimeUs")),
            TotalDurationUs = reader.GetInt64(reader.GetOrdinal("TotalDurationUs")),
            TotalLogicalReads = reader.GetInt64(reader.GetOrdinal("TotalLogicalReads")),
            TotalLogicalWrites = reader.GetInt64(reader.GetOrdinal("TotalLogicalWrites")),
            TotalPhysicalReads = reader.GetInt64(reader.GetOrdinal("TotalPhysicalReads"))
        };
    }
}
