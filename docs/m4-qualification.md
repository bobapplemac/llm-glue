# M4 live qualification

Last updated: 2026-11-11

## Standalone Windows/Ollama — functional path pass, full scenario gate partial

The current Windows host ran the composed Agent + Server + Client against the
live Ollama 0.40.16 endpoint and an isolated OpenCode system target:

1. Agent discovered two live Ollama routes through tags/show/version/ps.
2. Server normalized and published two models with a content revision and
   generated an OpenCode 2.x artifact.
3. Client fetched the artifact over loopback, staged and atomically installed it.
4. The artifact contained native contexts 262144 and 32768. The first model's
   explicit `[false]` thinking metadata became non-reasoning; the second model's
   explicit thinking support became reasoning-enabled.
5. OpenCode 2.2.7 loaded the installed document and exposed both models through
   its effective `/api/model` endpoint with the same contexts and no invented
   named variants.

This validates discovery → publication → download → scoped configuration apply →
OpenCode catalog visibility. That catalog run did **not** validate parameter
transport, model add/remove propagation, provider restart recovery, or Paseo UI
behavior. No production configuration was modified; it used isolated temporary
paths and all processes were stopped.

A later bounded native Ollama qualification used the existing
`qwen3.5:4b-q4_K_M` route. `think=false` returned `OK` with no thinking field;
`think=true` returned `OK` with a separate nonempty thinking field. `/api/ps`
reported effective context 4096 versus native context 262144 and
`size_vram == size == 3,140,495,604`, confirming full VRAM residency for that
load. With the model held loaded, LLM Glue's `loaded-only` policy added verified
`ollama:bounded-route-probe` evidence only to that route and did not probe the two
dormant routes. The model was explicitly unloaded afterward. This verifies the
native provider contract and probe policy.

On 2026-11-11, the installed native Windows OpenCode 2.2.7 executable was then
run against an updated development qualification build and the installed Ollama
0.40.16. The same code was subsequently repackaged with the required `r0`
pre-release identity.
LLM Glue discovered `qwen3.5:4b-q4_K_M`, published and synchronized an isolated
OpenCode target, and exposed verified `thinking-off` and `thinking-on` variants.
OpenCode selected
`llm-glue-ollama-windows-live/qwen3.5:4b-q4_K_M#thinking-on`, performed a real
inference, and received exactly `LLM_GLUE_WINDOWS_OK`. Separate direct
OpenAI-compatible probes proved `reasoning_effort=none` yielded no reasoning and
`medium` yielded a 400-character reasoning trace. The model was unloaded and
`/api/ps` was empty afterward. The harness used process-local OpenCode environment
variables and repository-contained temporary paths; the user's normal config and
system config were not modified.

A repeat run after duplicate reconciliation required both the enriched LLM Glue
model and absence of `ollama/qwen3.5:4b-q4_K_M`. OpenCode honored the generated
`disabled_providers` entry, exposed only the enriched route, completed the same
real inference, and left `/api/ps` empty after cleanup. Adjacent generated
ownership state recorded that LLM Glue—not the user—owned this disablement.

Reproduce the isolated Windows path with
`scripts/Test-WindowsOllamaOpenCodeLive.ps1`; it refuses a missing binary/model,
uses a fresh evidence directory, and preserves pre-existing model residency.

An additional isolated secondary Ollama 0.40.16 run used copied manifests and
metadata plus temporary hard links to the existing immutable model blobs. It
qualified the real composed lifecycle path without changing the production model
store:

1. Copying the Qwen 3.5 route to `llm-glue-lifecycle:latest` propagated an add;
   OpenCode exposed its verified thinking modes without a native duplicate.
2. While LLM Glue was stopped, the alias was replaced from the Qwen 3.5 model by
   the Qwen3-VL model. Application restart discovered the same ID's new family and
   removed the no-longer-supported reasoning variants.
3. Terminating the secondary provider produced an observed discovery failure.
   The last-known-good alias survived within removal grace, and provider restart
   recovered without target loss.
4. Removing the alias propagated through grace and disappeared from OpenCode.
5. Removing every isolated model emptied the managed provider, released LLM
   Glue's owned `ollama` disablement, and restored OpenCode's built-in provider.

The temporary store was deleted after the run. The production tag set and a
before/after store fingerprint (manifest/metadata content plus immutable-blob
size/timestamp inventory) were identical, and only the pre-existing production
Ollama process remained. Reproduce with
`scripts/Test-WindowsOllamaCatalogLifecycleLive.ps1`.

This qualifies native discovery → publish → sync → named selection → inference,
add/change/removal, application restart, and transient provider outage/recovery.
Windows service use of the production OpenCode path and Paseo UI presentation
remain outstanding.

## OpenCode and LiteLLM forwarding with captured upstream — pass

