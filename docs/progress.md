# Progress

## Post-r4 authenticated OpenCode credential provisioning

- Central servers now embed their configured LiteLLM client key in authenticated
  OpenCode artifacts by default. `opencode_inference_api_key` and its environment
  source form are mutually exclusive; a central configuration fails closed when
  default embedding has no available source.
- `embed_opencode_inference_api_key = false` preserves the prior
  `{env:LLM_GLUE_INFERENCE_API_KEY}` behavior. LiteLLM artifacts and diagnostic
  surfaces remain credential-free.
- Remote-client bootstrap now asks only for the LLM Glue URL/read token. Central
  bootstrap collects the LiteLLM key, and both platform matrices cover generated
  configuration. Existing server sidecars are preserved and require an explicit
  source or opt-out before upgrading.
- ADR 0017, the specification, architecture, installation, compatibility,
  security, README, examples, and agent guidance describe the new trust boundary.
- Verification: `dotnet build` completed with zero warnings/errors; all 134 .NET
  tests passed. Fresh r0 self-contained Windows and Linux binaries passed their
  mocked-release bootstrap matrices, including candidate config validation,
  central key embedding, remote-client single-token setup, existing-sidecar
  preservation, and checksum rejection.

Last updated: 2026-11-11

The requirement-level gate audit is maintained in `docs/completion-audit.md`.

## Current status

- **Post-r4 development:** automatic configuration discovery accepts both
  `llm-glue.conf` and `llm-glue.toml` beside the executable and in platform
  configuration folders while preserving Linux legacy paths. `help`, `--help`,
  `/?`, and `-h` work before configuration loading. Agent-side exact/wildcard
  exclusions prevent model export; server-side `model_pattern` exclusions retain
  diagnostics while filtering generated catalogs. All 131 Release tests pass;
  all four help spellings passed against the built executable, and an adjacent
  `llm-glue.toml` was selected and validated without `--config`. Self-contained
  win-x64 and linux-x64 publishes also pass. No post-r4 release has been
  authorized. The PowerShell bootstrap now defaults fresh service installs to
  `%ProgramData%\LLMGlue` while retaining `%LOCALAPPDATA%\LLMGlue` for
  non-service installs. It preserves detected legacy per-user services and
  existing `.toml` sidecars; the mocked bootstrap matrix passes.

- **Official release:** r4 is published from commit
  `342f40052f5b85609f310c89b8fe10ee5c77534c`. The guarded publisher passed
  all 114 tests, built both self-contained RIDs with FileVersion `1.4.0.0`, and
  published exact-commit manifest/checksum metadata. GitHub's asset digests match
  the local SHA-256 values. llama.cpp discovery now refreshes router presets,
  publishes dormant configured routes independently of loaded-only probes, and
  omits unknown OpenCode capability claims. A configurable 300-second discovery
  timeout covers changed-preset metadata sweeps. Live labhost03 qualification
  refreshed ten routes to eleven; the candidate discovered and generated all
  eleven, including unloaded `bench-01-02` at context 262144. OpenCode 2.2.7
  loaded the generated artifact and listed all eleven models. The candidate also
  bundles the PowerShell 5.1 bootstrap fix, automatic Windows service start,
  optional inference-key persistence, and owned built-in OpenCode catalog policy.
  The official r3→r4 self-update then passed on labhost03 with staged activation,
  authenticated health, and rollback protection. The restarted service reported
  11 discovered and 11 published models; the generated LiteLLM include activated
  after its expected cold restart, `/health/liveliness` recovered, and the
  authenticated LiteLLM catalog contained the `bench-01-02` alias.

