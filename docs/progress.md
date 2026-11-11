# Progress

Last updated: 2026-11-10

## Current status

- **M0 — evidence/interface validation:** implementation design complete;
  executable tests completed for OpenCode 2.2.7, LiteLLM 1.109.0, and Ollama
  0.40.16. Live llama.cpp/model and multi-account qualification deferred to M4
  and explicitly fail closed.
- **M1 — core domain/discovery:** implemented; build and 12 fixture/unit tests
  pass on .NET 10. Includes strict TOML role composition, provenance-aware domain
  types, safe GGUF metadata parsing, Ollama and llama.cpp adapters, and stable
  inventory hashes. Live Ollama API shape was additionally confirmed in M0.
- **M2 — server/generation:** implemented; reconciliation grace/conflict handling,
  deterministic OpenCode 2.x and LiteLLM generation, validation, bounded artifact
  history, authenticated `/api/v1` publication, diagnostics and ETags. Golden and
  real Kestrel tests pass; OpenCode 2.2.7 loaded the generated artifact and retained
  the exact verified variant body without inventing modes.
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

## M1 acceptance checklist

- [x] Composable role/provider/target TOML parsing with strict unknown-key and
  remote-listener security validation.
- [x] Domain types preserve tri-state capabilities and provenance.
- [x] Native and effective context are distinct.
- [x] Safe GGUF v2/v3 metadata reader enforces directory and allocation bounds.
- [x] Ollama tags/show/ps/version adapter with explicit thinking categories.
- [x] llama.cpp direct/router/property adapter without treating files as routes.
- [x] Deterministic, content-hashed discovery snapshots.
- [x] Validated Linux, Windows all-in-one, and remote-client TOML examples.
- [x] Automated tests pass; live model inference remains an M4 gate.

## Next steps

Implement M3 polling, staging, destination-filesystem atomic writes, locking,
backup retention and rollback, plus scoped OpenCode reconciliation and LiteLLM
include/restart targets with failure recovery tests.

## M2 acceptance checklist

- [x] Missing deployments become unavailable during grace and are removed only
  after expiry; ambiguous routes fail closed as invalid.
- [x] OpenCode 2.x output has explicit capabilities, effective context, and only
  transport-verified request-body variants.
- [x] LiteLLM output owns only `model_list`, uses stable aliases and environment
  secret references.
- [x] Golden JSON/YAML are deterministic across platform newlines.
- [x] Generated OpenCode artifact accepted by OpenCode 2.2.7 effective model API.
- [x] Versioned health/inventory/models/artifact/status/Agent endpoints exist.
- [x] ETags and 304 behavior verified through a real loopback Kestrel server.
- [x] Remote bearer checks distinguish read and write credentials and use fixed-time
  comparison; loopback authentication remains optional.
- [x] Artifact updates validate before publication and retain bounded history.
