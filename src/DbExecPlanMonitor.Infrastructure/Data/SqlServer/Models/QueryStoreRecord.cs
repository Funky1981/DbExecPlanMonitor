namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer.Models;

/// <summary>
/// Raw record from SQL Server Query Store views.
/// Maps directly to Query Store catalog views.
/// </summary>
/// <remarks>
/// Query Store provides richer historical data than DMVs:
/// - Persists across restarts
/// - Tracks plan history over time
/// - Supports plan forcing
/// 
/// Columns map to:
/// - sys.query_store_query (query info)
/// - sys.query_store_plan (plan info)
/// - sys.query_store_runtime_stats (runtime metrics)
/// </remarks>
public class QueryStoreRecord
{
    // Query identification
    public long QueryId { get; set; }
    public long QueryHash { get; set; }
    public string? QueryText { get; set; }

    // Object info
    public int? ObjectId { get; set; }
    public string? ObjectName { get; set; }
    public string? SchemaName { get; set; }

    // Plan identification
    public long PlanId { get; set; }
    public long QueryPlanHash { get; set; }
    public string? PlanXml { get; set; }

    // Plan metadata
    public bool IsForced { get; set; }
    public bool IsNativelyCompiled { get; set; }
    public bool HasInlinedPlan { get; set; }
    public int? CompatibilityLevel { get; set; }

    // Runtime stats interval
    public long RuntimeStatsIntervalId { get; set; }
    public DateTime? IntervalStartTime { get; set; }
    public DateTime? IntervalEndTime { get; set; }

    // Execution counts
    public long ExecutionCount { get; set; }

    // CPU time (microseconds)
    public double AvgCpuTime { get; set; }
    public double LastCpuTime { get; set; }
    public double MinCpuTime { get; set; }
    public double MaxCpuTime { get; set; }
    public double StdevCpuTime { get; set; }

    // Duration (microseconds)
    public double AvgDuration { get; set; }
    public double LastDuration { get; set; }
    public double MinDuration { get; set; }
    public double MaxDuration { get; set; }
    public double StdevDuration { get; set; }

    // Logical reads
    public double AvgLogicalReads { get; set; }
    public double LastLogicalReads { get; set; }
    public double MinLogicalReads { get; set; }
    public double MaxLogicalReads { get; set; }
    public double StdevLogicalReads { get; set; }

    // Physical reads
    public double AvgPhysicalReads { get; set; }
    public double LastPhysicalReads { get; set; }
    public double MinPhysicalReads { get; set; }
    public double MaxPhysicalReads { get; set; }

    // Writes
    public double AvgLogicalWrites { get; set; }
    public double LastLogicalWrites { get; set; }
    public double MinLogicalWrites { get; set; }
    public double MaxLogicalWrites { get; set; }

    // Rows
    public double AvgRowCount { get; set; }
    public double LastRowCount { get; set; }
    public double MinRowCount { get; set; }
    public double MaxRowCount { get; set; }

    // Memory grants (KB)
    public double? AvgMemoryGrant { get; set; }
    public double? LastMemoryGrant { get; set; }
    public double? MinMemoryGrant { get; set; }
    public double? MaxMemoryGrant { get; set; }

    // Tempdb spills
    public double? AvgTempdbSpaceUsed { get; set; }

    // Timestamps
    public DateTime? FirstExecutionTime { get; set; }
    public DateTime? LastExecutionTime { get; set; }
    public DateTime? PlanCreationTime { get; set; }
    public DateTime? LastCompileTime { get; set; }

    // Helpers for conversion (Query Store uses microseconds)
    public double AvgCpuTimeMs => AvgCpuTime / 1000.0;
    public double AvgDurationMs => AvgDuration / 1000.0;
    public double MinCpuTimeMs => MinCpuTime / 1000.0;
    public double MaxCpuTimeMs => MaxCpuTime / 1000.0;
    public double MinDurationMs => MinDuration / 1000.0;
    public double MaxDurationMs => MaxDuration / 1000.0;

    // Total values (for ranking/comparison)
    public double TotalCpuTimeMs => AvgCpuTimeMs * ExecutionCount;
    public double TotalDurationMs => AvgDurationMs * ExecutionCount;
    public double TotalLogicalReads => AvgLogicalReads * ExecutionCount;

    public string QueryHashHex => $"0x{QueryHash:X16}";
    public string PlanHashHex => $"0x{QueryPlanHash:X16}";
}
