# r1 completion audit

Last audited: 2026-11-11

This document maps the specification's qualification gates to authoritative
evidence. “Implemented” is not treated as “live-qualified,” and fixture/captured
upstream results are identified separately. The product remains `r0`; no release
is authorized.

## Milestone gates

| Gate | Evidence | Status |
|---|---|---|
| M0 integration design | `docs/m0-evidence.md`, compatibility matrix, two actual Linux accounts, live OpenCode 2.2.7/LiteLLM 1.106.3/1.109.0/Ollama 0.40.16/llama.cpp probes, ADRs 0001–0010 | Passed. Required sources, verification and fail-closed unknown behavior are defined. |
| M1 discovery | GGUF/Ollama/llama.cpp tests; live direct and router shapes; explicit container path mapping; real multipart/MTP, native/configured/effective context, KV/GPU/speculative and behavioral reasoning evidence | Passed for implementation and representative discovery. Unsupported GPT-OSS Off behavior remains unknown as required. |
| M2 publication | Golden JSON/YAML, real Kestrel/ETag/auth tests, captured LiteLLM forwarding, real OpenCode forwarding, persisted catalog integrity tests | Passed. Central backend interpretation still belongs to M4. |
| M3 synchronization | Atomic/no-op/rollback/collision tests; source-scoped OpenCode ownership v2 and migration tests; live LiteLLM include/restart/fail-once rollback; current Linux and Windows LocalService JSONC/sidecar lifecycle | Passed for implementation and live central/standalone target activation. |
| M4 live paths | Native Windows Ollama lifecycle/inference; current LocalService ProgramData JSONC/ownership plus second-account catalog; Debian 13.7 Qwen/Gemma/GPT-OSS router and GGUF evidence; generated LiteLLM 1.106.3 route; MarkIV OpenCode Off/On inference; Paseo 0.15.4 catalog/Off/On presentation; central add/change/remove, outage/grace/recovery, restart/rollback, and exact-unit polkit delegation | **Passed.** Both supported deployment paths and representative failure/lifecycle cases have live evidence. |
| M5 release hardening | Dual-RID binaries, minimal-Debian no-ICU execution, installers, services, updates/rollback, CI, docs, guarded release matrices, and full clean-clone r1 dry-run at `292fec7` with exact-commit manifest/checksums | **Not passed.** Local packaging is qualified. The remaining official-release update and clean authenticated upstream preflight require explicit release authorization, a pushed commit, GitHub CLI auth, and the resulting immutable asset. |

## Representative model matrix

| Required case | Evidence | Qualification |
|---|---|---|
| Qwen3.6 35B-A3B MTP Q8_0 via llama.cpp/LiteLLM | Real `qwen35moe` GGUF/MTP/runtime evidence, `none`/`medium` behavior, generated route, LiteLLM and MarkIV OpenCode exact-marker inference | Live-qualified for binary Off/On; named low/high semantics deliberately unclaimed. |
| Gemma 4 via llama.cpp/LiteLLM | Real Gemma 4 12B route, GGUF/context evidence, exact-marker `none`/`medium` inference, same-ID lifecycle change | Live-qualified for binary Off/On and catalog propagation. |
| GPT-OSS effort levels | Real GPT-OSS 120B route and exact-marker `none`/`medium` inference | Live inference qualified; `none` still emitted reasoning, so Off and named effort semantics remain unknown/unpublished. |
| Windows Ollama reasoning model | Qwen 3.5 4B native and OpenAI-compatible probes plus real OpenCode selection/inference | Live-qualified on Ollama 0.40.16/OpenCode 2.2.7. |
| Windows Ollama non-reasoning model | Qwen3-VL `[false]` metadata and live same-alias change removing reasoning modes | Live-qualified for discovery/catalog propagation; no separate text inference was needed to claim non-reasoning metadata. |
| Failure/lifecycle cases | Automated invalid/collision/rollback tests; standalone and central add/change/remove, restart, outage/grace/recovery; real fail-once LiteLLM rollback; exact-unit Compose delegation with unrelated-unit denial; Windows LocalService relocation/rollback/JSONC-sidecar cleanup | Live-qualified. |

## Definition-of-done audit

The standalone Windows/Ollama half of the v1 definition of done is substantially
qualified using isolated targets: zero-touch discovery and catalog synchronization,
real inference, accurate verified reasoning modes, safe changes/removal, transient
failure recovery, and unrelated-setting preservation all have live evidence.

The central OpenCode definition is now proven on the lab/MarkIV pair: automatic
discovery/publication, authenticated sync, real inference, parameter behavior,
lifecycle, outage and rollback passed. Paseo catalog and binary-variant
presentation and the lab's fixed-account Compose activation are now
live-qualified. The current Windows LocalService ProgramData path, ownership
sidecar, and second native account are also live-qualified. M4 is complete.

## External actions still required

1. Explicitly authorize the first official `r1`
   release. The checksum-verified portable GitHub CLI 2.103.3 is present but
   logged out, and the clean branch is ahead of `origin/main`. Push the
   clean commit, authenticate `gh`, run the guarded preflight, publish once, and
   qualify update/apply from that immutable asset.

None of these missing live results may be replaced by the existing fixtures or
captured mock upstream.
