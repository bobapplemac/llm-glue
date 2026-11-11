# ADR 0015: Config discovery, CLI help, and layered model exclusions

- Status: Accepted for post-r4 development
- Date: 2026-11-11

## Context

Operators commonly use `.toml` for TOML files even though LLM Glue historically
recognized only the `llm-glue.conf` sidecar and legacy `/etc/llm-glue.conf`
fallback. Help switches also attempted to load configuration before producing any
guidance. Finally, exact server publication rules could not express common model
families, and an Agent could not prevent locally discovered inventory from being
submitted upstream.

## Decision

- Explicit `--config` remains highest precedence. Automatic discovery checks
  `llm-glue.conf` then `llm-glue.toml` beside the executable, followed by the
  same names in `/etc/llm-glue` on Linux or `%ProgramData%\LLMGlue` on Windows.
  Legacy `/etc/llm-glue.conf` and `/etc/llm-glue.toml` remain final Linux
  fallbacks. When no candidate exists, the adjacent `.conf` path remains the
  actionable error location.
- `help`, `--help`, `/?`, and `-h` print global usage before configuration is
  resolved.
- `[agent].exclude_models` accepts case-insensitive exact names or `*`/`?`
  patterns. It matches model ID, display name, route, `provider/model`, or
  `provider/instance/model` and removes matches before local publication or
  remote submission. The filter also precedes optional inference probing, so an
  excluded dormant model cannot be loaded by `allow-load`. Existing matches are
  removed immediately rather than held by outage-removal grace.
- `[[server.exclusions]].model_pattern` uses the same matching rules. It composes
  with existing exact fields using AND semantics and suppresses only generated
  publication; the deployment remains in server diagnostics with its rule.

## Consequences

Both conventional TOML filenames work without weakening explicit configuration
selection. Help works on an unconfigured installation. Local policy can prevent
inventory disclosure, while central policy can hide models without losing
diagnostic visibility. Existing exact server rules and `.conf` installations
remain behaviorally compatible.
