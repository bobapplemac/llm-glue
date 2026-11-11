# ADR 0005: Central OpenCode traffic routes through LiteLLM

Status: accepted, 2026-11-11; credential-delivery portion superseded by ADR 0017

## Context

The supported central path is OpenCode/Paseo → LiteLLM → llama.cpp. Early
generation used a deployment's discovery endpoint and route in every OpenCode
artifact. That is correct for standalone Ollama but bypasses LiteLLM for central
llama.cpp. Separately, LiteLLM 1.109.0 parses `openai_like/<model>` configuration
but rejects it at chat-completion runtime as an unmapped provider.

## Decision

`server.opencode_inference_url` is the explicit, client-reachable LiteLLM base URL
for central deployments. When set, OpenCode generation uses this base URL and the
deterministic LiteLLM alias as `modelID`. A Server that also configures a LiteLLM
Client target is invalid without the setting. When omitted, OpenCode retains the
provider's direct endpoint and route for standalone Ollama.

LiteLLM generation uses the supported `openai/<backend-route>` provider with an
explicit `api_base` and environment-backed API key. An opt-in executable test
launches pinned LiteLLM, submits a real proxy request, and captures the upstream
request to guard the boundary.

Central OpenCode provider settings use the documented
`{env:LLM_GLUE_INFERENCE_API_KEY}` substitution for LiteLLM client
authentication. The generated artifact discloses only the fixed environment
variable name, never the credential. The provider also declares that variable in
its `env` list; live OpenCode 2.2.7 execution showed this metadata is required for
the custom provider to be considered routable.

ADR 0017 later changed the default to a literal key in the authenticated
OpenCode artifact for single-token client provisioning. This environment form
remains the explicit server opt-out.

## Consequences

The public and private inference boundaries are explicit, alias collisions remain
deterministic, and central bootstrap requires one additional URL. The URL must be
reachable from remote OpenCode clients and protected according to the operator's
LiteLLM/network policy. Real representative llama.cpp thinking-parameter behavior
remains a live M4 qualification gate.
