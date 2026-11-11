# ADR 0014: Router refresh and unknown-capability publication

- Status: Accepted for r4
- Date: 2026-11-11

## Context

The live labhost03 llama.cpp router retained a cached ten-model inventory after
an eleventh preset was added to `models.ini`. Its documented
`GET /models?reload=1` operation refreshed the presets, ran the router's bounded
metadata measurement children, and returned all eleven configured routes without
loading dormant models for inference.

LLM Glue r3 requested only `GET /models`. It also required verified text routing
and a non-unknown reasoning mode before either OpenCode or LiteLLM generation.
With `probe_mode = "loaded-only"`, this made catalog membership depend on which
model happened to be resident. It contradicted the zero-registration and
default-exposure requirements, and converted unavailable evidence into omission.

## Decision

- llama.cpp discovery requests `GET /models?reload=1`. Direct servers may ignore
  the query while routers refresh their configured inventory.
- Provider discovery timeout is configurable as `discovery_timeout_seconds` and
  defaults to 300 seconds because the first metadata sweep after a preset change
  can inspect large GGUFs serially.
- Agent discovery runs once at service start and waits 60 seconds after each
  completed cycle by default. This avoids aggressive router metadata polling for
  an inventory that changes infrequently; operators can configure the interval.
- Available/degraded, endpoint-backed deployments are publishable unless text
  input or output is explicitly reported unsupported. Unknown is not unsupported.
- OpenCode generation omits unknown modality, tool, and reasoning declarations.
  It never writes `false` for unknown support and emits only verified reasoning
  variants.
- `loaded-only` continues to govern active inference probes only. It never
  governs catalog membership and never wakes a dormant route.

## Consequences

Adding a router preset becomes zero-touch after the next discovery pass, including
for a dormant model. LiteLLM receives the route and OpenCode can select it, while
reasoning/tool claims remain absent until evidence exists. Refresh can take longer
than an ordinary cached inventory read, but it is bounded and operator-tunable.

Live qualification with llama.cpp `0.9.0-dev` refreshed labhost03 from ten to
eleven routes. The r4 candidate discovered and generated both benchmark routes;
OpenCode 2.2.7 listed all eleven models while `bench-01-02` remained unloaded and
carried no invented reasoning declaration.
