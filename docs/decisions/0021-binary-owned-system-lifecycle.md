# ADR 0021: Binary-owned system lifecycle

- Status: Accepted for post-r6 development
- Date: 2026-11-12

## Context

The Bash and PowerShell bootstraps accumulated role selection, integration
detection, TOML generation, secret handling, path policy, permissions, and
service orchestration. Maintaining that product logic twice caused the scripts
to expose provider implementations as deployment topologies and made equivalent
cross-platform behavior difficult to prove. Supporting both portable/user-local
and system service installations also multiplied path, privilege, and uninstall
semantics without serving the intended always-on deployment model.

## Decision

- Bootstrap scripts only resolve the current stable platform asset, verify its
  published checksum, extract it to a temporary directory, and invoke
  `llm-glue setup`.
- The binary owns detection, prompting, TOML generation, validation, secrets,
  canonical installation, permissions, service registration, activation, repair,
  and uninstall behavior.
- Installation is system-wide and requires elevation. Windows keeps the complete
  installation under `%ProgramData%\LLMGlue`, with the executable in its `bin`
  child and only that child added to the machine PATH. Linux uses
  `/usr/local/bin/llm-glue`, `/etc/llm-glue`, `/var/lib/llm-glue`, and
  `/var/log/llm-glue`.
- Automatically generated/renewed TLS material and update/rollback staging are
  mutable state under the platform state directory. Operator-supplied
  certificates remain externally owned, configured by path, and read-only to
  the service.
- The service remains least-privileged: LocalService on Windows and the fixed
  non-login `llm-glue` account on Linux. Administrative setup does not imply an
  Administrator/root runtime.
- Regular uninstall removes the service and binary, reconciles owned target
  entries, and retains sidecar/state/logs. Clean uninstall additionally removes
  every recorded LLM Glue-owned resource and the Linux service account. It fails
  closed rather than rewriting or deleting an unproven operator-owned reference.
- The rollout is ordered: publish a setup-capable binary with legacy bootstraps,
  then reduce the raw bootstraps after that release is the stable download.

## Consequences

There is one configuration/lifecycle implementation and one automated matrix.
Portable foreground installations cease to be a supported product path. OS
service and ACL primitives remain platform-specific inside the binary. Setup
must record ownership for integration scaffolding so clean uninstall can be both
complete and non-destructive.
