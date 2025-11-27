using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using DbExecPlanMonitor.Application.Interfaces;

namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer;

/// <summary>
/// Retrieves execution plan XML from SQL Server DMVs and Query Store.
/// </summary>
/// <remarks>
/// This provider fetches the actual execution plan XML which contains:
/// - Operator tree (scans, seeks, joins, sorts, etc.)
/// - Estimated and actual row counts
/// - Cost information
/// - Warnings (implicit conversions, missing indexes, etc.)
/// - Parameter information
/// </remarks>
public class DmvPlanDetailsProvider : SqlDataReaderBase, IPlanDetailsProvider
{
    private readonly ILogger<DmvPlanDetailsProvider> _logger;

    // Maps databaseId (Guid) to instance/database config
    private readonly Dictionary<Guid, (string instanceName, string databaseName)> _databaseRegistry = new();

    public DmvPlanDetailsProvider(
        ISqlConnectionFactory connectionFactory,
        ILogger<DmvPlanDetailsProvider> logger)
        : base(connectionFactory, logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a database for access.
    /// </summary>
    public void RegisterDatabase(Guid databaseId, string instanceName, string databaseName)
    {
        _databaseRegistry[databaseId] = (instanceName, databaseName);
    }

    /// <summary>
    /// Gets the execution plan by plan handle from the plan cache.
    /// </summary>
    public async Task<PlanDetailsResult?> GetPlanByHandleAsync(
        Guid databaseId,
        byte[] planHandle,
        CancellationToken cancellationToken = default)
    {
        if (!_databaseRegistry.TryGetValue(databaseId, out var dbInfo))
        {
            throw new ArgumentException($"Database {databaseId} is not registered", nameof(databaseId));
        }

        var sql = @"
            SELECT query_plan AS PlanXml
            FROM sys.dm_exec_query_plan(@PlanHandle)
            WHERE query_plan IS NOT NULL";

        var parameters = new Dictionary<string, object>
        {
            { "@PlanHandle", planHandle }
        };

        var results = await ExecuteQueryAsync(
            dbInfo.instanceName,
            dbInfo.databaseName,
            sql,
            parameters,
            reader =>
            {
                var planXml = GetStringOrNull(reader, "PlanXml");
                return planXml;
            },
            commandTimeout: 30,
            cancellationToken);

        var xml = results.FirstOrDefault();
        if (string.IsNullOrEmpty(xml))
        {
            _logger.LogDebug("Plan not found for handle (may have been evicted from cache)");
            return null;
        }

        return ParsePlanXml(xml, null, false);
    }

    /// <summary>
    /// Gets execution plan from Query Store by plan ID.
    /// </summary>
    public async Task<PlanDetailsResult?> GetPlanFromQueryStoreAsync(
        Guid databaseId,
        long queryStorePlanId,
        CancellationToken cancellationToken = default)
    {
        if (!_databaseRegistry.TryGetValue(databaseId, out var dbInfo))
        {
            throw new ArgumentException($"Database {databaseId} is not registered", nameof(databaseId));
        }

        var sql = @"
            SELECT 
                p.query_plan_hash AS QueryPlanHash,
                TRY_CAST(p.query_plan AS NVARCHAR(MAX)) AS PlanXml,
                p.is_forced_plan AS IsForced,
                p.initial_compile_start_time AS CreatedAt,
                p.last_execution_time AS LastUsedAt
            FROM sys.query_store_plan p
            WHERE p.plan_id = @PlanId";

        var parameters = new Dictionary<string, object>
        {
            { "@PlanId", queryStorePlanId }
        };

        var results = await ExecuteQueryAsync(
            dbInfo.instanceName,
            dbInfo.databaseName,
            sql,
            parameters,
            reader =>
            {
                var planXml = GetStringOrNull(reader, "PlanXml");
                var planHash = GetRequiredInt64(reader, "QueryPlanHash");
                var isForced = GetBooleanOrNull(reader, "IsForced") ?? false;
                var createdAt = GetDateTimeOrNull(reader, "CreatedAt");
                var lastUsed = GetDateTimeOrNull(reader, "LastUsedAt");

                return (planXml, planHash, isForced, createdAt, lastUsed);
            },
            commandTimeout: 30,
            cancellationToken);

        var result = results.FirstOrDefault();
        if (result.planXml == null)
        {
            _logger.LogDebug("Plan {PlanId} not found in Query Store", queryStorePlanId);
            return null;
        }

        var planDetails = ParsePlanXml(result.planXml, queryStorePlanId, result.isForced);
        if (planDetails != null)
        {
            // Enhance with Query Store metadata
            planDetails = new PlanDetailsResult
            {
                PlanHash = $"0x{result.planHash:X16}",
                PlanXml = planDetails.PlanXml,
                EstimatedCost = planDetails.EstimatedCost,
                EstimatedRows = planDetails.EstimatedRows,
                IsParallel = planDetails.IsParallel,
                DegreeOfParallelism = planDetails.DegreeOfParallelism,
                QueryStorePlanId = queryStorePlanId,
                IsForced = result.isForced,
                CreatedAtUtc = result.createdAt,
                LastUsedAtUtc = result.lastUsed
            };
        }

        return planDetails;
    }

    /// <summary>
    /// Gets all cached plans for a query hash.
    /// </summary>
    public async Task<IReadOnlyList<PlanDetailsResult>> GetPlansForQueryAsync(
        Guid databaseId,
        string queryHash,
        CancellationToken cancellationToken = default)
    {
        if (!_databaseRegistry.TryGetValue(databaseId, out var dbInfo))
        {
            throw new ArgumentException($"Database {databaseId} is not registered", nameof(databaseId));
        }

        // First try Query Store (has historical plans)
        var queryStorePlans = await GetPlansFromQueryStoreAsync(
            dbInfo.instanceName, dbInfo.databaseName, queryHash, cancellationToken);

        if (queryStorePlans.Count > 0)
        {
            return queryStorePlans;
        }

        // Fall back to DMV (only currently cached plans)
        return await GetPlansFromDmvAsync(
            dbInfo.instanceName, dbInfo.databaseName, queryHash, cancellationToken);
    }

    /// <summary>
    /// Gets plans from Query Store for a query hash.
    /// </summary>
    private async Task<IReadOnlyList<PlanDetailsResult>> GetPlansFromQueryStoreAsync(
        string instanceName,
        string databaseName,
        string queryHash,
        CancellationToken cancellationToken)
    {
        if (!TryParseQueryHash(queryHash, out var hashValue))
        {
            return Array.Empty<PlanDetailsResult>();
        }

        var sql = @"
            SELECT 
                p.plan_id AS PlanId,
                p.query_plan_hash AS QueryPlanHash,
                TRY_CAST(p.query_plan AS NVARCHAR(MAX)) AS PlanXml,
                p.is_forced_plan AS IsForced,
                p.initial_compile_start_time AS CreatedAt,
                p.last_execution_time AS LastUsedAt
            FROM sys.query_store_query q
            JOIN sys.query_store_plan p ON q.query_id = p.query_id
            WHERE q.query_hash = @QueryHash
            ORDER BY p.last_execution_time DESC";

        var parameters = new Dictionary<string, object>
        {
            { "@QueryHash", hashValue }
        };

        try
        {
            var results = await ExecuteQueryAsync(
                instanceName,
                databaseName,
                sql,
                parameters,
                reader =>
                {
                    var planXml = GetStringOrNull(reader, "PlanXml");
                    var planId = GetRequiredInt64(reader, "PlanId");
                    var planHash = GetRequiredInt64(reader, "QueryPlanHash");
                    var isForced = GetBooleanOrNull(reader, "IsForced") ?? false;
                    var createdAt = GetDateTimeOrNull(reader, "CreatedAt");
                    var lastUsed = GetDateTimeOrNull(reader, "LastUsedAt");

                    if (string.IsNullOrEmpty(planXml))
                        return null;

                    var details = ParsePlanXml(planXml, planId, isForced);
                    if (details == null)
                        return null;

                    return new PlanDetailsResult
                    {
                        PlanHash = $"0x{planHash:X16}",
                        PlanXml = details.PlanXml,
                        EstimatedCost = details.EstimatedCost,
                        EstimatedRows = details.EstimatedRows,
                        IsParallel = details.IsParallel,
                        DegreeOfParallelism = details.DegreeOfParallelism,
                        QueryStorePlanId = planId,
                        IsForced = isForced,
                        CreatedAtUtc = createdAt,
                        LastUsedAtUtc = lastUsed
                    };
                },
                commandTimeout: 60,
                cancellationToken);

            return results.Where(r => r != null).Cast<PlanDetailsResult>().ToList();
        }
        catch (SqlException ex) when (ex.Number == 208) // Object doesn't exist
        {
            _logger.LogDebug("Query Store not available for {Database}", databaseName);
            return Array.Empty<PlanDetailsResult>();
        }
    }

    /// <summary>
    /// Gets plans from DMV plan cache.
    /// </summary>
    private async Task<IReadOnlyList<PlanDetailsResult>> GetPlansFromDmvAsync(
        string instanceName,
        string databaseName,
        string queryHash,
        CancellationToken cancellationToken)
    {
        if (!TryParseQueryHash(queryHash, out var hashValue))
        {
            return Array.Empty<PlanDetailsResult>();
        }

        var sql = @"
            SELECT 
                qs.query_plan_hash AS QueryPlanHash,
                qp.query_plan AS PlanXml,
                qs.creation_time AS CreatedAt,
                qs.last_execution_time AS LastUsedAt
            FROM sys.dm_exec_query_stats qs
            CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
            WHERE qs.query_hash = @QueryHash
              AND qp.query_plan IS NOT NULL
            ORDER BY qs.last_execution_time DESC";

        var parameters = new Dictionary<string, object>
        {
            { "@QueryHash", hashValue }
        };

        var results = await ExecuteQueryAsync(
            instanceName,
            databaseName,
            sql,
            parameters,
            reader =>
            {
                var planXml = GetStringOrNull(reader, "PlanXml");
                var planHash = GetRequiredInt64(reader, "QueryPlanHash");
                var createdAt = GetDateTimeOrNull(reader, "CreatedAt");
                var lastUsed = GetDateTimeOrNull(reader, "LastUsedAt");

                if (string.IsNullOrEmpty(planXml))
                    return null;

                var details = ParsePlanXml(planXml, null, false);
                if (details == null)
                    return null;

                return new PlanDetailsResult
                {
                    PlanHash = $"0x{planHash:X16}",
                    PlanXml = details.PlanXml,
                    EstimatedCost = details.EstimatedCost,
                    EstimatedRows = details.EstimatedRows,
                    IsParallel = details.IsParallel,
                    DegreeOfParallelism = details.DegreeOfParallelism,
                    QueryStorePlanId = null,
                    IsForced = false,
                    CreatedAtUtc = createdAt,
                    LastUsedAtUtc = lastUsed
                };
            },
            commandTimeout: 60,
            cancellationToken);

        return results.Where(r => r != null).Cast<PlanDetailsResult>().ToList();
    }

    /// <summary>
    /// Parses the XML execution plan to extract key metrics.
    /// </summary>
    private PlanDetailsResult? ParsePlanXml(string planXml, long? queryStorePlanId, bool isForced)
    {
        if (string.IsNullOrEmpty(planXml))
            return null;

        try
        {
            // Parse the showplan XML to extract key metrics
            var doc = System.Xml.Linq.XDocument.Parse(planXml);
            var ns = doc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;

            // Find the StatementType element with cost information
            var stmtElement = doc.Descendants(ns + "StmtSimple").FirstOrDefault()
                ?? doc.Descendants(ns + "StmtCond").FirstOrDefault();

            double estimatedCost = 0;
            double? estimatedRows = null;

            if (stmtElement != null)
            {
                if (double.TryParse(stmtElement.Attribute("StatementSubTreeCost")?.Value, out var cost))
                {
                    estimatedCost = cost;
                }
                if (double.TryParse(stmtElement.Attribute("StatementEstRows")?.Value, out var rows))
                {
                    estimatedRows = rows;
                }
            }

            // Check for parallelism
            var parallelElements = doc.Descendants(ns + "Parallelism").ToList();
            var isParallel = parallelElements.Any();
            int? dop = null;

            if (isParallel)
            {
                // Try to find DOP from QueryPlan element
                var queryPlan = doc.Descendants(ns + "QueryPlan").FirstOrDefault();
                if (int.TryParse(queryPlan?.Attribute("DegreeOfParallelism")?.Value, out var dopValue))
                {
                    dop = dopValue;
                }
            }

            // Extract plan hash from XML if available
            var planHash = doc.Descendants(ns + "QueryPlan")
                .FirstOrDefault()?.Attribute("QueryPlanHash")?.Value ?? "0x0";

            return new PlanDetailsResult
            {
                PlanHash = planHash,
                PlanXml = planXml,
                EstimatedCost = estimatedCost,
                EstimatedRows = estimatedRows,
                IsParallel = isParallel,
                DegreeOfParallelism = dop,
                QueryStorePlanId = queryStorePlanId,
                IsForced = isForced
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse execution plan XML");

            // Return with just the raw XML if parsing fails
            return new PlanDetailsResult
            {
                PlanHash = "0x0",
                PlanXml = planXml,
                EstimatedCost = 0,
                QueryStorePlanId = queryStorePlanId,
                IsForced = isForced
            };
        }
    }

    /// <summary>
    /// Tries to parse a query hash from hex string format.
    /// </summary>
    private static bool TryParseQueryHash(string queryHash, out long hashValue)
    {
        hashValue = 0;
        if (string.IsNullOrEmpty(queryHash))
            return false;

        var hex = queryHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? queryHash[2..]
            : queryHash;

        return long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out hashValue);
    }
}
