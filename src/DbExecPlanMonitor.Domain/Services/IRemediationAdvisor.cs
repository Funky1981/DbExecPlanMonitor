using DbExecPlanMonitor.Domain.Entities;

namespace DbExecPlanMonitor.Domain.Services;

/// <summary>
/// A suggested remediation action (DTO for service layer).
/// </summary>
/// <remarks>
/// This is a lightweight data object returned by the remediation advisor.
/// It can be converted to a RemediationSuggestion entity when persisting.
/// </remarks>
public sealed class RemediationSuggestionDto
{
    /// <summary>
    /// The type of remediation action.
    /// </summary>
    public required RemediationType Type { get; init; }

    /// <summary>
    /// Short title for display.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Detailed description explaining what to do and why.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Optional T-SQL script that implements this suggestion.
    /// </summary>
    public string? ActionScript { get; init; }

    /// <summary>
    /// The safety level of this action.
    /// </summary>
    public required ActionSafetyLevel SafetyLevel { get; init; }

    /// <summary>
    /// Confidence level in this suggestion (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; init; } = 0.5;

    /// <summary>
    /// Priority/rank among suggestions (1 = try first).
    /// </summary>
    public int Priority { get; init; } = 1;
}

/// <summary>
/// Domain service that analyzes regressions and generates remediation suggestions.
/// </summary>
/// <remarks>
/// <para>
/// This is a pure domain service with no infrastructure dependencies.
/// It encapsulates the knowledge of how to diagnose performance issues
/// and what actions might fix them.
/// </para>
/// <para>
/// <strong>Design Philosophy:</strong>
/// <list type="bullet">
/// <item>Default stance: Do NOT auto-change production</item>
/// <item>Provide actionable suggestions with clear rationale</item>
/// <item>Classify risk levels to enable informed decisions</item>
/// <item>Generate T-SQL scripts that can be reviewed before execution</item>
/// </list>
/// </para>
/// <para>
/// <strong>Common Remediation Patterns:</strong>
/// <list type="bullet">
/// <item>UPDATE STATISTICS - for cardinality estimation issues</item>
/// <item>Force plan via Query Store - for plan regression</item>
/// <item>Add missing index - for scan-heavy queries</item>
/// <item>Rebuild index - for fragmented indexes</item>
/// <item>Clear procedure cache - for parameter sniffing issues</item>
/// </list>
/// </para>
/// </remarks>
public interface IRemediationAdvisor
{
    /// <summary>
    /// Analyzes a regression event and generates remediation suggestions.
    /// </summary>
    /// <param name="regression">The regression event to analyze.</param>
    /// <param name="currentPlan">The current execution plan (optional, for plan-based analysis).</param>
    /// <param name="previousPlan">The previous execution plan (optional, for comparison).</param>
    /// <returns>A list of prioritized remediation suggestions.</returns>
    IReadOnlyList<RemediationSuggestionDto> GenerateSuggestions(
        RegressionEvent regression,
        ExecutionPlanSnapshot? currentPlan = null,
        ExecutionPlanSnapshot? previousPlan = null);

    /// <summary>
    /// Analyzes a hotspot and suggests optimizations.
    /// </summary>
    /// <param name="hotspot">The hotspot to analyze.</param>
    /// <param name="planSnapshot">The execution plan (optional).</param>
    /// <returns>A list of optimization suggestions.</returns>
    IReadOnlyList<RemediationSuggestionDto> GenerateOptimizations(
        Hotspot hotspot,
        ExecutionPlanSnapshot? planSnapshot = null);
}

/// <summary>
/// Configuration for remediation behavior.
/// </summary>
public sealed class RemediationOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Remediation";

    /// <summary>
    /// Whether remediation features are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to auto-apply low-risk remediations.
    /// Should only be enabled after careful consideration.
    /// </summary>
    public bool AutoApplyLowRisk { get; set; } = false;

    /// <summary>
    /// Which remediation types are whitelisted for auto-apply.
    /// Only applies if AutoApplyLowRisk is true.
    /// </summary>
    public List<string> AutoApplyWhitelist { get; set; } = new()
    {
        "UpdateStatistics"
    };

    /// <summary>
    /// Maximum number of suggestions to generate per regression.
    /// </summary>
    public int MaxSuggestionsPerRegression { get; set; } = 5;

    /// <summary>
    /// Whether to include T-SQL scripts in suggestions.
    /// </summary>
    public bool IncludeScripts { get; set; } = true;

    /// <summary>
    /// Whether to use Query Store plan forcing when available.
    /// </summary>
    public bool UseQueryStorePlanForcing { get; set; } = true;
}