Pinned LiteLLM 1.109.0 was launched with its required master key using the
generated golden route shape and an environment-backed provider credential. A
real `/v1/chat/completions` request traversed the proxy to a local capturing
OpenAI-compatible upstream and returned through LiteLLM. The capture verified the
stable alias translated to the upstream model, the provider bearer credential was
used, and `temperature`, `top_p`, `max_tokens`, and the binary variant field
`enable_thinking = true` arrived unchanged. This test also found and corrected
two integration assumptions: `openai_like/` is unmapped
at request time in 1.109.0 (use `openai/` with `api_base`), and central OpenCode
artifacts must use the client-reachable LiteLLM URL plus LiteLLM alias rather than
the private llama.cpp route. Live llama.cpp interpretation of the thinking field,
effort-level profiles, and real model output remain part of the Debian gate below.

OpenCode 2.2.7 then loaded the central golden artifact and executed a real
noninteractive `run` using `llm-glue-llama-cpp-lab/qwen-model#thinking-on`.
OpenCode authenticated to LiteLLM through the generated environment reference,
LiteLLM resolved the stable alias, and the streamed `FORWARDED` response returned
to OpenCode. The captured upstream body contained the backend model ID,
`temperature = 0.6`, and `enable_thinking = true`. Initial execution also proved
that custom providers need provider-level `env` metadata to become routable; the
generator now emits it. This qualifies client/proxy transport with a captured
upstream, not llama.cpp's interpretation or Paseo UI behavior.

## Windows service lifecycle — pass

An elevated, isolated all-role development qualification ran as
`NT AUTHORITY\\LocalService` against local Ollama and a temporary OpenCode target.
It verified SCM install/start, HTTP health, controlled stop, executable relocation,
`service repair` updating the absolute path, restart/health from the new location,
the hidden Windows PowerShell service rollback helper, and uninstall cleanup. The
rollback result was `success`; the sidecar, state, log, and target stayed in the
isolated evidence directory, and no `LLMGlue` service remained afterward.

The live run found and corrected two issues that dry-runs could not expose: ASP.NET
was trying to enumerate a portable executable directory inaccessible to
LocalService, and the hidden helper used PowerShell 7-only encoding plus a string
to `[bool]` binding incompatible with Windows PowerShell 5.1. The host now uses its
authorized log directory as content root, and the helper is 5.1-compatible.

The catalog run also exposed and led to fixes for two real issues: duplicate DI adapter
registration caused duplicate model identities on the second cycle, and the
default Windows Event Log provider failed under a non-elevated identity. The
application now registers each adapter once and uses explicit portable logging
providers.

This Windows run predates the adjacent OpenCode ownership state introduced by
ADR 0007. The live harness now requires LocalService to create and retain that
sidecar across install, relocation/repair, and rollback, but the current Codex
process does not have an elevated Windows token and the rerun refused before
creating a service. LocalService sidecar access therefore remains unqualified.

## Debian llama.cpp/LiteLLM — not run

`labhost03`, its model directories, llama.cpp router, LiteLLM service identity,
and representative Qwen/Gemma/GPT-OSS/MTP models are unavailable in this
environment. The following remain mandatory:

- Real direct/router discovery and GGUF multipart/draft classification.
- LiteLLM include deployment followed by controlled service restart and health.
- Captured Off/On and effort-level request bodies at llama.cpp.
- Functional inference and context/KV/runtime observation.
- Model add, change, removal, outage, stale-grace, restart, and rollback scenarios.

## Linux systemd lifecycle — pass in Ubuntu/WSL

A native systemd instance ran the r1 Linux single-file binary under the fixed,
non-login `llm-glue` identity. An isolated Server configuration passed unit
install/enable, start/HTTP health, stop, executable relocation plus `service
repair`, restart/health, synchronous service rollback, uninstall preservation,
and final cleanup verification. The test first refused any pre-existing unit,
account, or owned service directories; afterward none of its system resources
remained. This qualifies service mechanics, not the unavailable Debian
llama.cpp/LiteLLM inference path.

The current r0 binary was rerun after ADR 0007 using a loopback Ollama discovery
fixture and all three roles. Under the fixed `llm-glue` identity it generated the
OpenCode target and adjacent ownership sidecar, recorded the owned native-provider
disablement, preserved both across executable relocation and service rollback,
and retained them after uninstall as documented. The fixture, unit, account, and
service directories were then removed. This qualifies systemd permissions and
ownership-state lifecycle, not real Linux Ollama or Debian llama.cpp inference.

## Multi-user precedence — Linux pass; Windows/UI pending

An isolated Ubuntu/WSL run created two actual OS accounts and installed the exact
OpenCode 2.2.7 binary plus the generated system artifact temporarily. For both
accounts, effective `/api/model` proved the system provider/model values won a
deliberately conflicting user document, while an unrelated user-owned provider
and model survived. Source listings included both `/etc/opencode/opencode.json`
and the correct account file. The harness refused pre-existing account or system
targets and cleanup left neither test accounts nor `/etc/opencode` behind.
Reproduce on an isolated Linux/WSL host with
`sudo scripts/Test-OpenCodeMultiUserLive.sh /path/to/opencode-2.2.7`.

Equivalent Windows multi-account behavior and Paseo UI variant presentation are
still unqualified.

## Gate status

M4 is **not passed**. Native Windows OpenCode/Ollama inference and catalog
lifecycle plus Linux multi-account precedence are verified; the remaining Debian,
Windows multi-account, production-target service, and Paseo UI requirements
cannot be simulated or waived.