- **Previous official release:** r3 is published from commit
  `9d29eca4cf0e6c4eb177f65e115c2d91672a08d5`. Its guarded local PowerShell
  publisher passed all 107 tests, built both self-contained RIDs, and emitted
  LF-only checksums and manifest metadata linked to the exact commit. GitHub's
  asset digests match the published checksums. The authorized preflight first
  exposed and corrected a multiline-tag parsing defect before any remote state
  was created. The labhost03 r2→r3 post-exit update then reported success;
  authenticated r3 health, `doctor`, ten-deployment llama.cpp discovery, and
  LiteLLM health passed. Regeneration triggered the host's 45–50 second LiteLLM
  cold start and r3's expanded bounded activation window passed it without the
  r2 rollback loop. MarkIV OpenCode 2.2.7 selected `thinking-on` through the
  r3-managed route and returned the exact live marker `OPENCODE_R3_OK`.
  The first subsequent Windows PowerShell 5.1 bootstrap exposed that its
  `Set-Content` does not recognize the PowerShell 7 `utf8NoBOM` encoding name.
  The main-branch bootstrap now uses the cross-version .NET UTF-8 encoder and
  its matrix explicitly rejects a generated BOM. That live install also exposed
  that the Windows bootstrap registered but did not start the automatic service;
  it now matches Linux by starting after successful registration, with a matrix
  guard for the install/start sequence.
  Remote-client bootstrap now also offers an optional non-echoed LiteLLM key
  prompt and persists a supplied `LLM_GLUE_INFERENCE_API_KEY` only in the current
  Windows user's environment, leaving existing values untouched on blank input.
  OpenCode targets now default to `deny_builtin_opencode_provider = true`, adding
  the current `provider.use` deny policy for the built-in `opencode` catalog.
  Ownership schema v3 migrates v1/v2 state, preserves identical operator-owned
  and unrelated policies/comments, and reverses only a statement LLM Glue added.

- **M0 — evidence/interface validation:** implementation design complete;
  executable tests completed for OpenCode 2.2.7, LiteLLM 1.106.3/1.109.0,
  Ollama 0.40.16, and llama.cpp direct/router builds. Two-account Linux
  precedence, Windows 2.2.7 system-level JSONC discovery/comment parsing, and
  representative central discovery are live-qualified; the later M4 run also
  qualified LocalService and a temporary second native Windows account.
- **M1 — core domain/discovery:** implemented; the full 114-test suite
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
- **M4 — live end-to-end:** passed. Live Windows Ollama discovery → Server →
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
  exposed `Thinking off`/`Thinking on` for the selected central route. A
  reversible MarkIV probe proved that the exact ProgramData JSONC path wins in
  the installed user's effective model catalog. The current elevated all-role
  harness then passed LocalService JSONC/ownership schema v2, a temporary second
  Windows account's effective OpenCode catalog, relocation/repair, rollback, and
  exact cleanup. The lab Compose path now runs
  as the fixed `llm-glue` account: a polkit rule permits only restart of the exact
  root-owned activation unit, an unrelated-unit restart was denied, real sync and
  health passed, and authenticated LiteLLM-to-llama.cpp inference survived the
  handoff from the temporary root process.
  Real OpenCode 2.2.7 through a real LiteLLM 1.109.0 proxy and a
  captured mock upstream verified stable alias routing, streamed response flow,
  environment-backed client/backend authentication, `temperature`, and the binary
  `enable_thinking` field.
  See `docs/m4-qualification.md`.
