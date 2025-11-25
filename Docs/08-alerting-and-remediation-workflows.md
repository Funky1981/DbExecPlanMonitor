# 08 – Alerting and Remediation Workflows

This file covers **how we surface issues** and **how remediation is proposed/executed**.

## Alerting Goals

- Make alerts **actionable**, not noisy.
- Include enough context to decide:
  - “Ignore”
  - “Investigate later”
  - “Act now”

## Alert Types

1. **Regression Alert**
   - Triggered by `RegressionEvent`.
   - Includes:
     - Query fingerprint
     - Example SQL (truncated)
     - Baseline vs current metrics
     - Plan hash change information

2. **Hotspot Alert**
   - Triggered by `Hotspot`.
   - Typically summarised (e.g., top 10 hotspots).

3. **Daily Summary**
   - Top regressions + hotspots over the last day.

## Alert Channels

Design for pluggable channels via Strategy pattern:

- `IAlertChannel`
  - `Task SendRegressionAlertsAsync(IEnumerable<RegressionEvent> events, CancellationToken ct);`
  - `Task SendHotspotSummaryAsync(IEnumerable<Hotspot> hotspots, CancellationToken ct);`

Implementations:

- `EmailAlertChannel`
- `TeamsAlertChannel` or `SlackAlertChannel`
- `LogOnlyAlertChannel` (for testing)

## Remediation Strategy (v1 – Human-Guided)

Our default stance: **do not auto-change production**.

Instead:

- Provide `RemediationSuggestion` objects with:
  - Description
  - Rationale
  - Risk level (Low/Medium/High)
  - Suggested script or T-SQL snippet

Example suggestions:

- “Update statistics on table X (index Y).”
- “Consider adding index (Columns...) based on missing index hint.”
- “Query Store: force plan with PlanId = 12345.”

This logic lives in a domain service: `IRemediationAdvisor`.

## Optional Semi-Automatic Mode

If explicitly enabled per environment:

- Provide an **“auto-apply low-risk”** mode:
  - Only apply suggestions that:
    - Are classified as Low risk.
    - Are whitelisted by configuration (e.g., only update stats).
- All actions must:
  - Be logged with:
    - Who/what triggered them (service account).
    - Timestamps.
    - Commands executed.
  - Be reversible where possible.

We will design an application service like:

```csharp
public interface IRemediationExecutor
{
    Task ExecuteAsync(RemediationSuggestion suggestion, CancellationToken ct);
}
```

SQL Server implementation lives in Infrastructure and uses ADO.NET.

## Operational Workflow Example

1. Service detects regression for Query A.
2. `IRemediationAdvisor` generates suggestions (e.g., update stats).
3. Alert is sent with:
   - Regression details
   - Suggested T-SQL script
4. Operator:
   - Applies script manually, **or**
   - Approves suggestion in some UI / control channel (future enhancement).
5. Service logs the action (if executed by service).

Next: see `09-background-service-hosting-and-scheduling.md` for how we host the service and run recurring jobs.
