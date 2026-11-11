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
- **M3 — client/targets:** implemented; conditional polling with bearer auth,
  bounded downloads/backoff, destination locks, validation/staging, atomic replace,
  no-op detection, bounded backups, reactivation rollback, scoped OpenCode merge,
  and LiteLLM include/collision/service-health activation all have automated tests.
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

Run M4 live qualification on Debian `labhost03` and representative Windows hosts:
real llama.cpp/LiteLLM and Ollama inference, model add/change/removal, outage and
restart recovery, transmitted-parameter capture, two actual OS accounts, and
OpenCode/Paseo catalog/variant visibility. Continue M5 implementation that does
not depend on those external environments, but do not claim the M4 gate passed.

## M3 acceptance checklist

- [x] Conditional client polling uses ETags, bounded downloads, bearer auth,
  exponential backoff and jitter.
- [x] Destination-filesystem staging, process lock, flush, atomic replacement and
  bounded backups are implemented.
- [x] Unchanged content produces no write or activation.
- [x] Validation failure leaves the destination untouched.
- [x] Activation failure restores and reactivates last-known-good state.
- [x] OpenCode scoped reconciliation preserves unrelated user/system settings and
  removes only reserved stale provider keys.
- [x] LiteLLM requires the operator include, preserves the primary config, rejects
  alias collisions, parses YAML, and uses constrained service restart/health check.
- [x] Client-only role does not bind a web listener; remote sources require a token
  environment reference.
- [x] 31 automated tests pass on Windows/.NET 10. Cross-platform and real-service
  interruption qualification remains M4.

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
