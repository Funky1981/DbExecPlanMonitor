# 07 – Plan Analysis and Regression Detection

This file describes how we go from **raw samples** to **useful insights**.

## Objectives

- Build and maintain baselines for each query fingerprint.
- Detect regressions when current performance deviates from baseline.
- Detect hotspots in the most recent window.

## Baseline Strategy

For each `QueryFingerprint`:

- Maintain a `PlanBaseline` with:
  - Median / P95 duration
  - Median / P95 CPU per execution
  - Typical execution count per interval
- Baseline is computed from:
  - A configurable historical window (e.g., last 7 days), excluding:
    - Extremely low sample counts
    - Obvious outliers (future enhancement)

Baselines can be recomputed periodically (e.g., nightly job).

## Regression Detection Rules

Example rule:

> If P95 duration in the last 15 minutes is **> 150%** of baseline P95 duration **and** execution count ≥ baseline execution count threshold, flag a regression.

In domain terms:

```csharp
public sealed class RegressionDetectionRule
{
    public decimal DurationIncreaseThresholdPercent { get; init; }
    public decimal CpuIncreaseThresholdPercent { get; init; }
    public int MinimumExecutions { get; init; }
}
```

`IRegressionDetector` takes:

- `PlanBaseline baseline`
- Recent `PlanMetricSample` set

and returns zero or more `RegressionEvent`.

## Hotspot Detection

A hotspot is simply:

- A query or plan that is in the **top N** for:
  - CPU
  - Duration
  - Reads

in a recent window.

We can define a `HotspotDetectionRule`:

- `TopN` (e.g., 10–50)
- `MinTotalCpuMs` etc.

`IHotspotDetector` will:

- Aggregate samples for the last X minutes.
- Sort and pick top queries.
- Return `Hotspot` domain objects.

## Application Services

Use cases:

- `DetectRegressionsForInstance`
  - For each database:
    - Load recent samples.
    - Load baselines.
    - Call `IRegressionDetector`.
    - Save `RegressionEvent` and forward to alerting pipeline.

- `DetectHotspotsForInstance`
  - For each database:
    - Load recent samples.
    - Call `IHotspotDetector`.
    - Forward results to alerting pipeline.

These will be triggered by scheduled jobs.

## Output for Alerts

Each `RegressionEvent` or `Hotspot` should contain:

- Database instance & database name.
- Query fingerprint & truncated SQL text.
- Key metrics:
  - Baseline P95 vs current P95.
  - Baseline CPU vs current CPU.
- Plan hashes (old vs new, if available).
- Link or identifier to fetch full plan XML later.

Next: see `08-alerting-and-remediation-workflows.md` for how this info reaches humans and (optionally) triggers fixes.
