# 14 – Deployment, Operations, and Rollout Plan

This file describes how to **deploy and operate** the service.

## Deployment Targets

Support at least:

- Windows Server – installed as a Windows Service.
- Linux – systemd service.
- Containers – Docker image, deployable to:
  - Kubernetes
  - Container orchestrators

## Build & Packaging

- Use standard .NET publishing:
  - `dotnet publish -c Release`
- Optionally build a Docker image with:
  - Minimal runtime image
  - Config via environment variables / mounted files

## Configuration per Environment

- Each environment has its own:
  - `appsettings.{Environment}.json`
  - Secrets setup (Key Vault, environment variables, etc.)
- Ensure instances/databases are appropriately enabled/disabled per environment.

## Rollout Strategy

1. **Phase 1 – Dev/Test**
   - Connect to non-critical test DB.
   - Validate:
     - Collection
     - Analysis
     - Alerts (log-only or test channel).

2. **Phase 2 – Staging / Pre-Prod**
   - Connect to staging DB that mirrors production workload.
   - Tune thresholds and noise levels.
   - Confirm:
     - No performance impact.
     - Alerts are understandable and actionable.

3. **Phase 3 – Production Read-Only**
   - Enable in production, but:
     - `MonitoringMode = ReadOnly` or `SuggestRemediation`.
   - Use alerts for a few weeks to:
     - Build trust.
     - Adjust thresholds.

4. **Phase 4 – Optional Semi-Auto Remediation**
   - Only if organisation is comfortable.
   - Start with:
     - Very small subset of low-risk suggestions.
     - Tight monitoring & audit.

## Operational Playbook

Document:

- How to:
  - Add a new instance/database to monitoring.
  - Change thresholds.
  - Temporarily disable monitoring for maintenance.
- What to do when:
  - Service cannot connect to DB.
  - Alerts spike unexpectedly (possible false positives).
  - The service itself causes load issues (rare; but have a plan).

Next: see `15-learning-path-and-next-steps.md` for how to use this project as a learning tool.
