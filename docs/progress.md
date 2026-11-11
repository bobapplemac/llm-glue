# Progress

Last updated: 2026-11-10

## Current status

- **M0 — evidence/interface validation:** implementation design complete;
  executable tests completed for OpenCode 2.2.7, LiteLLM 1.109.0, and Ollama
  0.40.16. Live llama.cpp/model and multi-account qualification deferred to M4
  and explicitly fail closed.
- **M1 — core domain/discovery:** not started.
- **M2 — server/generation:** not started.
- **M3 — client/targets:** not started.
- **M4 — live end-to-end:** blocked on access to Debian `labhost03`, representative
  llama.cpp model files, LiteLLM service, and additional OS accounts.
- **M5 — release hardening:** not started. Publication is not authorized.

## M0 acceptance checklist

- [x] Read authoritative specification and inspect repository/toolchain.
- [x] Record exact tested/current integration versions.
- [x] Verify OpenCode locations, layering, effective model precedence, and reload
  behavior with isolated profiles.
- [x] Verify LiteLLM include syntax, merge ownership, and reload behavior.
- [x] Probe live Ollama discovery and explicit thinking metadata.
- [x] Inspect current llama.cpp documented direct/router/property APIs.
- [x] Resolve destructive merge, ownership, stale cleanup, and activation semantics.
- [x] Define discovery/verification/unknown behavior for every v1 field.
- [x] Define mandatory-remote/optional-loopback authentication threat model.
- [x] Select TOML parser and strict validation approach.
- [x] Add sanitized fixtures/reference transcripts.
- [ ] Qualify actual llama.cpp/Gemma/GPT-OSS/MTP request paths (M4 environment).
- [ ] Repeat OpenCode system precedence with two actual OS accounts (M4).

## Key decisions

- OpenCode is version-gated; 2.x uses `providers` and explicit capabilities to
  prevent fabricated variants.
- LiteLLM generated files contain only `model_list`; primary YAML is never replaced.
- LiteLLM production activation uses configured restart plus health/rollback, not
  development auto-reload.
- Capability unknowns are preserved and excluded from advertised modes.

## Next steps

Implement M1 domain types, TOML configuration loader, safe GGUF reader, Ollama and
llama.cpp adapters, deterministic snapshots, and unit/fixture tests. Then build and
commit the milestone before starting server generation.
