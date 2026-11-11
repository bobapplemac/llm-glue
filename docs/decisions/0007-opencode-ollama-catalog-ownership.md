# ADR 0007: Direct Ollama owns one enriched OpenCode catalog

## Context

OpenCode 2.2.7 automatically discovers a local Ollama server. LLM Glue must add
verified context, capability, and reasoning profiles, but a separate custom
provider otherwise displays every local model twice. The specification explicitly
forbids duplicate native and generated catalog entries.

Live comparison showed the built-in entries had correct native context but no
reasoning variants. An isolated OpenCode run proved that the documented
`disabled_providers: ["ollama"]` setting removes only the built-in provider while
the generated `llm-glue-ollama-*` provider remains functional.

## Decision

A direct OpenCode artifact containing Ollama deployments disables the built-in
`ollama` provider. Central LiteLLM artifacts do not: a remote catalog must not
disable an unrelated Ollama installation on the client.

The OpenCode target merges the disabled-provider list rather than replacing it.
It records only values it actually added in an adjacent generated ownership file,
under the same destination-scoped process lock as reconciliation. When the last
direct Ollama deployment disappears, LLM Glue removes only its owned disablement.
Pre-existing and unrelated user values survive. Apply or activation failure
restores both the target document and ownership state.

## Consequences

Standalone users see one enriched model entry rather than native and generated
duplicates. The extra ownership file is machine-generated operational state, not
a second human-managed configuration format. Corrupt ownership state fails closed
before changing the target.
