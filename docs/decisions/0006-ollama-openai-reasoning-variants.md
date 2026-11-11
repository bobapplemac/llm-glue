# ADR 0006: Ollama variants use OpenAI reasoning effort

## Context

Ollama reports model-defined thinking values through `/api/show`, but LLM Glue's
direct OpenCode route uses Ollama's OpenAI-compatible `/v1` API. Native `think`
request fields cannot be assumed to traverse that path. Initial implementation
therefore advertised the capability without named request profiles.

Ollama 0.40.16's official compatibility contract defines `reasoning_effort` for
chat completions. A live Qwen 3.5 4B probe showed that `none` produced no reasoning
and `medium` produced a separate reasoning trace. OpenCode 2.2.7 also accepted and
executed the resulting named variant.

## Decision

For Ollama 0.40.16 or newer, boolean thinking metadata produces two
transport-verified profiles: `thinking-off` sends `reasoning_effort = "none"` and
`thinking-on` sends `reasoning_effort = "medium"`. Model-defined named efforts
produce `reasoning-<name>` with the exact reported name. Models with no reasoning,
unknown metadata, or an older/unparseable runtime version receive no such profiles.

## Consequences

OpenCode receives useful named modes without relying on provider-native fields or
model-name inference. The version gate is deliberately conservative; older Ollama
versions require separate qualification before the mapping can be expanded.
