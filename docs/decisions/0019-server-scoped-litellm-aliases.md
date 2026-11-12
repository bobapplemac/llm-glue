# ADR 0019: Server-scoped LiteLLM aliases

- Status: Accepted for post-r4 development
- Date: 2026-11-11

## Context

LiteLLM aliases previously repeated runtime provider and instance identity, such
as `llama-cpp-labhost03-router-bench-01-01`. That provenance is useful in
diagnostics but unnecessarily verbose as a gateway route. OpenCode now has a
separate concise presentation namespace, so its visible identity does not need to
equal the LiteLLM route.

## Decision

- LiteLLM aliases are `<server-namespace>-<model-id>`.
- `server.litellm_namespace` configures the prefix. When omitted, LLM Glue
  lowercases and hyphen-normalizes the local machine hostname, limited to 64
  characters. An unusable hostname falls back to `llm-glue`.
- Central OpenCode uses the same alias as the model's internal `modelID`, while
  its visible provider/model key remains controlled independently by
  `opencode_namespace` and its overrides.
- Direct OpenCode/Ollama routing continues to use the provider route and does not
  use a LiteLLM alias.
- Duplicate normalized aliases in one LiteLLM namespace fail generation. LLM
  Glue does not append source-dependent suffixes whose appearance could rename an
  existing route.

## Consequences

Aliases identify the serving LLM Glue host without exposing runtime adapter
details. Operators should pin `litellm_namespace` before moving a sidecar between
hosts when route continuity matters. Changing it is an intentional route rename
that must update the generated LiteLLM include and OpenCode artifact together.
