# Progress

Last updated: 2026-11-11

The requirement-level gate audit is maintained in `docs/completion-audit.md`.

## Current status

- **M0 — evidence/interface validation:** implementation design complete;
  executable tests completed for OpenCode 2.2.7, LiteLLM 1.106.3/1.109.0,
  Ollama 0.40.16, and llama.cpp direct/router builds. Two-account Linux
  precedence, Windows 2.2.7 system-level JSONC discovery/comment parsing, and
  representative central discovery are live-qualified; Windows
  multi-account remains deferred to M4.
- **M1 — core domain/discovery:** implemented; the full 103-test suite
  pass on .NET 10. Includes strict TOML role composition, provenance-aware domain
  types, safe GGUF metadata parsing, Ollama and llama.cpp adapters, and stable
  inventory hashes. Configured llama.cpp directories now enrich only
  provider-confirmed GGUF routes selected by an allowlisted reported path or exact
  filename; full-file hashing is opt-in. A live native Ubuntu/WSL run enriched an
  aliased route without publishing its path, while outside-allowlist tests fail
  closed. Positive GGUF `nextn_predict_layers` is now recorded as MTP runtime
  metadata without inventing a client mode, and versioned llama.cpp `/props`
  shapes report effective speculative state. Live build 8681 reported
  speculation disabled. Live Ollama API shape was additionally confirmed in M0. Opt-in functional text-route probes are
  timeout/size bounded, preserve unknown non-text capabilities, and never load a
  dormant model under `loaded-only` policy. Current router `data[]` status shapes,
  explicit container-to-host model path mappings, and behaviorally verified
  binary llama.cpp reasoning profiles are covered by live evidence and tests.
- **M2 — server/generation:** implemented; reconciliation grace/conflict handling,
  deterministic OpenCode 2.x and LiteLLM generation, validation, bounded artifact
  history, authenticated `/api/v1` publication, diagnostics and ETags. Golden and
  real Kestrel tests pass; OpenCode 2.2.7 loaded the generated artifact and retained
  the exact verified variant body without inventing modes. Exact scoped exclusions
  retain diagnostic visibility while filtering both generated artifacts. Catalog
  state and artifacts are transactionally persisted and hash-validated on restart.
  Central OpenCode artifacts now fail closed unless configured with a
  client-reachable LiteLLM URL, and use LiteLLM's stable alias rather than a
  private llama.cpp route.
- **M3 — client/targets:** implemented; conditional polling with bearer auth,
  bounded downloads/backoff, destination locks, validation/staging, atomic replace,
  no-op detection, bounded backups, reactivation rollback, scoped OpenCode merge,
  and LiteLLM include/collision/service-health activation all have automated tests.
  Agent-only role composition now submits canonical snapshots through the remote
  write-auth boundary; delayed snapshots cannot replace newer Server state.
  OpenCode ownership schema v2 scopes provider keys and native-provider
  disablements by artifact source, migrates v1 state, and fails closed on
  unowned/cross-source collisions. Explicit system or user paths are supported;
  repeated OpenCode targets fetch one artifact and apply independently, with the
  shared ETag advancing only after every selected destination succeeds. New
  targets default to JSONC; unowned comments survive reconciliation and managed
  groups are explicitly marked without duplicating comments on repeated sync.
