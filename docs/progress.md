# Progress

Last updated: 2026-11-11

## Current status

- **M0 — evidence/interface validation:** implementation design complete;
  executable tests completed for OpenCode 2.2.7, LiteLLM 1.109.0, and Ollama
  0.40.16. Live llama.cpp/model and multi-account qualification deferred to M4
  and explicitly fail closed.
- **M1 — core domain/discovery:** implemented; build and 12 fixture/unit tests
  pass on .NET 10. Includes strict TOML role composition, provenance-aware domain
  types, safe GGUF metadata parsing, Ollama and llama.cpp adapters, and stable
  inventory hashes. Configured llama.cpp directories now enrich only exact,
  provider-confirmed GGUF routes; full-file hashing is opt-in. Live Ollama API
  shape was additionally confirmed in M0. Opt-in functional text-route probes are
  timeout/size bounded, preserve unknown non-text capabilities, and never load a
  dormant model under `loaded-only` policy.
- **M2 — server/generation:** implemented; reconciliation grace/conflict handling,
  deterministic OpenCode 2.x and LiteLLM generation, validation, bounded artifact
  history, authenticated `/api/v1` publication, diagnostics and ETags. Golden and
  real Kestrel tests pass; OpenCode 2.2.7 loaded the generated artifact and retained
  the exact verified variant body without inventing modes. Exact scoped exclusions
  retain diagnostic visibility while filtering both generated artifacts. Catalog
  state and artifacts are transactionally persisted and hash-validated on restart.
- **M3 — client/targets:** implemented; conditional polling with bearer auth,
  bounded downloads/backoff, destination locks, validation/staging, atomic replace,
  no-op detection, bounded backups, reactivation rollback, scoped OpenCode merge,
  and LiteLLM include/collision/service-health activation all have automated tests.
  Agent-only role composition now submits canonical snapshots through the remote
  write-auth boundary; delayed snapshots cannot replace newer Server state.
- **M4 — live end-to-end:** partial. Live Windows Ollama discovery → Server →
  Client → isolated OpenCode 2.2.7 catalog visibility passed for two models.
  An elevated all-role Windows service run also passed LocalService identity,
  health, relocation/repair, restart, service rollback, and uninstall cleanup.
  Functional inference and the Debian llama.cpp/LiteLLM, multi-account, restart,
  change/removal, parameter-capture, and Paseo paths remain blocked on unavailable
  environments. See `docs/m4-qualification.md`.
- **M5 — release hardening:** implementation substantially complete: dual-RID
  self-contained single files, runtime-derived rN version identity, interactive
  bootstrap installers, SCM and
  systemd lifecycle commands, portable console/rolling-file logs, doctor and
  operational CLI, checksum-verified stable updates/rollback, guarded equivalent
  release scripts, packaging manifests, CI, and operator/developer docs. Native
  Windows and WSL builds/tests pass. Isolated Bash/PowerShell release matrices now
  qualify fail-closed Git states and a local-only partial-publication recovery path.
  Live service install/update/rollback and an authenticated clean-pushed release
  preflight remain unqualified; publication is not authorized. Unknown reasoning
  capability excludes a deployment from client publication rather than being
  converted to `false`.
  Service registration now provisions stable least-privilege identities and
  verifies exact executable, sidecar, state, log, and target access before
  registering the service. Elevated Windows all-role lifecycle and rollback now
  pass; native Ubuntu/WSL systemd install/repair/rollback/uninstall also passes.
  Both bootstrap scripts now cover remote-client, standalone Ollama, and central
  llama.cpp/LiteLLM modes, validate before privileged copying, protect generated
  sidecars, and preserve existing configuration. Isolated mocked-release matrices
  execute and validate every generated mode on Windows and Linux.
  Non-loopback Server configurations now enforce bearer authentication even for
  same-host proxy peers; the bootstrap central Client receives the read token.

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
- [x] Exact-route GGUF enrichment canonicalizes allowlisted/symlinked paths,
  records architecture/context/type/multipart metadata, never promotes file-only
  models, and makes expensive full-file hashing explicit opt-in.
