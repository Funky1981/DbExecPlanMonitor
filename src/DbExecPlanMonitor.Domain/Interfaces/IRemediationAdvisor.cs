using DbExecPlanMonitor.Domain.Entities;

namespace DbExecPlanMonitor.Domain.Interfaces;

/// <summary>
/// Generates remediation suggestions for regressions and hotspots.
/// </summary>
/// <remarks>
/// This is a pure domain service that analyzes problems and suggests fixes.
/// 
/// It uses heuristics based on:
/// - Plan characteristics (missing indexes, implicit conversions, etc.)
/// - Metric patterns (high variance = parameter sniffing)
/// - Available options (Query Store enabled = can force plans)
/// 
/// The suggestions are actionable and may include executable T-SQL scripts.
/// </remarks>
public interface IRemediationAdvisor
{
    /// <summary>
    /// Generates remediation suggestions for a regression event.
    /// </summary>
    /// <param name="regression">The regression event to remediate.</param>
    /// <returns>Prioritized list of remediation suggestions.</returns>
    IReadOnlyList<RemediationSuggestion> SuggestRemediations(RegressionEvent regression);

    /// <summary>
    /// Generates remediation suggestions for a hotspot.
    /// </summary>
    /// <param name="hotspot">The hotspot to remediate.</param>
    /// <returns>Prioritized list of remediation suggestions.</returns>
    IReadOnlyList<RemediationSuggestion> SuggestRemediations(Hotspot hotspot);

    /// <summary>
    /// Analyzes an execution plan and suggests improvements.
    /// </summary>
    /// <param name="planSnapshot">The plan to analyze.</param>
    /// <returns>Suggestions based on plan characteristics.</returns>
    IReadOnlyList<RemediationSuggestion> AnalyzePlan(ExecutionPlanSnapshot planSnapshot);

    /// <summary>
    /// Generates a plan forcing suggestion using Query Store.
    /// </summary>
    /// <param name="fingerprint">The query fingerprint.</param>
    /// <param name="goodPlan">The plan to force.</param>
    /// <returns>A suggestion with the force plan script.</returns>
    RemediationSuggestion? CreateForcePlanSuggestion(
        QueryFingerprint fingerprint,
        ExecutionPlanSnapshot goodPlan);

    /// <summary>
    /// Generates an update statistics suggestion.
    /// </summary>
    /// <param name="fingerprint">The query fingerprint.</param>
    /// <param name="tableNames">Tables that should have stats updated.</param>
    /// <returns>A suggestion with the update statistics script.</returns>
    RemediationSuggestion? CreateUpdateStatisticsSuggestion(
        QueryFingerprint fingerprint,
        IEnumerable<string> tableNames);

    /// <summary>
    /// Generates a create index suggestion based on missing index hints.
    /// </summary>
    /// <param name="fingerprint">The query fingerprint.</param>
    /// <param name="missingIndexXml">The missing index XML from the plan.</param>
    /// <returns>A suggestion with the create index script.</returns>
    RemediationSuggestion? CreateMissingIndexSuggestion(
        QueryFingerprint fingerprint,
        string missingIndexXml);
}

/// <summary>
/// Configuration for the remediation advisor.
/// </summary>
public class RemediationAdvisorOptions
{
    /// <summary>
    /// Whether to generate auto-executable suggestions for safe operations.
    /// </summary>
    public bool GenerateAutoExecutableSuggestions { get; set; } = true;

    /// <summary>
    /// Whether Query Store is available for plan forcing.
    /// </summary>
    public bool QueryStoreAvailable { get; set; } = true;

    /// <summary>
    /// Minimum improvement impact to suggest an index creation.
    /// SQL Server provides an estimated improvement percentage.
    /// </summary>
    public double MinimumIndexImprovementPercent { get; set; } = 50.0;

    /// <summary>
    /// Maximum number of suggestions to generate per problem.
    /// </summary>
    public int MaxSuggestionsPerProblem { get; set; } = 5;

    /// <summary>
    /// Whether to include risky suggestions (schema changes, etc.)
    /// </summary>
    public bool IncludeRiskySuggestions { get; set; } = false;
}
