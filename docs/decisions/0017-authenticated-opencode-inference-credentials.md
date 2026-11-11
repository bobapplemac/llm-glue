# ADR 0017: Authenticated OpenCode inference credentials

- Status: Accepted for post-r4 development
- Date: 2026-11-11

## Context

Central OpenCode artifacts previously referenced
`LLM_GLUE_INFERENCE_API_KEY`. Every workstation therefore needed both an LLM
Glue read token and a separately installed LiteLLM key, and interactive desktop
applications had to inherit the correct user environment. The read token already
authorizes download of the complete OpenCode catalog.

## Decision

- A central server embeds the resolved LiteLLM client key directly in each
  generated OpenCode provider by default.
- `[server].opencode_inference_api_key` or
  `opencode_inference_api_key_environment` supplies the key. Exactly one source
  is allowed. A central route fails configuration validation without one.
- `embed_opencode_inference_api_key = false` is the explicit opt-out. It emits
  `{env:LLM_GLUE_INFERENCE_API_KEY}` plus the provider `env` declaration and does
  not accept a server-side inference-key source.
- The key is permitted only in the authenticated OpenCode artifact, retained
  artifact state, and applied OpenCode JSONC. It remains forbidden from LiteLLM
  YAML, diagnostics, inventory/status/health responses, logs, fixtures containing
  real credentials, and unauthenticated endpoints.
- Remote-client bootstrap asks only for the LLM Glue URL/read token by default.
  Central bootstrap collects the inference key. Existing configs are preserved,
  so an upgrade requires the operator to add the new source or opt out.

## Consequences

Client provisioning becomes single-token and works for OpenCode/Paseo without
environment inheritance. The LLM Glue read token can now retrieve the inference
key, so it must be treated as an inference credential; protected transport,
restrictive server state/target permissions, and coordinated rotation are
mandatory. Operators needing separate credential distribution retain the prior
environment-reference behavior.
