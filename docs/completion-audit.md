# r1 completion audit

Last audited: 2026-11-11

This document maps the specification's qualification gates to authoritative
evidence. “Implemented” is not treated as “live-qualified,” and fixture/captured
upstream results are identified separately. The product remains `r0`; no release
is authorized.

## Milestone gates

| Gate | Evidence | Status |
|---|---|---|
| M0 integration design | `docs/m0-evidence.md`, compatibility matrix, two actual Linux accounts, live OpenCode 2.2.7/LiteLLM 1.106.3/1.109.0/Ollama 0.40.16/llama.cpp probes, ADRs 0001–0009 | Passed. Required sources, verification and fail-closed unknown behavior are defined. |
| M1 discovery | GGUF/Ollama/llama.cpp tests; live direct and router shapes; explicit container path mapping; real multipart/MTP, native/configured/effective context, KV/GPU/speculative and behavioral reasoning evidence | Passed for implementation and representative discovery. Unsupported GPT-OSS Off behavior remains unknown as required. |
| M2 publication | Golden JSON/YAML, real Kestrel/ETag/auth tests, captured LiteLLM forwarding, real OpenCode forwarding, persisted catalog integrity tests | Passed. Central backend interpretation still belongs to M4. |
| M3 synchronization | Atomic/no-op/rollback/collision tests; source-scoped OpenCode ownership v2 and migration tests; live LiteLLM include/restart/fail-once rollback; current Linux service sidecar lifecycle | Passed for implementation and live central target activation. Current Windows LocalService sidecar rerun is outstanding. |
| M4 live paths | Native Windows Ollama lifecycle/inference; Debian 13.7 Qwen/Gemma/GPT-OSS router and GGUF evidence; generated LiteLLM 1.106.3 route; MarkIV OpenCode Off/On inference; central add/change/remove, outage/grace/recovery, restart and rollback | **Partial.** Representative central and standalone paths pass. Paseo UI, the real Windows system target/current LocalService sidecar, Windows effective system-path behavior, and least-privilege delegation of the lab Compose restart remain unqualified. |
| M5 release hardening | Dual-RID r0 binaries, minimal-Debian no-ICU execution, installers, services, updates/rollback, CI, docs, guarded release matrices, exact-commit checksum manifest | **Not passed.** Depends on M4; official-release update and clean authenticated upstream preflight require release authorization, a pushed commit, GitHub CLI/auth, and an official asset. |

## Representative model matrix

| Required case | Evidence | Qualification |
|---|---|---|
| Qwen3.6 35B-A3B MTP Q8_0 via llama.cpp/LiteLLM | Real `qwen35moe` GGUF/MTP/runtime evidence, `none`/`medium` behavior, generated route, LiteLLM and MarkIV OpenCode exact-marker inference | Live-qualified for binary Off/On; named low/high semantics deliberately unclaimed. |
| Gemma 4 via llama.cpp/LiteLLM | Real Gemma 4 12B route, GGUF/context evidence, exact-marker `none`/`medium` inference, same-ID lifecycle change | Live-qualified for binary Off/On and catalog propagation. |
| GPT-OSS effort levels | Real GPT-OSS 120B route and exact-marker `none`/`medium` inference | Live inference qualified; `none` still emitted reasoning, so Off and named effort semantics remain unknown/unpublished. |
| Windows Ollama reasoning model | Qwen 3.5 4B native and OpenAI-compatible probes plus real OpenCode selection/inference | Live-qualified on Ollama 0.40.16/OpenCode 2.2.7. |
| Windows Ollama non-reasoning model | Qwen3-VL `[false]` metadata and live same-alias change removing reasoning modes | Live-qualified for discovery/catalog propagation; no separate text inference was needed to claim non-reasoning metadata. |
| Failure/lifecycle cases | Automated invalid/collision/rollback tests; standalone and central add/change/remove, restart, outage/grace/recovery; real fail-once LiteLLM rollback | Qualified except current Windows LocalService ownership sidecar and lab least-privilege Compose delegation. |

## Definition-of-done audit

The standalone Windows/Ollama half of the v1 definition of done is substantially
qualified using isolated targets: zero-touch discovery and catalog synchronization,
real inference, accurate verified reasoning modes, safe changes/removal, transient
failure recovery, and unrelated-setting preservation all have live evidence.

The central OpenCode definition is now proven on the lab/MarkIV pair: automatic
discovery/publication, authenticated sync, real inference, parameter behavior,
lifecycle, outage and rollback passed. Completion still requires equivalent
Paseo UI evidence, Windows system-target/current LocalService qualification, and
a least-privilege authorization mechanism for this lab's Compose activation unit.

## External actions still required

1. Provide Paseo for catalog and variant UI verification.
2. Choose and qualify least-privilege delegation for the exact lab Compose
   activation unit; the live qualification intentionally used an isolated root
   transient LLM Glue process.
3. Run the current elevated Windows all-role service harness, and—only with
   explicit approval—qualify the real `%ProgramData%\opencode\opencode.json`
   system target rather than an isolated override.
4. After every live gate passes, explicitly authorize the first official `r1`
   release. Push the clean commit, provide authenticated `gh`, run the guarded
   preflight, publish once, and qualify update/apply from that immutable asset.

None of these missing live results may be replaced by the existing fixtures or
captured mock upstream.