- [x] Ollama tags/show/ps/version adapter with explicit thinking categories.
- [x] llama.cpp direct/router/property adapter without treating files as routes.
- [x] Opt-in loaded-only/allow-load inference probes verify only observed text
  routing with bounded timeout/output; failures exclude the route without leaking
  response bodies or fabricating reasoning/tool/sampling support.
- [x] Deterministic, content-hashed discovery snapshots.
- [x] Validated Linux, Windows all-in-one, and remote-client TOML examples.
- [x] Automated tests pass; live model inference remains an M4 gate.

## Next steps

Run M4 live qualification on Debian `labhost03` and representative Windows hosts:
real llama.cpp/LiteLLM and Ollama inference, model add/change/removal, outage and
restart recovery, transmitted-parameter capture, two actual OS accounts, and
OpenCode/Paseo catalog/variant visibility. Then exercise the implemented M5
service install/move/update/rollback/uninstall flows on both OSes and the guarded
release preflight from a clean pushed branch. Do not publish without explicit
authorization or claim either live gate passed beforehand.

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

## M5 acceptance checklist

- [x] Product remains r0 with Assembly/File `0.0.0.0`; official release build
  overrides map `rN` to Assembly `1.0.0.0` and File `1.N.0.0`. An r1 qualification
  publish reported `LLM Glue r1` and Windows FileVersion `1.1.0.0`.
- [x] Self-contained single-file win-x64 and linux-x64 outputs build; both execute
  and report `LLM Glue r0` (Windows host and Ubuntu/WSL respectively).
- [x] GNU Make help, native Linux restore/build/test, Linux script syntax, r0
  packaging, checksum verification, and commit/version manifest pass.
- [x] PowerShell installer/release scripts parse successfully; dirty worktree
  preflight fails closed before any remote mutation.
- [x] Bash and PowerShell bootstrap matrices use real published binaries with
  mocked pinned release responses to verify checksums, all three configuration
  modes, protected sidecars, rerun preservation, and executable rollback.
- [x] Windows SCM/systemd install, repair, start, stop, status and uninstall are
  implemented with fixed tools, stable LocalService/`llm-glue` identities,
  path-specific permissions, and preservation defaults; install has a verified
  non-mutating dry-run on both platforms.
- [x] Explicit update check/apply/rollback accepts pinned stable releases and
  verifies checksums; failed checks do not affect sync. Explicit service mode
  coordinates restart/health verification and automatically restores the fallback
  after failed or interrupted activation; fault-injection tests pass on both OSes.
- [x] Console plus bounded rolling-file logs use standard logging abstractions and
  avoid prompt/request bodies and credentials. Sanitized request correlation IDs
  are returned and scoped into logs; each discovery/sync cycle gets its own ID.
- [x] CI, installation, quick-start, troubleshooting, security, release, ADR and
  v2 roadmap documentation are present.
- [x] Isolated Bash and PowerShell preflight matrices cover clean, dirty, ahead,
  behind, detached, collision, ambiguous/wrong remote, and unavailable-remote
  states without GitHub mutation; Bash also verifies partial-failure guidance.
- [x] Live Windows SCM install/start/health/move/repair/restart/service rollback/
  uninstall passes under LocalService with isolated all-role paths.
- [x] Native Ubuntu/WSL systemd install/start/health/move/repair/restart/service
  rollback/uninstall and preservation pass under the fixed `llm-glue` identity.
- [ ] Live update-apply from an official release asset; none exists without release
  authorization. Windows service rollback is live-qualified.
- [ ] Authenticated preflight against the actual clean, already-pushed upstream
  with GitHub CLI available. No publication authorized; isolated Git-state
  success/failure matrices already pass.
- [ ] M4 live Debian/inference and multi-user gates; M5 cannot be declared passed
  until the release's required live suite passes.

Latest available verification: clean Windows Release build and 69 tests; native
Ubuntu/WSL Release restore/build and 69 tests; PowerShell and Bash bootstrap
matrices validate all three modes with real
single-file binaries; r1 win-x64/linux-x64 execution and Windows/Linux service
install dry runs pass. These are implementation/packaging evidence, not substitutes
for the unchecked live gates above.
