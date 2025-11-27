# 01-README: Problem Overview and Goals

## 📚 Summary

This document establishes **why** we're building the DB Execution Plan Monitoring Service. It defines the business problem, vision, use cases, and success criteria.

---

## 🎯 The Problem

Production SQL Server databases silently degrade when:

| Cause | Impact |
|-------|--------|
| Execution plans change after statistics/index updates | Queries suddenly run slower |
| Parameter sniffing compiles sub-optimal plans | Some parameter values cause terrible performance |
| New deployments introduce inefficient queries | Gradual or sudden performance degradation |
| A few "heavy" plans consume disproportionate resources | CPU/IO bottlenecks affect all users |
| Bad plans are reused thousands of times per minute | Problems multiply rapidly |

**The Discovery Problem**: Most teams discover issues **late** - when CPU is pegged at 90%+, the site is already slow, and customers are complaining.

---

## 🔭 The Vision

Build an automated service that:

1. **Continuously collects** execution plan data and runtime metrics
2. **Detects regressions** (same query, significantly worse performance)
3. **Detects hotspots** (top N "worst" plans by CPU, duration, reads)
4. **Notifies humans** with actionable information
5. **Optionally applies safe fixes** under controlled conditions

**v1 Focus**: Read-only monitoring and alerting with optional semi-automatic remediation (human confirms).

---

## 🚫 Non-Goals (v1)

What this service is **NOT**:

- ❌ A full APM product
- ❌ A query tuning AI
- ❌ A schema migration tool
- ❌ Auto-indexing in production without human sign-off

---

## 📋 Primary Use Cases

### 1. Daily Top-Offenders Report
```
Every morning at 7am:
  → Summarise top N execution plans by CPU, duration, logical reads
  → Send report via email/Teams/Slack
```

### 2. Execution Plan Regression Detection
```
When a query runs:
  → Compare median/P95 duration against baseline
  → If duration increased by X% or more → Record regression event
  → If CPU per execution increased by X% → Record regression event
  → If different plan hash is used → Record plan change event
```

### 3. Hot Plan Real-Time Alerts
```
Every minute:
  → Check if any query suddenly spiked in resource usage
  → If threshold exceeded → Raise alert immediately
  → Configurable thresholds per metric
```

### 4. Human-Guided Remediation
```
For a problematic plan:
  → Generate plan diff vs baseline
  → Suggest actions: update stats, recreate index, force plan
  → Operator chooses to apply manually or via automated script
```

---

## ✅ Quality Attributes

| Attribute | Description |
|-----------|-------------|
| **Safe-by-design** | Default mode is read-only. Every write/remediation is logged and auditable |
| **Observable** | Rich logs for decisions made. Metrics exported to monitoring systems |
| **Configurable** | Sampling intervals, thresholds, alert channels all configuration-driven |
| **Extensible** | Support multiple DB instances, eventually multiple DB engines |

---

## 🏆 Success Criteria

We know the service is successful when:

| Criteria | Measure |
|----------|---------|
| Early detection | Performance regressions detected within **minutes/hours**, not days |
| Visibility | DBAs/engineers can see which queries are hurting the system |
| Root cause | Teams can see when/why a plan changed |
| Trust | Teams rely on it as part of routine operational tooling |
| Automation | Optional automated fixes can be enabled for low-risk scenarios |

---

## 📁 Related Implementation

At this stage, no code is implemented - this document establishes requirements.

**Solution structure created:**
```
src/
├── DbExecPlanMonitor.sln           # Solution file
├── DbExecPlanMonitor.Domain/       # Core business logic
├── DbExecPlanMonitor.Application/  # Use cases and orchestration
├── DbExecPlanMonitor.Infrastructure/  # External integrations
└── DbExecPlanMonitor.Worker/       # Host application

tests/
├── DbExecPlanMonitor.Domain.Tests/
├── DbExecPlanMonitor.Application.Tests/
└── DbExecPlanMonitor.Infrastructure.Tests/
```

---

## ➡️ Next Steps

After understanding the problem space, proceed to:
- **[02-high-level-architecture.md](02-high-level-architecture.md)** - How we structure the solution
