# 11 – Logging, Telemetry, and Auditing

This file defines the **observability** requirements.

## Logging

Use `ILogger<T>` everywhere, with a pluggable logging provider (e.g., Serilog).

Key events to log:

- Job start & end + duration.
- Per-instance/database collection results:
  - How many samples collected.
- Detected regressions and hotspots:
  - Query fingerprint
  - Metrics summary
- Errors:
  - DB connection failures
  - Query timeouts
  - Configuration issues

Logging levels:

- `Information` for normal operations.
- `Warning` for non-fatal issues (e.g., one instance unreachable).
- `Error` for significant failures.
- `Debug` for detailed troubleshooting (disabled by default in prod).

## Telemetry

If an APM/metrics system is available (Prometheus, App Insights, etc.):

- Expose metrics like:
  - `db_exec_monitor_plan_collection_duration_seconds`
  - `db_exec_monitor_samples_collected_total`
  - `db_exec_monitor_regressions_detected_total`
  - `db_exec_monitor_alerts_sent_total`

Even without a full metrics system, we can:

- Log these as structured logs.
- Integrate later with a metrics backend.

## Auditing (Especially for Remediation)

Whenever the service *executes* any DB-changing statement (optional feature):

- Log an **audit entry** containing:
  - Timestamp
  - Instance + database
  - Query fingerprint
  - Remediation suggestion ID
  - T-SQL executed (sanitised if needed)
  - Result (success/failure + error message)

Consider writing these to a dedicated table:

- `monitoring.RemediationAudit`

This table is separate from normal logs for easy reporting.

Next: see `12-security-and-safety-rails.md` to define the guardrails that keep us from doing something dangerous.
