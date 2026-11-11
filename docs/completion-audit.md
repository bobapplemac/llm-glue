# r1 completion audit

Last audited: 2026-11-11

This document maps the specification's qualification gates to authoritative
evidence. “Implemented” is not treated as “live-qualified,” and fixture/captured
upstream results are identified separately. The product remains `r0`; no release
is authorized.

## Milestone gates

| Gate | Evidence | Status |
|---|---|---|
| M0 integration design | `docs/m0-evidence.md`, compatibility matrix, two actual Linux accounts, live OpenCode 2.2.7/LiteLLM 1.109.0/Ollama 0.40.16 probes, ADRs 0001–0007 | Passed. Required sources, verification and fail-closed unknown behavior are defined. |
| M1 discovery | GGUF/Ollama/llama.cpp fixture tests; live Ollama discovery/context/reasoning/residency/probe evidence | Passed for implementation. Representative live llama.cpp models remain an M4 gate, not an M1 claim. |
| M2 publication | Golden JSON/YAML, real Kestrel/ETag/auth tests, captured LiteLLM forwarding, real OpenCode forwarding, persisted catalog integrity tests | Passed. Central backend interpretation still belongs to M4. |
| M3 synchronization | Atomic/no-op/rollback/collision tests; scoped OpenCode ownership tests; live Windows service lifecycle predating ADR 0007; current all-role Linux systemd sidecar lifecycle | Passed for implementation and Linux current service identity. Current Windows LocalService sidecar rerun is outstanding. |
| M4 live paths | Native Windows Ollama functional inference, named modes, duplicate suppression, add/change/remove, application restart, outage/grace/recovery; captured central OpenCode→LiteLLM transport | **Not passed.** Debian llama.cpp/LiteLLM representative models and Paseo UI are unavailable. The real Windows system target/current LocalService sidecar and Windows effective system-path behavior also remain unqualified. |
| M5 release hardening | Dual-RID r0 binaries, installers, services, updates/rollback, CI, docs, guarded release matrices, exact-commit checksum manifest | **Not passed.** Depends on M4; official-release update and clean authenticated upstream preflight require release authorization, a pushed commit, GitHub CLI/auth, and an official asset. |

## Representative model matrix

| Required case | Evidence | Qualification |
|---|---|---|
| Qwen3.6 35B-A3B MTP Q8_0 via llama.cpp/LiteLLM | Specification reference profile, fixtures, captured transport of `enable_thinking` and sampling fields | Transport fixture/captured upstream only; real backend/model missing. |
| Gemma 4 via llama.cpp/LiteLLM | Sanitized discovery fixture | Fixture only; real backend/model missing. |
| GPT-OSS effort levels | Sanitized named-effort fixture | Fixture only; real backend/model missing. |
| Windows Ollama reasoning model | Qwen 3.5 4B native and OpenAI-compatible probes plus real OpenCode selection/inference | Live-qualified on Ollama 0.40.16/OpenCode 2.2.7. |
| Windows Ollama non-reasoning model | Qwen3-VL `[false]` metadata and live same-alias change removing reasoning modes | Live-qualified for discovery/catalog propagation; no separate text inference was needed to claim non-reasoning metadata. |
| Failure/lifecycle cases | Automated invalid/collision/rollback tests; live native add/change/remove, restart, outage/grace/recovery and service rollback | Qualified for standalone path except current Windows LocalService ownership sidecar. Central service/backend cases remain live-unqualified. |

## Definition-of-done audit

The standalone Windows/Ollama half of the v1 definition of done is substantially
qualified using isolated targets: zero-touch discovery and catalog synchronization,
real inference, accurate verified reasoning modes, safe changes/removal, transient
failure recovery, and unrelated-setting preservation all have live evidence.

The central definition of done is not yet proven. Completion requires one real
Debian deployment with the representative llama.cpp models and LiteLLM service,
then evidence that model add/change/remove automatically reaches a remote Windows
OpenCode/Paseo client with the expected parameters and survives restart/outage.

## External actions still required

1. Make `labhost03` reachable and provide its llama.cpp/LiteLLM services and the
   Qwen/Gemma/GPT-OSS representative models.
2. Provide Paseo for catalog and named-mode UI verification.
3. Run the current elevated Windows all-role service harness, and—only with
   explicit approval—qualify the real `%ProgramData%\opencode\opencode.json`
   system target rather than an isolated override.
4. After every live gate passes, explicitly authorize the first official `r1`
   release. Push the clean commit, provide authenticated `gh`, run the guarded
   preflight, publish once, and qualify update/apply from that immutable asset.

None of these missing live results may be replaced by the existing fixtures or
captured mock upstream.
