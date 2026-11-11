# ADR 0010: Explicit OpenCode target scope

- Status: Accepted for r0 implementation
- Date: 2026-11-11

## Context

OpenCode reads system, user, and project configuration with system settings taking
precedence. A Client running as a service cannot safely infer which interactive
users should receive a generated catalog, and enumerating every profile would
expand LLM Glue's ownership without operator intent. A machine may nevertheless
need one system catalog or the same catalog in several selected user profiles.

## Decision

- The operator selects scope by configuring each destination path explicitly.
  `C:\ProgramData\opencode\opencode.jsonc` is the default Windows system target; a path
  below a selected profile is a user target.
- Multiple `[[client.targets]]` entries of type `opencode` are supported. The
  Client downloads the OpenCode artifact once per polling cycle and applies it
  independently to every configured destination.
- LLM Glue does not enumerate profiles and does not derive an interactive user
  from the service identity. Duplicate normalized destination paths are rejected.
- A failed destination does not prevent other destinations from applying. The
  shared ETag advances only after every destination succeeds, so failures retry
  without leaving a selected user permanently behind.
- Every destination has its own adjacent ownership document. Existing JSON/JSONC
  is merged; only provider keys and native-provider disablements recorded for the
  artifact source are updated or removed. Unowned collisions fail closed.
- New installations standardize on JSONC. Reconciliation preserves comments on
  unowned syntax and emits managed-section comments; explicit legacy `.json`
  destinations remain compatible.

## Consequences

Global and selected-user deployments are both deterministic and auditable. The
operator must list each selected user path and grant the service identity access
to it. Configuring both system and user targets is allowed, but overlapping keys
will resolve according to OpenCode's system precedence and therefore normally
adds no value.
