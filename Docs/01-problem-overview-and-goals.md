# 01 – Problem Overview and Goals

## Problem Statement

Production databases can silently degrade when:

- Execution plans change after statistics/index changes or parameter sniffing.
- New deployments introduce slower queries.
- A few “heavy” plans consume disproportionate resources (CPU, IO, memory).
- Bad plans are *reused* thousands of times per minute.

Most teams discover this **late**, when:

- The site is already slow.
- CPU is pegged at 90%+.
- Customers are complaining.

We want an **automated service** that continuously monitors execution plans and flags (or fixes) issues quickly.

## Vision

Create a **DB Execution Plan Monitoring Service** that:

1. **Continuously collects** execution plan data and runtime metrics.
2. **Detects regressions** (same query, significantly worse performance).
3. **Detects hotspots** (top N “worst” plans by CPU, duration, reads, etc.).
4. **Notifies humans** with actionable information.
5. **Optionally applies safe fixes** (e.g., recommend or automate index, hint, or config changes) under controlled conditions.

The first version focuses on **read-only monitoring and alerting** with optional semi-automatic remediation (human confirms).

## Non-Goals (for v1)

- Not a full APM product.
- Not a query tuning AI.
- Not a schema migration tool.
- No direct auto-indexing in production without human sign-off.

## Primary Use Cases

1. **Daily Top-Offenders Report**
   - Summarise top N execution plans by CPU, duration, and logical reads.
   - Send report via email/Teams/Slack.

2. **Execution Plan Regression Detection**
   - Detect when a query’s median/95th percentile duration or CPU has worsened by X% vs baseline.
   - Record regression events for investigation.

3. **Hot Plan Real-Time Alerts**
   - If a query suddenly spikes in resource usage (e.g., “query storm”).
   - Raise an alert quickly (configurable thresholds).

4. **Human-Guided Remediation**
   - For a problematic plan, generate:
     - Plan diff vs baseline.
     - Suggested actions (update stats, recreate index, use query hint, force plan).
   - Operator chooses whether to apply the fix manually or via automated script.

## High-Level Quality Attributes

- **Safe-by-design**
  - Default mode: *read-only*, no automatic changes to production.
  - Every write/remediation action is logged and auditable.
- **Observable**
  - Rich logs for what the service saw and decided.
  - Metrics exported to standard monitoring systems.
- **Configurable**
  - Sampling intervals, thresholds, alert channels, and remediation modes are all configuration-driven.
- **Extensible**
  - Support multiple DB instances and eventually multiple DB engines via provider model.

## Success Criteria

We know this service is successful when:

- Performance regressions are detected **within minutes/hours**, not days.
- DBAs and engineers can:
  - See which queries are hurting the system.
  - See when/why a plan changed.
- The system is trusted enough that:
  - Teams rely on it as part of their routine operational tooling.
  - Optional automated fixes can be enabled for low-risk scenarios.

Next: read `02-high-level-architecture.md` to see how we structure the system using Clean Architecture and SOLID.