- **M4 — live end-to-end:** partial. Live Windows Ollama discovery → Server →
  Client → isolated OpenCode 2.2.7 catalog visibility passed for two models.
  An elevated all-role Windows service run also passed LocalService identity,
  health, relocation/repair, restart, service rollback, and uninstall cleanup.
  Native Ollama Off/On inference, effective/native context separation, full-VRAM
  residency, and loaded-only route probing passed for Qwen 3.5 4B. The installed
  OpenCode 2.2.7 selected a generated `thinking-on` variant and completed real
  native inference; direct OpenAI-compatible probes verified the generated
  `reasoning_effort` Off/On bodies. A repeat run suppressed OpenCode's duplicate
  built-in Ollama entry while retaining the enriched route. An isolated secondary
  native Ollama instance then passed real add/change/remove propagation,
  application restart, transient outage/grace/recovery, empty-catalog cleanup,
  and native-provider restoration. The actual Windows user-profile target was
  then synchronized from its initially empty config directory: installed
  OpenCode exposed all three local models without native duplicates and returned
  the expected marker through a generated `thinking-off` route. Two actual Linux
  accounts also passed system precedence and unrelated user-setting preservation.
  A real Ubuntu/WSL llama.cpp 8681 + SmolLM2 route exposed and corrected current
  direct `/models` alias handling, then passed native/effective context discovery,
  version capture, allowlisted single-file GGUF enrichment, and bounded inference.
  A manually isolated downstream route
  also passed LiteLLM 1.109.0 forwarding and OpenCode-to-backend transport. The
  production generator correctly excluded the model because reasoning remained
  unknown, and the deliberately small model did not produce a terminal OpenCode
  task response. The later Debian 13.7 `labhost03` run qualified the actual
  llama.cpp `0.9.0-dev` router, LiteLLM 1.106.3 include/restart/rollback, Qwen MTP,
  Gemma 4, GPT-OSS, multipart GGUF, context/KV/GPU/speculative observation,
  authenticated Windows OpenCode Off/On inference, add/change/remove propagation,
  transient outage/grace/recovery, and live fail-once activation rollback. GPT-OSS
  reasoning-off remains unknown because `reasoning_effort=none` still emitted
  reasoning. Paseo 0.15.4 displayed the generated central and local model IDs and
  exposed `Thinking off`/`Thinking on` for the selected central route. Windows
  multi-account/system-target service remains open. The lab Compose path now runs
  as the fixed `llm-glue` account: a polkit rule permits only restart of the exact
  root-owned activation unit, an unrelated-unit restart was denied, real sync and
  health passed, and authenticated LiteLLM-to-llama.cpp inference survived the
  handoff from the temporary root process.
  Real OpenCode 2.2.7 through a real LiteLLM 1.109.0 proxy and a
  captured mock upstream verified stable alias routing, streamed response flow,
  environment-backed client/backend authentication, `temperature`, and the binary
  `enable_thinking` field.
  See `docs/m4-qualification.md`.
- **M5 — release hardening:** implementation substantially complete: dual-RID
  self-contained single files, runtime-derived rN version identity, interactive
  bootstrap installers, SCM and
  systemd lifecycle commands, portable console/rolling-file logs, doctor and
  operational CLI, checksum-verified stable updates/rollback, guarded equivalent
  release scripts, packaging manifests, CI, and operator/developer docs. Native
  Windows and WSL builds/tests pass. A minimal Debian 13.5 environment exposed
  and led to removal of the Linux binary's undeclared ICU dependency; the
  invariant-globalization binary and full fixed-account systemd lifecycle now
  pass there, and CI reruns the binary/config check in `debian:13-slim`.
  Isolated Bash/PowerShell release matrices now
  qualify fail-closed Git states and a local-only partial-publication recovery path.
  Live official-release update and an authenticated clean-pushed release preflight
  remain unqualified; publication is not authorized. Unknown reasoning
  capability excludes a deployment from client publication rather than being
  converted to `false`.
  Service registration now provisions stable least-privilege identities and
  verifies exact executable, sidecar, state, log, and target access before
  registering the service. Elevated Windows all-role lifecycle and rollback pass
  for the pre-ADR-0007 target; a current LocalService sidecar rerun needs an
  elevated Windows token. Native Ubuntu and Debian 13/WSL all-role systemd
  install/repair/rollback/uninstall pass with ownership-sidecar access verified.
  Both bootstrap scripts now cover remote-client, standalone Ollama, and central
  llama.cpp/LiteLLM modes, validate before privileged copying, protect generated
  sidecars, and preserve existing configuration. Isolated mocked-release matrices
  execute and validate every generated mode on Windows and Linux.
  Non-loopback Server configurations now enforce bearer authentication even for
  same-host proxy peers; the bootstrap central Client receives the read token.
  `doctor` now returns failure for unavailable/non-2xx providers or missing
  configured secret environments; a live local Ollama check returned zero while
  the unavailable llama.cpp example returned one.

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
- [x] Qualify actual representative llama.cpp/Qwen/Gemma/GPT-OSS/MTP paths;
  unsupported GPT-OSS Off behavior remains unknown rather than weakened.
- [x] Repeat OpenCode system precedence with two actual Linux OS accounts (M4);
  Windows multi-account qualification remains open.

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
- [x] Opt-in loaded-only/allow-load inference probes verify observed text routing
  with bounded timeout/output. Loaded llama.cpp routes additionally publish
  binary reasoning only after distinct `none`/`medium` behavior; failures and
  ambiguous results remain unknown without leaking response bodies.
