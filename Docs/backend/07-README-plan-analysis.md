# Document 07: Plan Analysis and Regression Detection - Implementation Guide

## Overview

This document covers the implementation of the **analysis layer** - the component that compares current query performance against established baselines and detects regressions. This is where the monitoring system transitions from passive data collection to active problem detection.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                      Analysis Layer                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────────┐    ┌──────────────────────┐          │
│  │  AnalysisOrchestrator│───>│  BaselineService     │          │
│  │  (Application)       │    │  (Application)       │          │
│  └──────────┬───────────┘    └──────────┬───────────┘          │
│             │                           │                        │
│             ▼                           ▼                        │
│  ┌──────────────────────┐    ┌──────────────────────┐          │
│  │  RegressionDetector  │    │  HotspotDetector     │          │
│  │  (Domain Service)    │    │  (Domain Service)    │          │
│  └──────────┬───────────┘    └──────────────────────┘          │
│             │                                                    │
│             ▼                                                    │
│  ┌──────────────────────┐    ┌──────────────────────┐          │
│  │  RegressionEvent     │    │  PlanBaseline        │          │
│  │  (Domain Entity)     │    │  (Domain Entity)     │          │
│  └──────────────────────┘    └──────────────────────┘          │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Domain Layer Components

### 1. Regression Detection Algorithm

The regression detector compares current metrics against baseline values using configurable thresholds:

```csharp
// Check if P95 duration regressed
var increase = CalculatePercentIncrease(baseline.P95DurationUs, current.P95DurationUs);
if (increase >= rules.DurationIncreaseThresholdPercent)
{
    regressions.Add(("P95Duration", baseline, current, increase));
}
```

**Severity Classification:**
| Percent Increase | Severity |
|-----------------|----------|
| 500%+ (6x) | Critical |
| 200%+ (3x) | High |
| 100%+ (2x) | Medium |
| < 100% | Low |

### 2. Entity Workflow Pattern

`RegressionEvent` implements a state machine for workflow management:

```
┌─────┐  Acknowledge()  ┌──────────────┐  Resolve()  ┌──────────┐
│ New │ ───────────────> │ Acknowledged │ ──────────> │ Resolved │
└─────┘                  └──────────────┘             └──────────┘
                                                            ▲
                                                            │
                         ┌──────────────┐                   │
                         │ AutoResolved │ ──────────────────┘
                         └──────────────┘
                              (when performance returns to normal)
```

**Encapsulated Behavior:**
```csharp
public void Acknowledge(string acknowledgedBy)
{
    if (Status != RegressionStatus.New)
        throw new InvalidOperationException($"Cannot acknowledge in status {Status}");
    
    Status = RegressionStatus.Acknowledged;
    AcknowledgedAtUtc = DateTime.UtcNow;
    AcknowledgedBy = acknowledgedBy;
}
```

### 3. Baseline Soft-Delete Pattern

Baselines are never physically deleted - they're "superseded":

```csharp
public void Supersede()
{
    IsActive = false;
    SupersededAtUtc = DateTime.UtcNow;
}
```

This preserves audit history and enables trend analysis.

## Application Layer Components

### 1. BaselineService

Computes baselines from historical samples:

1. **Fetch samples** from the lookback window (default: 7 days)
2. **Calculate percentiles** - P50 (median), P95, P99 for each metric
3. **Supersede existing** baseline for the fingerprint
4. **Save new baseline** with computed statistics

### 2. AnalysisOrchestrator

Coordinates the analysis workflow:

```
For each enabled instance:
  For each enabled database:
    For each fingerprint:
      1. Get active baseline
      2. Get recent aggregated metrics
      3. Call RegressionDetector
      4. If regression found, check for existing
      5. Save new regression event if needed
    Run hotspot detection
    Return database result
Return run summary
```

### 3. Configuration Options

```json
{
  "Analysis": {
    "RecentWindow": "01:00:00",
    "BaselineRefreshInterval": "1.00:00:00",
    "RegressionRules": {
      "DurationIncreaseThresholdPercent": 50,
      "CpuIncreaseThresholdPercent": 50,
      "LogicalReadsIncreaseThresholdPercent": 100,
      "MinimumExecutions": 5,
      "MinimumBaselineSamples": 10
    },
    "HotspotRules": {
      "TopN": 10,
      "RankingMetric": "TotalCpuTime"
    }
  }
}
```

