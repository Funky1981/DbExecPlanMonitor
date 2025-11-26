namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// A suggested action to remediate a regression or performance issue.
/// Provides actionable guidance and optionally executable scripts.
/// </summary>
/// <remarks>
/// Suggestions can range from simple advice ("Update statistics on Orders table")
/// to executable scripts (the actual UPDATE STATISTICS command).
/// 
/// Safety levels ensure that automated execution is only allowed for
/// low-risk operations.
/// </remarks>
public class RemediationSuggestion
{
    /// <summary>
    /// Unique identifier for this suggestion.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the regression event this suggestion is for.
    /// </summary>
    public Guid RegressionEventId { get; private set; }

    /// <summary>
    /// Navigation property to the parent regression event.
    /// </summary>
    public RegressionEvent RegressionEvent { get; private set; } = null!;

    /// <summary>
    /// The type of remediation action suggested.
    /// </summary>
    public RemediationType Type { get; private set; }

    /// <summary>
    /// Short title for the suggestion (for display).
    /// </summary>
    public string Title { get; private set; }

    /// <summary>
    /// Detailed description explaining what to do and why.
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// Optional T-SQL script that implements this suggestion.
    /// Can be executed manually or (if safe) automatically.
    /// </summary>
    public string? ActionScript { get; private set; }

    /// <summary>
    /// The safety level of this action.
    /// Determines whether it can be auto-executed.
    /// </summary>
    public ActionSafetyLevel SafetyLevel { get; private set; }

    /// <summary>
    /// Confidence level in this suggestion (0.0 to 1.0).
    /// Higher = more likely to fix the issue.
    /// </summary>
    public double Confidence { get; private set; }

    /// <summary>
    /// Priority/rank among suggestions for the same regression.
    /// 1 = try this first.
    /// </summary>
    public int Priority { get; private set; }

    /// <summary>
    /// When this suggestion was generated.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Whether this suggestion was applied.
    /// </summary>
    public bool WasApplied { get; private set; }

    /// <summary>
    /// When the suggestion was applied.
    /// </summary>
    public DateTime? AppliedAtUtc { get; private set; }

    /// <summary>
    /// Who applied the suggestion.
    /// </summary>
    public string? AppliedBy { get; private set; }

    /// <summary>
    /// Whether the suggestion was successful.
    /// </summary>
    public bool? WasSuccessful { get; private set; }

    /// <summary>
    /// Result message after applying (success or error).
    /// </summary>
    public string? ResultMessage { get; private set; }

    /// <summary>
    /// Whether this suggestion was dismissed (user chose not to apply).
    /// </summary>
    public bool WasDismissed { get; private set; }

    /// <summary>
    /// Reason for dismissal.
    /// </summary>
    public string? DismissalReason { get; private set; }

    // Private constructor for EF Core
    private RemediationSuggestion() { }