- [x] Deterministic, content-hashed discovery snapshots.
- [x] Validated Linux, Windows all-in-one, and remote-client TOML examples.
- [x] Automated tests pass; representative live model inference is M4-qualified.

## Next steps

Complete the remaining Windows multi-account/system-target checks. Then
exercise the implemented M5
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
  removes only stale keys owned by the same artifact source. Direct Ollama
  synchronization ownership-tracks and reversibly suppresses the duplicate native
  provider; local and central controllers coexist through ownership schema v2.
- [x] LiteLLM requires the operator include, preserves the primary config, rejects
  alias collisions, parses YAML, and uses constrained service restart/health check.
- [x] Client-only role does not bind a web listener; remote sources require a token
  environment reference.
- [x] Targeted client tests plus the full 103-test suite pass on Windows/.NET 10;
  real LiteLLM interruption and rollback are live-qualified.

## M2 acceptance checklist

- [x] Missing deployments become unavailable during grace and are removed only
  after expiry; ambiguous routes fail closed as invalid.
- [x] OpenCode 2.x output has explicit capabilities, effective context, and only
  transport-verified request-body variants.
- [x] LiteLLM output owns only `model_list`, uses stable aliases and environment
  secret references.
- [x] LiteLLM 1.109.0 executable forwarding test verifies `openai/` route mapping,
  stable alias resolution, response flow, standard parameters, binary
  `enable_thinking`, and backend auth; the representative llama.cpp reasoning
  path is now separately live-qualified with `reasoning_effort`.
- [x] OpenCode 2.2.7 noninteractive forwarding through LiteLLM verifies custom
  provider activation, the generated credential reference and alias, streamed
  response flow, and `thinking-on` body transport against a captured upstream.
- [x] Golden JSON/YAML are deterministic across platform newlines.
- [x] Generated OpenCode artifact accepted by OpenCode 2.2.7 effective model API.
- [x] Versioned health/inventory/models/artifact/status/Agent endpoints exist.
- [x] ETags and 304 behavior verified through a real loopback Kestrel server.
- [x] Remote bearer checks distinguish read and write credentials and use fixed-time
  comparison; loopback authentication remains optional.
- [x] Artifact updates validate before publication and retain bounded history.

## M5 acceptance checklist

- [x] Product and all development/qualification outputs remain r0 with
  Assembly/File `0.0.0.0`; guarded official release overrides map `rN` to
  Assembly `1.0.0.0` and File `1.N.0.0`. Make/package entry points reject a
  nonzero revision unless invoked by the guarded release path.
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
  rollback/uninstall and preservation pass under the fixed `llm-glue` identity;
  the current all-role run also verifies managed OpenCode ownership-sidecar access.
- [ ] Rerun the current all-role Windows LocalService harness to qualify ADR 0007
  ownership-sidecar access; this session lacks an elevated Windows token. Earlier
  service lifecycle and rollback behavior remains live-qualified.
- [ ] Live update-apply from an official release asset; none exists without release
  authorization. Windows service rollback is live-qualified.
- [ ] Authenticated preflight against the actual clean, already-pushed upstream
  with GitHub CLI available. No publication authorized; isolated Git-state
  success/failure matrices already pass.
- [ ] Remaining M4 Windows multi-account/system-target gate; M5 cannot be
  declared passed until the
  release's required live suite passes.

Latest available verification: Windows and native Ubuntu/WSL Release builds with
all 103 tests passing;
OpenCode 2.2.7 → LiteLLM 1.109.0 forwarding with a captured upstream;
native OpenCode 2.2.7 → Ollama 0.40.16 real-model inference with verified
`thinking-off`/`thinking-on` variants and no duplicate native Ollama catalog row;
the installed default OpenCode profile was rechecked after the latest discovery
fix and returned `LLM_GLUE_CURRENT_OK` through its generated `thinking-off` route;
isolated native Ollama add/change/remove, application restart, outage/grace/recovery,
and native-provider restoration;
Debian 13.7 llama.cpp Qwen/Gemma/GPT-OSS central inference, generated LiteLLM
include activation/rollback, catalog lifecycle, and MarkIV OpenCode Off/On runs;
two-account Linux system precedence;
PowerShell and Bash bootstrap
matrices validate all three modes with real
single-file binaries; r0 win-x64/linux-x64 execution and Windows/Linux service
install dry runs pass. These are implementation/packaging evidence, not substitutes
for the unchecked live gates above.
