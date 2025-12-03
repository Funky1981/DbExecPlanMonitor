using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Infrastructure.Persistence;

namespace DbExecPlanMonitor.Infrastructure.Logging;

/// <summary>
/// SQL Server implementation of remediation audit repository.
/// </summary>
/// <remarks>
/// Stores audit records in monitoring.RemediationAudit table.
/// This table should be created via migrations or setup scripts.
/// </remarks>
public sealed class SqlRemediationAuditRepository : IRemediationAuditRepository
{
    private readonly ILogger<SqlRemediationAuditRepository> _logger;
    private readonly MonitoringStorageOptions _options;

    public SqlRemediationAuditRepository(
        IOptions<MonitoringStorageOptions> options,
        ILogger<SqlRemediationAuditRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SaveAsync(
        RemediationAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO monitoring.RemediationAudit (
                Id, Timestamp, InstanceName, DatabaseName, QueryFingerprint, QueryHash,
                RegressionEventId, RemediationSuggestionId, RemediationType,
                SqlStatement, IsDryRun, Success, ErrorMessage, SqlErrorNumber,
                DurationMs, InitiatedBy, Notes, MachineName, ServiceVersion
            ) VALUES (
                @Id, @Timestamp, @InstanceName, @DatabaseName, @QueryFingerprint, @QueryHash,
                @RegressionEventId, @RemediationSuggestionId, @RemediationType,
                @SqlStatement, @IsDryRun, @Success, @ErrorMessage, @SqlErrorNumber,
                @DurationMs, @InitiatedBy, @Notes, @MachineName, @ServiceVersion
            )";

        try
        {
            await using var connection = new SqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            command.Parameters.AddWithValue("@Id", record.Id);
            command.Parameters.AddWithValue("@Timestamp", record.Timestamp);
            command.Parameters.AddWithValue("@InstanceName", record.InstanceName);
            command.Parameters.AddWithValue("@DatabaseName", record.DatabaseName);
            command.Parameters.AddWithValue("@QueryFingerprint", record.QueryFingerprint);
            command.Parameters.AddWithValue("@QueryHash", (object?)record.QueryHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@RegressionEventId", (object?)record.RegressionEventId ?? DBNull.Value);
            command.Parameters.AddWithValue("@RemediationSuggestionId", (object?)record.RemediationSuggestionId ?? DBNull.Value);
            command.Parameters.AddWithValue("@RemediationType", record.RemediationType);
            command.Parameters.AddWithValue("@SqlStatement", record.SqlStatement);
            command.Parameters.AddWithValue("@IsDryRun", record.IsDryRun);
            command.Parameters.AddWithValue("@Success", record.Success);
            command.Parameters.AddWithValue("@ErrorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("@SqlErrorNumber", (object?)record.SqlErrorNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@DurationMs", record.Duration.HasValue ? (object)record.Duration.Value.TotalMilliseconds : DBNull.Value);
            command.Parameters.AddWithValue("@InitiatedBy", (object?)record.InitiatedBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Notes", (object?)record.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("@MachineName", (object?)record.MachineName ?? DBNull.Value);
            command.Parameters.AddWithValue("@ServiceVersion", (object?)record.ServiceVersion ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug(
                "Saved remediation audit record {Id} for {InstanceName}/{DatabaseName}",
                record.Id,
                record.InstanceName,
                record.DatabaseName);
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Failed to save remediation audit record {Id}",
                record.Id);
            throw;
        }
    }

    public async Task<IReadOnlyList<RemediationAuditRecord>> GetByInstanceAsync(
        string instanceName,
        string databaseName,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT Id, Timestamp, InstanceName, DatabaseName, QueryFingerprint, QueryHash,
                   RegressionEventId, RemediationSuggestionId, RemediationType,
                   SqlStatement, IsDryRun, Success, ErrorMessage, SqlErrorNumber,
                   DurationMs, InitiatedBy, Notes, MachineName, ServiceVersion
            FROM monitoring.RemediationAudit
            WHERE InstanceName = @InstanceName
              AND DatabaseName = @DatabaseName
              AND Timestamp >= @From
              AND Timestamp <= @To
            ORDER BY Timestamp DESC";

        return await ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["@InstanceName"] = instanceName,
            ["@DatabaseName"] = databaseName,
            ["@From"] = from,
            ["@To"] = to
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<RemediationAuditRecord>> GetByQueryFingerprintAsync(
        string queryFingerprint,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP (@Limit)
                   Id, Timestamp, InstanceName, DatabaseName, QueryFingerprint, QueryHash,
                   RegressionEventId, RemediationSuggestionId, RemediationType,
                   SqlStatement, IsDryRun, Success, ErrorMessage, SqlErrorNumber,
                   DurationMs, InitiatedBy, Notes, MachineName, ServiceVersion
            FROM monitoring.RemediationAudit
            WHERE QueryFingerprint = @QueryFingerprint
            ORDER BY Timestamp DESC";

        return await ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["@QueryFingerprint"] = queryFingerprint,
            ["@Limit"] = limit
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<RemediationAuditRecord>> GetRecentFailuresAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT Id, Timestamp, InstanceName, DatabaseName, QueryFingerprint, QueryHash,
                   RegressionEventId, RemediationSuggestionId, RemediationType,
                   SqlStatement, IsDryRun, Success, ErrorMessage, SqlErrorNumber,
                   DurationMs, InitiatedBy, Notes, MachineName, ServiceVersion
            FROM monitoring.RemediationAudit
            WHERE Success = 0
              AND IsDryRun = 0
              AND Timestamp >= @Since
            ORDER BY Timestamp DESC";

        return await ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["@Since"] = since
        }, cancellationToken);
    }

    public async Task<RemediationAuditSummary> GetSummaryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                COUNT(*) AS TotalAttempts,
                SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS SuccessCount,
                SUM(CASE WHEN Success = 0 AND IsDryRun = 0 THEN 1 ELSE 0 END) AS FailureCount,
                SUM(CASE WHEN IsDryRun = 1 THEN 1 ELSE 0 END) AS DryRunCount
            FROM monitoring.RemediationAudit
            WHERE Timestamp >= @From AND Timestamp <= @To;

            SELECT RemediationType, COUNT(*) AS Count
            FROM monitoring.RemediationAudit
            WHERE Timestamp >= @From AND Timestamp <= @To
            GROUP BY RemediationType;

            SELECT InstanceName, COUNT(*) AS Count
            FROM monitoring.RemediationAudit
            WHERE Timestamp >= @From AND Timestamp <= @To
            GROUP BY InstanceName;";

        try
        {
            await using var connection = new SqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@From", from);
            command.Parameters.AddWithValue("@To", to);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            int totalAttempts = 0, successCount = 0, failureCount = 0, dryRunCount = 0;
            var byType = new Dictionary<string, int>();
            var byInstance = new Dictionary<string, int>();

            // First result set: totals
            if (await reader.ReadAsync(cancellationToken))
            {
                totalAttempts = reader.GetInt32(0);
                successCount = reader.GetInt32(1);
                failureCount = reader.GetInt32(2);
                dryRunCount = reader.GetInt32(3);
            }

            // Second result set: by type
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    byType[reader.GetString(0)] = reader.GetInt32(1);
                }
            }

            // Third result set: by instance
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    byInstance[reader.GetString(0)] = reader.GetInt32(1);
                }
            }

            return new RemediationAuditSummary
            {
                TotalAttempts = totalAttempts,
                SuccessCount = successCount,
                FailureCount = failureCount,
                DryRunCount = dryRunCount,
                ByType = byType,
                ByInstance = byInstance
            };
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to get remediation audit summary");
            throw;
        }
    }

    private async Task<IReadOnlyList<RemediationAuditRecord>> ExecuteQueryAsync(
        string sql,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        var results = new List<RemediationAuditRecord>();

        try
        {
            await using var connection = new SqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(MapFromReader(reader));
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to execute remediation audit query");
            throw;
        }

        return results;
    }

    private static RemediationAuditRecord MapFromReader(SqlDataReader reader)
    {
        return new RemediationAuditRecord
        {
            Id = reader.GetGuid(reader.GetOrdinal("Id")),
            Timestamp = reader.GetDateTimeOffset(reader.GetOrdinal("Timestamp")),
            InstanceName = reader.GetString(reader.GetOrdinal("InstanceName")),
            DatabaseName = reader.GetString(reader.GetOrdinal("DatabaseName")),
            QueryFingerprint = reader.GetString(reader.GetOrdinal("QueryFingerprint")),
            QueryHash = reader.IsDBNull(reader.GetOrdinal("QueryHash"))
                ? null
                : reader.GetString(reader.GetOrdinal("QueryHash")),
            RegressionEventId = reader.IsDBNull(reader.GetOrdinal("RegressionEventId"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("RegressionEventId")),
            RemediationSuggestionId = reader.IsDBNull(reader.GetOrdinal("RemediationSuggestionId"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("RemediationSuggestionId")),
            RemediationType = reader.GetString(reader.GetOrdinal("RemediationType")),
            SqlStatement = reader.GetString(reader.GetOrdinal("SqlStatement")),
            IsDryRun = reader.GetBoolean(reader.GetOrdinal("IsDryRun")),
            Success = reader.GetBoolean(reader.GetOrdinal("Success")),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage"))
                ? null
                : reader.GetString(reader.GetOrdinal("ErrorMessage")),
            SqlErrorNumber = reader.IsDBNull(reader.GetOrdinal("SqlErrorNumber"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("SqlErrorNumber")),
            Duration = reader.IsDBNull(reader.GetOrdinal("DurationMs"))
                ? null
                : TimeSpan.FromMilliseconds(reader.GetDouble(reader.GetOrdinal("DurationMs"))),
            InitiatedBy = reader.IsDBNull(reader.GetOrdinal("InitiatedBy"))
                ? null
                : reader.GetString(reader.GetOrdinal("InitiatedBy")),
            Notes = reader.IsDBNull(reader.GetOrdinal("Notes"))
                ? null
                : reader.GetString(reader.GetOrdinal("Notes")),
            MachineName = reader.IsDBNull(reader.GetOrdinal("MachineName"))
                ? null
                : reader.GetString(reader.GetOrdinal("MachineName")),
            ServiceVersion = reader.IsDBNull(reader.GetOrdinal("ServiceVersion"))
                ? null
                : reader.GetString(reader.GetOrdinal("ServiceVersion"))
        };
    }
}