    /// <summary>
    /// Creates a new remediation suggestion. Called by RegressionEvent.AddSuggestion().
    /// </summary>
    internal RemediationSuggestion(
        RegressionEvent regressionEvent,
        RemediationType type,
        string title,
        string description,
        string? actionScript = null)
    {
        if (regressionEvent == null)
            throw new ArgumentNullException(nameof(regressionEvent));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));

        Id = Guid.NewGuid();
        RegressionEventId = regressionEvent.Id;
        RegressionEvent = regressionEvent;
        Type = type;
        Title = title.Trim();
        Description = description.Trim();
        ActionScript = actionScript?.Trim();
        
        // Determine safety level based on type
        SafetyLevel = DetermineSafetyLevel(type);
        Confidence = 0.5; // Default medium confidence
        Priority = 1;
        
        CreatedAtUtc = DateTime.UtcNow;
        WasApplied = false;
        WasDismissed = false;
    }

    /// <summary>
    /// Determines the safety level based on remediation type.
    /// </summary>
    private static ActionSafetyLevel DetermineSafetyLevel(RemediationType type)
    {
        return type switch
        {
            // Safe to auto-execute - read-only or reversible
            RemediationType.ForcePlan => ActionSafetyLevel.Safe,
            RemediationType.UpdateStatistics => ActionSafetyLevel.Safe,
            RemediationType.ClearPlanCache => ActionSafetyLevel.Safe,
            
            // Requires review - makes schema changes
            RemediationType.CreateIndex => ActionSafetyLevel.RequiresReview,
            RemediationType.ModifyIndex => ActionSafetyLevel.RequiresReview,
            RemediationType.AddQueryHint => ActionSafetyLevel.RequiresReview,
            
            // Manual only - significant changes
            RemediationType.RewriteQuery => ActionSafetyLevel.ManualOnly,
            RemediationType.DropIndex => ActionSafetyLevel.ManualOnly,
            RemediationType.SchemaChange => ActionSafetyLevel.ManualOnly,
            
            _ => ActionSafetyLevel.ManualOnly
        };
    }

    /// <summary>
    /// Sets the confidence level for this suggestion.
    /// </summary>
    public void SetConfidence(double confidence)
    {
        if (confidence < 0.0 || confidence > 1.0)
            throw new ArgumentException("Confidence must be between 0.0 and 1.0", nameof(confidence));

        Confidence = confidence;
    }

    /// <summary>
    /// Sets the priority for this suggestion.
    /// </summary>
    public void SetPriority(int priority)
    {
        if (priority < 1)
            throw new ArgumentException("Priority must be at least 1", nameof(priority));

        Priority = priority;
    }

    /// <summary>
    /// Records that this suggestion was applied.
    /// </summary>
    public void MarkAsApplied(string appliedBy, bool wasSuccessful, string? resultMessage = null)
    {
        if (string.IsNullOrWhiteSpace(appliedBy))
            throw new ArgumentException("Applied by is required.", nameof(appliedBy));

        WasApplied = true;
        AppliedAtUtc = DateTime.UtcNow;
        AppliedBy = appliedBy;
        WasSuccessful = wasSuccessful;
        ResultMessage = resultMessage;
    }

    /// <summary>
    /// Records that this suggestion was auto-applied by the system.
    /// </summary>
    public void MarkAsAutoApplied(bool wasSuccessful, string? resultMessage = null)
    {
        if (SafetyLevel != ActionSafetyLevel.Safe)
            throw new InvalidOperationException($"Cannot auto-apply a suggestion with safety level {SafetyLevel}");

        WasApplied = true;
        AppliedAtUtc = DateTime.UtcNow;
        AppliedBy = "SYSTEM (auto-remediation)";
        WasSuccessful = wasSuccessful;
        ResultMessage = resultMessage;
    }

    /// <summary>
    /// Dismisses this suggestion (user chose not to apply).
    /// </summary>
    public void Dismiss(string? reason = null)
    {
        WasDismissed = true;
        DismissalReason = reason;
    }

    /// <summary>
    /// Checks if this suggestion can be automatically executed.
    /// </summary>
    public bool CanAutoExecute()
    {
        return SafetyLevel == ActionSafetyLevel.Safe 
            && !string.IsNullOrWhiteSpace(ActionScript)
            && !WasApplied
            && !WasDismissed;
    }

    /// <summary>
    /// Gets a formatted display for the suggestion.
    /// </summary>
    public string GetFormattedSuggestion()
    {
        var safetyIcon = SafetyLevel switch
        {
            ActionSafetyLevel.Safe => "✅",
            ActionSafetyLevel.RequiresReview => "⚠️",
            ActionSafetyLevel.ManualOnly => "🛑",
            _ => "❓"
        };

        return $"{safetyIcon} [{Type}] {Title} (Confidence: {Confidence:P0})";
    }
}

/// <summary>
/// The type of remediation action.
/// </summary>
public enum RemediationType
{
    /// <summary>
    /// Force a specific execution plan using Query Store.
    /// Quick fix that can be done immediately.
    /// </summary>
    ForcePlan,

    /// <summary>
    /// Update statistics on the affected table(s).
    /// Often fixes cardinality estimation issues.
    /// </summary>
    UpdateStatistics,

    /// <summary>
    /// Clear the plan from cache to force recompilation.
    /// Simple but may not be a permanent fix.
    /// </summary>
    ClearPlanCache,

    /// <summary>
    /// Create a new index to improve query performance.
    /// </summary>
    CreateIndex,

    /// <summary>
    /// Modify an existing index (add columns, etc.).
    /// </summary>
    ModifyIndex,

    /// <summary>
    /// Drop an unused or problematic index.
    /// </summary>
    DropIndex,

    /// <summary>
    /// Add a query hint to force specific behavior.
    /// </summary>
    AddQueryHint,

    /// <summary>
    /// Rewrite the query for better performance.
    /// </summary>
    RewriteQuery,

    /// <summary>
    /// Make schema changes (partitioning, etc.).
    /// </summary>
    SchemaChange,

    /// <summary>
    /// Change database or server settings.
    /// </summary>
    ConfigurationChange,

    /// <summary>
    /// Other/custom remediation.
    /// </summary>
    Other
}

/// <summary>
/// How safe this action is to execute automatically.
/// </summary>
public enum ActionSafetyLevel
{
    /// <summary>
    /// Safe to auto-execute. Reversible or read-only impact.
    /// Examples: Force plan, update statistics, clear cache.
    /// </summary>
    Safe,

    /// <summary>
    /// Requires human review before execution.
    /// Examples: Create index, add query hint.
    /// </summary>
    RequiresReview,

    /// <summary>
    /// Must be executed manually by a DBA.
    /// Examples: Schema changes, dropping indexes.
    /// </summary>
    ManualOnly
}
