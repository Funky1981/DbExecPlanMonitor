using System.Xml.Linq;
using DbExecPlanMonitor.Domain.Entities;

namespace DbExecPlanMonitor.Domain.Services;

/// <summary>
/// Analyzes performance issues and generates remediation suggestions.
/// </summary>
/// <remarks>
/// Uses heuristics based on common SQL Server performance patterns:
/// <list type="bullet">
/// <item>Plan changes → consider forcing previous plan</item>
/// <item>CPU regression → likely cardinality or parameter issues</item>
/// <item>I/O regression → often missing indexes or outdated statistics</item>
/// <item>Missing indexes in plan XML → suggest index creation</item>
/// </list>
/// </remarks>
public sealed class RemediationAdvisor : IRemediationAdvisor
{
    /// <inheritdoc />
    public IReadOnlyList<RemediationSuggestionDto> GenerateSuggestions(
        RegressionEvent regression,
        ExecutionPlanSnapshot? currentPlan = null,
        ExecutionPlanSnapshot? previousPlan = null)
    {
        ArgumentNullException.ThrowIfNull(regression);

        var suggestions = new List<RemediationSuggestionDto>();
        var priority = 1;

        // 1. If there's a plan change and we have the old plan, suggest forcing it
        if (regression.IsPlanChange && previousPlan != null)
        {
            suggestions.Add(CreatePlanForcingSuggestion(regression, priority++));
        }

        // 2. Analyze the type of regression
        if (regression.DurationChangePercent > 100 || regression.CpuChangePercent > 100)
        {
            // Significant CPU increase - likely cardinality estimation issue
            suggestions.Add(CreateUpdateStatisticsSuggestion(regression, priority++));
        }

        // 3. Analyze plan for missing indexes if we have the current plan
        if (currentPlan?.PlanXml != null)
        {
            var indexSuggestion = AnalyzePlanForMissingIndexes(currentPlan, priority++);
            if (indexSuggestion != null)
            {
                suggestions.Add(indexSuggestion);
            }
        }

        // 4. Check for parameter sniffing indicators
        if (HasParameterSniffingIndicators(regression))
        {
            suggestions.Add(CreateClearCacheSuggestion(regression, priority++));
        }

        // 5. Always add investigation suggestion as last resort
        if (suggestions.Count == 0)
        {
            suggestions.Add(CreateInvestigationSuggestion(regression));
        }

        return suggestions;
    }

    /// <inheritdoc />
    public IReadOnlyList<RemediationSuggestionDto> GenerateOptimizations(
        Hotspot hotspot,
        ExecutionPlanSnapshot? planSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(hotspot);

        var suggestions = new List<RemediationSuggestionDto>();
        var priority = 1;

        // Suggest reviewing high-execution queries
        suggestions.Add(new RemediationSuggestionDto
        {
            Type = RemediationType.Other,
            Title = "Review High-Impact Query",
            Description = $"Query is ranked #{hotspot.Rank} by {hotspot.RankedBy}. " +
                          $"Total executions: {hotspot.ExecutionCount:N0}, " +
                          $"Avg duration: {hotspot.AvgDurationMs:N2}ms.",
            SafetyLevel = ActionSafetyLevel.ManualOnly,
            Confidence = 0.6,
            Priority = priority++
        });

        // High execution count suggests caching opportunity
        if (hotspot.ExecutionCount > 10000)
        {
            suggestions.Add(new RemediationSuggestionDto
            {
                Type = RemediationType.Other,
                Title = "Consider Application-Level Caching",
                Description = $"Query executes {hotspot.ExecutionCount:N0} times. " +
                              "Consider if results could be cached in application layer.",
                SafetyLevel = ActionSafetyLevel.ManualOnly,
                Confidence = 0.5,
                Priority = priority++
            });
        }

        // Analyze plan for missing indexes
        if (planSnapshot?.PlanXml != null)
        {
            var indexSuggestion = AnalyzePlanForMissingIndexes(planSnapshot, priority++);
            if (indexSuggestion != null)
            {
                suggestions.Add(indexSuggestion);
            }
        }

        return suggestions;
    }

