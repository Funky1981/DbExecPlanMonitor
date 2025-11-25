# 12 – Security and Safety Rails

This file sets out the **guardrails** to avoid harming production systems.

## Security Principles

- **Least privilege**:
  - Monitoring account should have only the permissions required to:
    - Read DMVs / Query Store.
    - Read from our monitoring schema.
  - Remediation account (if used) may need elevated privileges; treat separately.

- **Separation of duties**:
  - Ideally, monitoring and remediation run under different credentials.
  - Human approval step for production remediation.

- **Secure transport**:
  - Use encrypted connections (e.g., TLS) to SQL Server.
  - Secure alert channels (Teams/Slack webhooks, SMTP with TLS).

## Safety Modes

Define explicit modes in config, e.g.:

- `MonitoringMode`:
  - `ReadOnly` (default)
  - `SuggestRemediation`
  - `AutoApplyLowRisk`

The application must treat `ReadOnly` as the default if configuration is missing.

## Environment Awareness

- The service should be aware of its environment:
  - `Development`
  - `Test`
  - `Staging`
  - `Production`

Behaviour can differ:

- Auto-remediation might be allowed in Test/Staging.
- Production might only allow suggestions.

## Protective Checks

Before executing any remediation:

- Check:
  - Environment is allowed.
  - Mode is not `ReadOnly`.
  - Suggestion is allowed by policy.
- Log the decision with clear reason.

## Access Control Around Config

- Connection strings and critical options should:
  - Not be editable by arbitrary users.
  - Be managed via secured configuration stores.

Next: see `13-testing-strategy-and-demo-environment.md` for how we test this system safely.