- **M5 — release hardening:** post-release qualification reopened. Dual-RID
  self-contained single files, runtime-derived rN version identity, interactive
  bootstrap installers, SCM and
  systemd lifecycle commands, portable console/rolling-file logs, doctor and
  operational CLI, checksum-verified stable updates/rollback, guarded equivalent
  release scripts, packaging manifests, CI, and operator/developer docs. Native
  Windows and WSL builds/tests pass. A minimal Debian 13.5 environment exposed
  and led to removal of the Linux binary's undeclared ICU dependency; the
  invariant-globalization binary and full fixed-account systemd lifecycle now
  pass there, and CI reruns the binary/config check in `debian:13-slim`.
  Isolated Bash/PowerShell release matrices qualify fail-closed Git states and a
  local-only partial-publication recovery path; both passed on 2026-11-11.
  Official GitHub CLI 2.103.3 was downloaded from GitHub
  Releases, checksum-verified, authenticated, and used by the guarded publisher.
  A full isolated
  clean-clone r1 dry-run at commit `292fec7` then passed preflight and all 103
  tests, published both self-contained RIDs locally, reported `LLM Glue r1`, and
  generated a matching `1.1.0.0` file-version manifest plus SHA-256 package list
  before stopping prior to tag/release creation. After an authenticated final
  preflight, immutable `r1` was published from
  `dd070d215d91f3518ce71a443d8867ddd11b91e0` with win-x64 and linux-x64 assets,
  manifest, and checksums. An isolated r0 Windows binary then discovered the
  public stable release, downloaded and checksum-verified the official asset,
  applied it to report `LLM Glue r1`, and restored the exact original r0 SHA-256
  through `update rollback`. The first persistent Linux official-asset upgrade
  then exposed four r1 defects: in-process replacement invalidated lazy bundle
  loads, tar extraction lost executable mode, authenticated Server health was
  probed without its token, and r1 rejected valid r0-generated persisted state.
  labhost03 was restored to its intact r0 executable and healthy service. The
  correction uses a post-exit Linux activator, restores mode, carries health auth
  only in the helper environment, and accepts canonical earlier-revision ownership
  headers while retaining all hash/schema validation. All 105 tests pass; a
  simulated r2 single file loaded a copy of the real r0 state, replaced an
  isolated executable, launched a child process, and passed authenticated health
  against the real endpoint. M5 requires an authorized corrective r2 publication
  and live bootstrap upgrade because immutable r1 cannot repair its own updater.
  Unknown reasoning
  capability excludes a deployment from client publication rather than being
  converted to `false`.
  Service registration now provisions stable least-privilege identities and
  verifies exact executable, sidecar, state, log, and target access before
  registering the service. Elevated Windows all-role lifecycle, current JSONC
  ownership sidecar, second-account catalog, and rollback pass. Native Ubuntu
  and Debian 13/WSL all-role systemd
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
- [x] Repeat OpenCode system precedence with two actual Linux OS accounts and a
  temporary second native Windows account under the current LocalService target.

## Key decisions

- OpenCode is version-gated; 2.x uses `providers` and explicit capabilities to
  prevent fabricated variants.
- LiteLLM generated files contain only `model_list`; primary YAML is never replaced.
- LiteLLM production activation uses configured restart plus health/rollback, not
  development auto-reload.
- Capability unknowns are preserved; unknown declarations and modes are omitted,
  while the discovered model remains published unless text is explicitly
  unsupported.

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

Obtain explicit authorization for corrective r2, publish it through the guarded
process, and qualify the labhost03 bootstrap upgrade plus service/catalog/inference
continuity. Do not retry r1 Linux self-update.

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
- [x] Targeted client tests plus the full 105-test suite pass on Windows/.NET 10;
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
- [x] Current all-role Windows `-SystemOpenCode` harness qualifies ADR 0007 JSONC
  ownership-sidecar access, real Ollama discovery, and a temporary second Windows
  account in one guarded LocalService pass, including rollback and exact cleanup.
- [x] Live foreground update/apply/rollback from the official r1 Windows asset:
  r0 discovered r1, verified its published SHA-256, applied it, reported r1, and
  restored the byte-identical original r0 executable. Windows service rollback is
  separately live-qualified.
- [ ] Publish the corrective r2 and qualify a bootstrap/manual Linux upgrade from
  the current labhost03 r0 service. The fixed simulated-r2 single file passes
  copied-state, staged replacement/mode, child-process, and authenticated-health
  tests; r1's old updater must not be retried.
- [x] Authenticated preflight against the actual clean, already-pushed upstream
  passed at `51932effb5e68c0f6f72508238cbf36804ef2c97` with GitHub CLI 2.103.3.
  The first publication attempt stopped before creating a tag or release because
  PowerShell treated the helper parameter name `Arguments` as a named-parameter
  prefix when the annotated-tag `-a` option was reached. The helper now uses an
  explicit `GitArguments` remainder parameter; the isolated PowerShell Git-state
  matrix passes after the correction. Publication must target the final pushed
  correction commit and re-run the authenticated preflight. The corrected path
  subsequently passed and published r1 from `dd070d2`.
- [x] M4 representative live suite and both supported deployment paths pass.

Latest available verification: Windows and native Ubuntu/WSL Release builds with
all 105 tests passing;
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