    private static RemediationSuggestionDto CreateInvestigationSuggestion(RegressionEvent regression)
    {
        var changeInfo = "";
        if (regression.DurationChangePercent.HasValue)
            changeInfo += $"Duration: +{regression.DurationChangePercent:N0}%. ";
        if (regression.CpuChangePercent.HasValue)
            changeInfo += $"CPU: +{regression.CpuChangePercent:N0}%. ";

        return new RemediationSuggestionDto
        {
            Type = RemediationType.Other,
            Title = "Investigate Performance Regression",
            Description = $"Manual investigation recommended. {changeInfo}" +
                          "Review query plan, statistics, and recent changes.",
            SafetyLevel = ActionSafetyLevel.ManualOnly,
            Confidence = 0.4,
            Priority = 999 // Low priority - fallback suggestion
        };
    }

    private static RemediationSuggestionDto CreatePlanForcingSuggestion(
        RegressionEvent regression,
        int priority)
    {
        var script = $@"-- Force previous plan using Query Store
-- Replace <query_id> and <plan_id> with actual values from:
-- SELECT query_id, plan_id FROM sys.query_store_plan 
-- WHERE query_hash = 0x{BitConverter.ToString(regression.OldPlanHash ?? []).Replace("-", "")}

EXEC sp_query_store_force_plan @query_id = <query_id>, @plan_id = <plan_id>;
GO

-- To unforce later:
-- EXEC sp_query_store_unforce_plan @query_id = <query_id>, @plan_id = <plan_id>;";

        return new RemediationSuggestionDto
        {
            Type = RemediationType.ForcePlan,
            Title = "Force Previous Execution Plan",
            Description = "A plan change was detected. Forcing the previous (faster) plan " +
                          "can provide immediate relief while investigating root cause.",
            ActionScript = script,
            SafetyLevel = ActionSafetyLevel.Safe,
            Confidence = 0.8,
            Priority = priority
        };
    }

    private static RemediationSuggestionDto CreateUpdateStatisticsSuggestion(
        RegressionEvent regression,
        int priority)
    {
        var script = $@"-- Update statistics with full scan for most accurate results
-- Identify tables from the query plan and update their statistics

-- Example:
-- UPDATE STATISTICS [dbo].[YourTable] WITH FULLSCAN;

-- Or update all statistics on tables referenced by this query:
-- EXEC sp_updatestats;

-- Note: FULLSCAN is more accurate but may take longer on large tables.
-- Consider using SAMPLE percent for very large tables.";

        return new RemediationSuggestionDto
        {
            Type = RemediationType.UpdateStatistics,
            Title = "Update Table Statistics",
            Description = $"Significant metric increase detected " +
                          $"(Duration: {regression.DurationChangePercent:+#;-#;0}%, " +
                          $"CPU: {regression.CpuChangePercent:+#;-#;0}%). " +
                          "Outdated statistics often cause poor cardinality estimates.",
            ActionScript = script,
            SafetyLevel = ActionSafetyLevel.Safe,
            Confidence = 0.7,
            Priority = priority
        };
    }

    private static RemediationSuggestionDto CreateClearCacheSuggestion(
        RegressionEvent regression,
        int priority)
    {
        var script = $@"-- Clear the plan from cache to force recompilation
-- Option 1: Clear specific plan (requires plan_handle)
-- DBCC FREEPROCCACHE (<plan_handle>);

-- Option 2: Clear all plans for the database
USE [{regression.DatabaseName}];
DECLARE @db_id INT = DB_ID();
DBCC FLUSHPROCINDB(@db_id);
GO

-- Option 3: Add OPTION (RECOMPILE) hint to the query
-- This forces recompilation on each execution (has overhead)";

        return new RemediationSuggestionDto
        {
            Type = RemediationType.ClearPlanCache,
            Title = "Clear Plan Cache",
            Description = "Performance pattern suggests parameter sniffing. " +
                          "Clearing the cache forces plan recompilation with current parameters.",
            ActionScript = script,
            SafetyLevel = ActionSafetyLevel.Safe,
            Confidence = 0.6,
            Priority = priority
        };
    }

