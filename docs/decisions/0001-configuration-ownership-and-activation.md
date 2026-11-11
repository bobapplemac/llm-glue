# ADR 0001: Configuration ownership and activation

- Status: Accepted for r0 implementation
- Date: 2026-11-10

## Context

LLM Glue must update OpenCode and LiteLLM without erasing operator or user state.
M0 showed that OpenCode system configuration wins conflicts and merges additions,
while LiteLLM include merging is shallow outside lists and development reload is
not a dependable production activation mechanism.

## Decision

1. OpenCode uses a deterministic provider namespace in the system-managed
   document. Reconciliation parses the existing document, changes only owned
   provider/model keys, removes only previously owned stale keys after grace and
   verification, and preserves all unrelated content. User/project files are
   never written. The generator is version-gated and targets the current
   `providers` schema for OpenCode 2.x.
2. LiteLLM uses one operator-authorized generated include restricted to
   `model_list`. The primary file and manually managed routes remain
   operator-owned. Alias collisions fail closed.
3. LiteLLM activation validates YAML, atomically replaces the include, invokes
   only an explicitly configured local service action, health-checks the route,
   and rolls back on failure. Development `--reload` is not used as the
   production mechanism.
4. OpenCode activation validates schema/effective catalog and normally relies on
   its file watcher. Explicit reload is permitted as a version-tested action but
   success output alone is not proof; effective `/api/model` state is checked.

## Consequences

The one-time LiteLLM include authorization is unavoidable but per-model edits are
not. A service activation command must be configured for LiteLLM installations.
OpenCode schema changes require an explicit compatibility adapter. Omission never
deletes unowned configuration.

This is the smallest correction to the original integration assumption supported
by the tested versions and preserves the zero-touch model lifecycle intent.
