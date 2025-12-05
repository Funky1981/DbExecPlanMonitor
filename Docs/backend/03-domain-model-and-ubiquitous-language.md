# 03 – Domain Model and Ubiquitous Language

This file defines the language we use across the project. The goal is that DBAs, developers, and the code all talk about the same concepts.

## Core Concepts

- **Database Instance**
  - A monitored SQL Server instance (or cluster).
  - Identified by server name, port, and environment tags.

- **Database**
  - A specific database within an instance.

- **Query Fingerprint**
  - A logical representation of “the same query”:
    - Normalised SQL text (whitespace insensitive, literals replaced).
    - Stable identifier for grouping metrics.

- **Execution Plan Snapshot**
  - A captured plan for a query at a point in time:
    - Plan ID (from DB engine if available).
    - XML/JSON representation.
    - Plan hash (for comparison).
    - Capture time.

- **Plan Metrics**
  - Aggregated runtime statistics for a query + plan:
    - CPU time (total/avg)
    - Duration (total/avg)
    - Logical reads/writes
    - Execution count
    - Memory grants, spills, etc. (where available)

- **Baseline**
  - A reference range of “normal” performance for a query:
    - Typically median / P95 metrics over a stable period.
    - Used to detect regressions.

- **Regression Event**
  - A detected deterioration vs baseline:
    - For the same query fingerprint:
      - P95 duration increased by X% or more
      - CPU per execution increased by X%
      - Different plan hash used

- **Hotspot**
  - A plan or query currently consuming disproportionate resources.
  - Example: top N queries by CPU in the last 15 minutes.

- **Remediation Suggestion**
  - A suggested action to improve performance, such as:
    - Update statistics
    - Rebuild or add index
    - Force a known good plan (Query Store)
    - Change MAXDOP / query option (with care)

## Domain Entities (Conceptual)

These will become classes in the Domain project:

- `DatabaseInstance`
- `MonitoredDatabase`
- `QueryFingerprint`
- `ExecutionPlanSnapshot`
- `PlanMetricSample`
- `PlanBaseline`
- `RegressionDetectionRule`
- `RegressionEvent`
- `HotspotDetectionRule`
- `Hotspot`
- `RemediationSuggestion`

## Aggregate Roots

Candidate aggregates:

- `MonitoredDatabase` aggregate:
  - Contains:
    - Query fingerprints
    - Baselines
    - Rules
- `QueryFingerprint` aggregate:
  - Contains:
    - Plan snapshots
    - Metric samples
    - Baseline
    - Regression events

## Domain Services

- `IRegressionDetector`
  - Input: baseline + recent metrics
  - Output: list of `RegressionEvent`

- `IHotspotDetector`
  - Input: recent metrics across many queries
  - Output: list of `Hotspot`

- `IRemediationAdvisor`
  - Input: regression/hotspot + plan details
  - Output: `RemediationSuggestion` list

These stay in the Domain layer and operate on pure domain models.

Next: see `04-database-integration-and-metadata-model.md` for how we talk to SQL Server and model its metadata.