## Infrastructure Updates

### Repository Methods Added

**IBaselineRepository:**
- `SaveAsync(PlanBaseline)` - Save domain entity
- `GetActiveByFingerprintIdAsync(Guid)` - Get active baseline
- `SupersedeActiveBaselineAsync(Guid)` - Soft-delete

**IRegressionEventRepository:**
- `SaveAsync(RegressionEvent)` - Save domain entity
- `UpdateAsync(RegressionEvent)` - Update workflow state
- `GetActiveByFingerprintIdAsync(Guid)` - Check for existing active regression
- `GetActiveAsync()` - All unresolved regressions

## Files Created/Modified

### New Domain Layer Files
| File | Purpose |
|------|---------|
| `Domain/Services/IRegressionDetector.cs` | Interface + `AggregatedMetricsForAnalysis` + `RegressionDetectionRules` |
| `Domain/Services/RegressionDetector.cs` | Detection algorithm implementation |
| `Domain/Services/IHotspotDetector.cs` | Interface + `Hotspot` record + `HotspotRankingMetric` enum |
| `Domain/Services/HotspotDetector.cs` | Hotspot identification implementation |
| `Domain/Entities/RegressionEvent.cs` | Entity with workflow methods |
| `Domain/Entities/PlanBaseline.cs` | Entity with soft-delete pattern |
| `Domain/Enums/RegressionEnums.cs` | `RegressionSeverity`, `RegressionStatus` |

### New Application Layer Files
| File | Purpose |
|------|---------|
| `Application/Services/IBaselineService.cs` | Baseline computation interface |
| `Application/Services/BaselineService.cs` | Implementation with percentile calculation |
| `Application/Services/IAnalysisOrchestrator.cs` | Analysis workflow interface |
| `Application/Services/AnalysisOrchestrator.cs` | Orchestrator implementation |
| `Application/Services/AnalysisOptions.cs` | Configuration classes |

### Modified Infrastructure Files
| File | Changes |
|------|---------|
| `Persistence/SqlBaselineRepository.cs` | Added domain entity methods |
| `Persistence/SqlRegressionEventRepository.cs` | Added domain entity methods |

### Modified Interface Files
| File | Changes |
|------|---------|
| `Application/Interfaces/IBaselineRepository.cs` | Added `SaveAsync`, `SupersedeActiveBaselineAsync` |
| `Application/Interfaces/IRegressionEventRepository.cs` | Added entity-based methods |

## Key Design Decisions

### 1. Domain Service vs Entity Behavior

- **Domain Services** (`IRegressionDetector`, `IHotspotDetector`): Stateless, pure logic that compares data
- **Entity Behavior** (`RegressionEvent.Acknowledge()`): State transitions that enforce invariants

### 2. DTO Separation

The domain layer defines `AggregatedMetricsForAnalysis` separately from the application's `AggregatedMetrics` in `IPlanMetricsRepository`. This:
- Keeps the domain independent of infrastructure concerns
- Allows the domain to define exactly what it needs
- The orchestrator handles mapping between them

### 3. Threshold-Based Detection

Rather than using ML/statistical methods initially, we use simple percentage thresholds:
- Easier to understand and explain
- Predictable behavior
- Can be tuned per-environment
- Foundation for more sophisticated detection later

## Testing Strategy

### Unit Tests (Domain)
```csharp
[Fact]
public void DetectRegression_WhenP95DurationExceedsThreshold_ReturnsRegression()
{
    // Arrange
    var detector = new RegressionDetector();
    var baseline = CreateBaseline(p95DurationUs: 1000);
    var current = CreateMetrics(p95DurationUs: 1600); // 60% increase
    var rules = new RegressionDetectionRules { DurationIncreaseThresholdPercent = 50 };
    
    // Act
    var result = detector.DetectRegression(baseline, current, rules);
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal(RegressionSeverity.Low, result.Severity);
}
```

### Integration Tests
- Test full flow: samples → baseline → regression detection → event storage
- Verify correct severity classification
- Test workflow transitions (New → Acknowledged → Resolved)

## Next Steps

Document 08 will cover **Alerting and Remediation Workflows**:
- Notification channels (email, Teams, PagerDuty)
- Alert escalation policies
- Auto-remediation actions (plan forcing, index hints)
- Integration with incident management systems
