# ADR 0018: Concise, configurable OpenCode namespaces

- Status: Accepted for post-r4 development
- Date: 2026-11-11

## Context

OpenCode provider keys previously repeated controller, runtime, and instance
provenance, producing selections such as
`llm-glue-llama-cpp-labhost03-router/bench-01-01`. LiteLLM needs a stable,
collision-safe backend alias, but OpenCode's visible provider namespace does not
need to expose that routing detail.

## Decision

- The Server aggregates all published sources into `opencode_namespace`, default
  `llm-glue`. The provider display name is `LLM Glue` for the default namespace.
- Ordered `[[server.opencode_namespace_overrides]]` entries match exact
  `provider_id` and/or `instance_id` fields. The first match wins and assigns the
  source to the rule's namespace. The instance ID is the Agent provider's
  configured name.
- OpenCode model keys remain the source model IDs. The internal `modelID` remains
  the stable, Server-scoped LiteLLM alias defined by ADR 0019 for central routing.
- A namespace with duplicate model IDs fails generation. A direct-routing
  namespace spanning multiple endpoints also fails because one OpenCode provider
  has only one base URL. Both diagnostics require an explicit namespace split;
  LLM Glue does not create unstable suffixes.
- Provider ownership remains scoped by artifact source and exact generated key.
  An apply replaces a previously owned verbose key transactionally. Separate LLM
  Glue controllers targeting one document must use distinct namespaces.

## Consequences

The common UI identity becomes `llm-glue/model`, while internal LiteLLM routing
stays deterministic. Operators can expose meaningful groups such as `lab/model`
or `workstation/model` without per-model registration. The one-time provider-key
migration may require reselecting a saved model in OpenCode/Paseo.
