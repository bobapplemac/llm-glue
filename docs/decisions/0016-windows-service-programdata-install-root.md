# ADR 0016: Windows service ProgramData installation root

- Status: Accepted for post-r4 development
- Date: 2026-11-11

## Context

The PowerShell bootstrap historically defaulted every installation to the
invoking user's `%LOCALAPPDATA%\LLMGlue`. When the operator selected Windows
service registration, the LocalService process therefore depended on a binary
and sidecar inside one interactive user's profile. State and logs were already
redirected to ProgramData for service modes, leaving the installation split
across machine and user scopes.

Changing the default unconditionally would make non-service installs require
elevation and could cause a bootstrap rerun to abandon an existing service
installation in LocalAppData.

## Decision

- A new service installation with no explicit `-InstallDirectory` defaults to
  `%ProgramData%\LLMGlue`.
- A non-service installation continues to default to
  `%LOCALAPPDATA%\LLMGlue`, preserving an ordinary non-elevated workflow.
- An explicit `-InstallDirectory` always wins.
- If service mode is selected and an existing per-user LLM Glue executable or
  `.conf`/`.toml` sidecar is detected while the ProgramData destination does not
  exist, the bootstrap preserves that legacy directory and prints a migration
  warning. Migration to ProgramData must be explicit rather than silently
  splitting or replacing the existing installation.
- Bootstrap reruns preserve an existing `llm-glue.toml` sidecar and do not create
  a higher-precedence `llm-glue.conf` beside it.

## Consequences

Fresh Windows services have a machine-scoped executable, configuration, state,
and logs under ProgramData with ServiceManager granting the fixed LocalService
identity only the required access. Existing installations remain stable, and
portable/per-user use stays non-elevated. The PowerShell bootstrap matrix guards
the new default, legacy preservation, and `.toml` rerun behavior.