    private RemediationSuggestionDto? AnalyzePlanForMissingIndexes(
        ExecutionPlanSnapshot plan,
        int priority)
    {
        if (string.IsNullOrEmpty(plan.PlanXml))
            return null;

        try
        {
            var doc = XDocument.Parse(plan.PlanXml);
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

            // Look for MissingIndexes element in the plan
            var missingIndexGroups = doc.Descendants(ns + "MissingIndexGroup")
                .Take(3) // Limit to first 3 suggestions
                .ToList();

            if (missingIndexGroups.Count == 0)
                return null;

            var indexSuggestions = new List<string>();
            foreach (var group in missingIndexGroups)
            {
                var impact = group.Attribute("Impact")?.Value ?? "unknown";
                var missingIndex = group.Descendants(ns + "MissingIndex").FirstOrDefault();
                if (missingIndex != null)
                {
                    var database = missingIndex.Attribute("Database")?.Value ?? "";
                    var schema = missingIndex.Attribute("Schema")?.Value ?? "";
                    var table = missingIndex.Attribute("Table")?.Value ?? "";
                    indexSuggestions.Add($"  - {database}.{schema}.{table} (impact: {impact}%)");
                }
            }

            var script = GenerateMissingIndexScript(missingIndexGroups, ns);

            return new RemediationSuggestionDto
            {
                Type = RemediationType.CreateIndex,
                Title = "Create Missing Index",
                Description = $"Query plan contains {missingIndexGroups.Count} missing index suggestion(s):\n" +
                              string.Join("\n", indexSuggestions),
                ActionScript = script,
                SafetyLevel = ActionSafetyLevel.RequiresReview,
                Confidence = 0.75,
                Priority = priority
            };
        }
        catch (Exception)
        {
            // Failed to parse execution plan XML - skip index suggestion
            return null;
        }
    }

    private static string GenerateMissingIndexScript(
        List<XElement> missingIndexGroups,
        XNamespace ns)
    {
        var script = "-- Missing Index Suggestions from Query Plan\n" +
                     "-- Review carefully before creating in production\n\n";

        var indexNum = 1;
        foreach (var group in missingIndexGroups)
        {
            var missingIndex = group.Descendants(ns + "MissingIndex").FirstOrDefault();
            if (missingIndex == null) continue;

            var database = missingIndex.Attribute("Database")?.Value?.Trim('[', ']') ?? "";
            var schema = missingIndex.Attribute("Schema")?.Value?.Trim('[', ']') ?? "";
            var table = missingIndex.Attribute("Table")?.Value?.Trim('[', ']') ?? "";

            var equalityCols = missingIndex.Descendants(ns + "ColumnGroup")
                .FirstOrDefault(cg => cg.Attribute("Usage")?.Value == "EQUALITY")
                ?.Descendants(ns + "Column")
                .Select(c => c.Attribute("Name")?.Value?.Trim('[', ']'))
                .Where(n => n != null)
                .ToList() ?? [];

            var inequalityCols = missingIndex.Descendants(ns + "ColumnGroup")
                .FirstOrDefault(cg => cg.Attribute("Usage")?.Value == "INEQUALITY")
                ?.Descendants(ns + "Column")
                .Select(c => c.Attribute("Name")?.Value?.Trim('[', ']'))
                .Where(n => n != null)
                .ToList() ?? [];

            var includeCols = missingIndex.Descendants(ns + "ColumnGroup")
                .FirstOrDefault(cg => cg.Attribute("Usage")?.Value == "INCLUDE")
                ?.Descendants(ns + "Column")
                .Select(c => c.Attribute("Name")?.Value?.Trim('[', ']'))
                .Where(n => n != null)
                .ToList() ?? [];

            var keyCols = equalityCols.Concat(inequalityCols).ToList();
            var indexName = $"IX_{table}_{string.Join("_", keyCols.Take(3))}";

            script += $"-- Index {indexNum}: {group.Attribute("Impact")?.Value ?? "?"}% improvement\n";
            script += $"CREATE NONCLUSTERED INDEX [{indexName}]\n";
            script += $"ON [{database}].[{schema}].[{table}] ({string.Join(", ", keyCols.Select(c => $"[{c}]"))})";

            if (includeCols.Count > 0)
            {
                script += $"\nINCLUDE ({string.Join(", ", includeCols.Select(c => $"[{c}]"))})";
            }

            script += ";\nGO\n\n";
            indexNum++;
        }

        return script;
    }

    private static bool HasParameterSniffingIndicators(RegressionEvent regression)
    {
        // Heuristic: large variance between executions often indicates parameter sniffing
        // This is a simplified check - in practice, you'd analyze execution history

        // If we have a plan change without obvious cause, parameter sniffing is possible
        if (regression.IsPlanChange)
            return true;

        // Large CPU spike with smaller duration change can indicate bad plan choice
        if (regression.CpuChangePercent > 200 && regression.DurationChangePercent < 100)
            return true;

        return false;
    }
}
