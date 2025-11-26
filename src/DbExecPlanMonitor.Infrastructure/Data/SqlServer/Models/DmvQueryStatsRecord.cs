namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer.Models;

/// <summary>
/// Raw record from SQL Server DMVs (sys.dm_exec_query_stats + related views).
/// This is the Infrastructure model - maps directly to DMV columns.
/// </summary>
/// <remarks>
/// Columns map to:
/// - sys.dm_exec_query_stats (execution statistics)
/// - sys.dm_exec_sql_text (query text)
/// - sys.dm_exec_query_plan (execution plan)
/// 
/// All times are in microseconds from SQL Server, converted to ms in mapping.
/// </remarks>
public class DmvQueryStatsRecord
{
    // Identifiers
    public byte[] SqlHandle { get; set; } = Array.Empty<byte>();
    public byte[] PlanHandle { get; set; } = Array.Empty<byte>();
    public byte[] QueryHash { get; set; } = Array.Empty<byte>();
    public byte[] QueryPlanHash { get; set; } = Array.Empty<byte>();
    public long StatementStartOffset { get; set; }
    public long StatementEndOffset { get; set; }

    // Query text
    public string? QueryText { get; set; }
    public string? ObjectName { get; set; }
    public int? ObjectId { get; set; }
    public string? DatabaseName { get; set; }

    // Execution counts
    public long ExecutionCount { get; set; }

    // CPU time (microseconds in SQL Server)
    public long TotalWorkerTime { get; set; }
    public long LastWorkerTime { get; set; }
    public long MinWorkerTime { get; set; }
    public long MaxWorkerTime { get; set; }

    // Elapsed time (microseconds in SQL Server)
    public long TotalElapsedTime { get; set; }
    public long LastElapsedTime { get; set; }
    public long MinElapsedTime { get; set; }
    public long MaxElapsedTime { get; set; }

    // Logical reads
    public long TotalLogicalReads { get; set; }
    public long LastLogicalReads { get; set; }
    public long MinLogicalReads { get; set; }
    public long MaxLogicalReads { get; set; }

    // Physical reads
    public long TotalPhysicalReads { get; set; }
    public long LastPhysicalReads { get; set; }
    public long MinPhysicalReads { get; set; }
    public long MaxPhysicalReads { get; set; }

    // Writes
    public long TotalLogicalWrites { get; set; }
    public long LastLogicalWrites { get; set; }
    public long MinLogicalWrites { get; set; }
    public long MaxLogicalWrites { get; set; }

    // Rows
    public long TotalRows { get; set; }
    public long LastRows { get; set; }
    public long MinRows { get; set; }
    public long MaxRows { get; set; }

    // Memory grants (SQL Server 2016+)
    public long? TotalGrantKb { get; set; }
    public long? LastGrantKb { get; set; }
    public long? MinGrantKb { get; set; }
    public long? MaxGrantKb { get; set; }

    // Spills (SQL Server 2016+)
    public long? TotalSpills { get; set; }
    public long? LastSpills { get; set; }

    // Timestamps
    public DateTime CreationTime { get; set; }
    public DateTime LastExecutionTime { get; set; }

    // Plan info
    public string? PlanXml { get; set; }

    // Helpers for conversion
    public double TotalCpuTimeMs => TotalWorkerTime / 1000.0;
    public double TotalDurationMs => TotalElapsedTime / 1000.0;
    public double AvgCpuTimeMs => ExecutionCount > 0 ? TotalCpuTimeMs / ExecutionCount : 0;
    public double AvgDurationMs => ExecutionCount > 0 ? TotalDurationMs / ExecutionCount : 0;

    public string QueryHashHex => "0x" + BitConverter.ToString(QueryHash).Replace("-", "");
    public string PlanHashHex => "0x" + BitConverter.ToString(QueryPlanHash).Replace("-", "");
}
