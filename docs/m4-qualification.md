# M4 live qualification

Last updated: 2026-11-11

## Standalone Windows/Ollama — pass

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

The installed user-profile path was also exercised on 2026-11-11 after the
operator reported that the integration was not visible in normal OpenCode use.
`C:\Users\Andrew\.config\opencode` existed but was empty, while OpenCode 2.2.7
was installed through the user's npm shim and Ollama 0.40.16 exposed three
models. A portable all-role r0 process synchronized the real user target
`opencode.json` plus its ownership sidecar. OpenCode's effective `/api/model`
response exposed all three models only through
`llm-glue-ollama-local-ollama`; the disabled native `ollama` provider produced
no duplicates. OpenCode then selected
`llm-glue-ollama-local-ollama/qwen3.5:4b-q4_K_M#thinking-off` and returned
exactly `LLM_GLUE_USER_CONFIG_OK` through the installed Ollama endpoint. The
foreground process was stopped cleanly after the check; the synchronized user
artifact remains usable, but automatic future catalog refresh still requires
the elevated service installation.

After the llama.cpp discovery follow-up was committed, the installed applications
were checked again through the normal, unmodified user profile. Ollama 0.40.16
still exposed all three local routes, and OpenCode 2.2.7 listed exactly the three
`llm-glue-ollama-local-ollama` entries with no native `ollama/...` duplicates.
OpenCode selected
`llm-glue-ollama-local-ollama/qwen3.5:4b-q4_K_M#thinking-off` and returned exactly
`LLM_GLUE_CURRENT_OK`; its structured result reported seven output tokens and
zero reasoning tokens. This reconfirms the currently installed default-profile
integration rather than only the isolated harness.

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
add/change/removal, application restart, transient provider outage/recovery, and
the installed user's effective OpenCode configuration. The later current-service
run below qualifies the system OpenCode path under LocalService and a second
Windows account; Paseo presentation is also qualified below.

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

## Live small-model llama.cpp/LiteLLM route — transport pass, representative gate partial

Ubuntu 26.04/WSL ran the official packaged llama.cpp build 8681
(`build_info: b8681-Debian`) on loopback with the Apache-2.0 SmolLM2 135M
Instruct Q2_K GGUF. This exposed two current API details absent from the earlier
fixtures: direct `/models` returns both a `models` compatibility array and the
OpenAI `data` array, and `/props` places `reasoning_format` under
`default_generation_settings.params`. The adapter initially misclassified this
as a router, losing loaded state, context, version, and the functional probe.
The corrected adapter recognizes `data` as direct mode. Official documentation
shows `reasoning_format` controls response parsing rather than model capability,
so the adapter deliberately leaves reasoning unknown for every such value.

A follow-up run executed the current LLM Glue source natively in Ubuntu/WSL with
the model directory allowlisted. Although the route was the alias
`smollm2-live`, `/props.model_path` identified the actual GGUF. Its canonical path
remained inside the allowlist, so LLM Glue inspected GGUF v3 metadata and recorded
architecture `llama`, type `model`, and file type `10` while retaining native
context 8192 from live model metadata. The serialized deployment contained no
local path. Automated companion tests reject provider-reported files outside the
allowlist without leaking the path. This qualifies live single-file GGUF
enrichment, not multipart or MTP classification.

The subsequent MTP implementation audit found that build 8681 reports
`default_generation_settings.params["speculative.type"]`, whereas newer upstream
shapes may expose `speculative.types` and a top-level boolean. The adapter now
normalizes both explicit forms. A fresh live run reported
`speculative.enabled=false` and `speculative.types=none`. Synthetic GGUF coverage
also verifies that a positive architecture-scoped `nextn_predict_layers` value is
reported only as an MTP runtime artifact trait and never as a client variant.
Real MTP artifact loading and speculative behavior remain part of the Debian gate.

With the server deliberately configured at 2048 tokens, LLM Glue reported native
context 8192, effective context 2048, version `b8681-Debian`, loaded state, and a
verified bounded text route. The production generator correctly excluded this
model because its reasoning capability remained unknown. To test downstream
mechanics without weakening that policy, a temporary LiteLLM configuration
mirrored the standard generated alias/API-base shape and carried
`max_input_tokens: 2048`; an actual LiteLLM 1.109.0 request reached llama.cpp and
returned model text with the same build fingerprint. Installed OpenCode 2.2.7
loaded an equivalent isolated central model, then correctly failed before
inference when its 3856-token agent request exceeded the configured 2048-token
limit.

The server was restarted at its native 8192-token context and the isolated
downstream configurations were updated to 8192. OpenCode then forwarded
a 3248-token request through LiteLLM and llama.cpp returned HTTP 200. The tiny
Q2 model generated to the 1024-token limit without a terminal answer, causing
OpenCode to request continuation; the isolated client loop was interrupted.
This proves real discovery, context evidence, bounded inference, LiteLLM routing,
and OpenCode-to-backend transport. It does **not** qualify automatic publication,
a successful OpenCode task, representative Qwen/Gemma/GPT-OSS reasoning and
sampling behavior, Debian service ownership, or lifecycle scenarios.

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

The earlier Windows run predated the adjacent OpenCode ownership state introduced
by ADR 0007. On 2026-11-11, the current r0 binary reran elevated against real
Ollama 0.40.16 with state/logs and the exact default OpenCode target under
ProgramData. LocalService discovered all three models, persisted bounded catalog
revisions, installed comment-bearing `opencode.jsonc`, and wrote ownership schema
v2 with the generated provider plus its owned native `ollama` disablement. A
random temporary standard Windows account then executed OpenCode 2.2.7 and found
`llm-glue-ollama-qualification-ollama` through the effective `/api/model` endpoint.
Relocation/repair, restart, synchronous service rollback, JSONC/sidecar retention,
and health all passed. The harness removed the service, temporary account/profile,
copied OpenCode binary, and exact ProgramData resources it created; post-run checks
found no residue. Sanitized artifacts, logs, state, hashes, and
`ownershipSidecar=verified secondUser=verified` are preserved under
`artifacts/live-service-qualification/68a622ea417e49ebace32f7c593520ce`.

This run also corrected qualification-only defects: Windows PowerShell 5.1 does
not recognize PowerShell 7's `utf8NoBOM` encoding name or parse JSONC comments via
`ConvertFrom-Json`; the harness now uses a cross-version .NET UTF-8 writer and a
targeted generated-field assertion. System-service durable state/logs use a
GUID-scoped ProgramData directory rather than a user workspace, matching intended
deployment boundaries, and are copied into evidence before guarded cleanup.

## Debian llama.cpp/LiteLLM — representative central path pass

`labhost03` ran Debian 13.7, llama.cpp router `0.9.0-dev` commit `0a94a3f`,
LiteLLM `1.106.3`, and ten configured routes. Its current `/models` response uses
OpenAI-style `data[]` plus nested router status/source evidence, exposing and
correcting the prior `data means direct` assumption. The live adapter now reports
dormant state, native/configured/effective context, KV cache, GPU layers, and
speculative settings from that shape.

The router reports container paths under `/models`; the host sees the read-only
tree under `/mnt/local-nvme-01/AI/models/gguf`. An explicit prefix mapping kept
the canonical allowlist intact. Real metadata inspection identified Qwen 3.6
35B-A3B Q8 as `qwen35moe` with one MTP layer, context 262144, Q8 KV, all GPU
layers, `draft-mtp`, and draft maximum 2. Dormant Gemma 4/GPT-OSS metadata and a
four-part Qwen split were also observed without publishing paths.

A disposable same-version LiteLLM container proved the one-time include syntax
and manual/generated list merge. The operator root YAML and its ten manual routes
were backed up and preserved; only a generated include and read-only Compose bind
were added. A scoped systemd activation unit recreated only LiteLLM and verified
`/health/liveliness`. The generated alias became the eleventh route while the
manual `bench-01-01` remained present.

Behavioral probes showed:

- Qwen: `none` produced no reasoning; `medium` produced reasoning. Generated
  LiteLLM requests returned the exact marker with 0 versus 798 reasoning chars.
- Gemma 4 12B: exact markers with 0 versus 358 reasoning chars.
- GPT-OSS 120B: inference passed, but `none` still produced 384 reasoning chars;
  its Off mode therefore remains unknown and unpublished.
- Accepted low/high requests were not treated as proof of distinct effort levels.

Installed Windows OpenCode 2.2.7 synchronized the authenticated artifact into
the normal profile, selected both generated variants, and returned exact Off/On
markers through OpenCode → LiteLLM → llama.cpp. The On JSON stream contained an
explicit reasoning event. A live ownership defect found during this step was
fixed: schema v2 scopes providers by artifact source, so the three pre-existing
local Ollama models and `disabled_providers: [ollama]` survived the central sync.

Lifecycle qualification added a temporary Qwen alias, propagated it into the
LiteLLM include, replaced the same ID with Gemma and observed `family=gemma4` in
the OpenCode artifact, then restored the original preset and observed removal
after grace. A 40-second router outage produced an agent failure while the
last-known-good include hash and Server health survived; restart plus exact
inference recovered. Finally, a dedicated fail-once activation unit caused a
real sync failure, exact include restoration, successful prior-state
reactivation, LiteLLM HTTP 200, and canonical reapply.

The initial central qualification process used an isolated root transient unit.
On 2026-11-11, Debian `polkitd` was installed and an exact rule granted the fixed
`llm-glue` account only `restart` for
`llm-glue-litellm-compose.service`. A real unprivileged `sync --once` replaced a
deliberately drifted generated include, restarted the root-owned Compose unit,
and passed its configured health check. Restart of the unrelated `polkit.service`
was denied with interactive authorization required. The current persistent r0
service then took over from the root transient unit, ran as `llm-glue:llm-glue`,
published ten routes, produced stable 304/no-op client polls, and completed an
authenticated LiteLLM-to-llama.cpp request with nonempty reasoning output. One
final root-owned catalog revision created during handoff was migrated inside the
LLM Glue state tree; the next discovery cycle and warning journal were clean.

## Linux systemd lifecycle — pass in Ubuntu and Debian 13/WSL

A native systemd instance ran the r0 Linux single-file binary under the fixed,
non-login `llm-glue` identity. An isolated Server configuration passed unit
install/enable, start/HTTP health, stop, executable relocation plus `service
repair`, restart/health, synchronous service rollback, uninstall preservation,
and final cleanup verification. The test first refused any pre-existing unit,
account, or owned service directories; afterward none of its system resources
remained. This qualifies service mechanics independently of the representative
Debian llama.cpp/LiteLLM inference path qualified above.

The current r0 binary was rerun after ADR 0007 using a loopback Ollama discovery
fixture and all three roles. Under the fixed `llm-glue` identity it generated the
OpenCode target and adjacent ownership sidecar, recorded the owned native-provider
disablement, preserved both across executable relocation and service rollback,
and retained them after uninstall as documented. The fixture, unit, account, and
service directories were then removed. This qualifies systemd permissions and
ownership-state lifecycle, not real Linux Ollama or Debian llama.cpp inference.

A clean Debian 13.5 WSL installation then exposed an undeclared portability
dependency: the self-contained binary aborted because ICU was absent. ADR 0008
made product globalization invariant, matching the existing culture-independent
network/configuration contracts. The rebuilt binary ran without ICU, validated
the central llama.cpp/LiteLLM configuration, and passed the same all-role systemd
lifecycle with fixture discovery, managed target plus ownership sidecar,
relocation/repair, restart, synchronous rollback, uninstall preservation, and
final service/account/directory cleanup. Python and curl were installed only for
the qualification harness; neither is an LLM Glue runtime dependency. This
qualifies Debian 13 service and binary mechanics; the representative-model path
on `labhost03` is qualified separately above.

## Multi-user precedence and Paseo presentation

An isolated Ubuntu/WSL run created two actual OS accounts and installed the exact
OpenCode 2.2.7 binary plus the generated system artifact temporarily. For both
accounts, effective `/api/model` proved the system provider/model values won a
deliberately conflicting user document, while an unrelated user-owned provider
and model survived. Source listings included both `/etc/opencode/opencode.json`
and the correct account file. The harness refused pre-existing account or system
targets and cleanup left neither test accounts nor `/etc/opencode` behind.
Reproduce on an isolated Linux/WSL host with
`sudo scripts/Test-OpenCodeMultiUserLive.sh /path/to/opencode-2.2.7`.

Equivalent Windows multi-account behavior is now qualified by the elevated
LocalService run described above.

On MarkIV, a later reversible probe created the exact default
`C:\ProgramData\opencode\opencode.jsonc` with a unique enabled provider and model.
OpenCode 2.2.7 parsed its JSONC comment and exposed the unique model through the
effective `/api/model` endpoint even though the user document had its own provider
allowlist. The test removed the only file and directory it created. OpenCode's
source listing duplicated the system document and one immediate post-removal
invocation retained stale source metadata; after three seconds a fresh process
listed only the user document. Effective catalog behavior, not the source array,
is therefore the qualification evidence. The subsequent elevated run passed the
same real ProgramData path under LocalService and a temporary second account.

The elevated Windows harness now has a guarded `-SystemOpenCode` mode that covers
both missing checks in one run. It refuses pre-existing LLM Glue and ProgramData
OpenCode resources, installs the current all-role service under LocalService,
validates JSONC plus ownership schema v2, creates a random temporary standard
account, and executes a supplied OpenCode binary under that account to inspect the
effective model API. Cleanup is scoped to the random account and exact directories
created by the run. Its live elevated result passed as recorded above.

Paseo 0.15.4 on MarkIV was inspected through its real model chooser. Searching
for `llm` displayed the exact generated central route
`llm-glue-llama-cpp-labhost03-router/bench-01-01` and all three generated local
Ollama routes. Selecting `bench-01-01` added a thinking selector whose menu
contained both `Thinking off` and `Thinking on`. This live UI observation
qualifies catalog and binary-variant presentation; real inference through the
same central route is covered by the OpenCode/LiteLLM/llama.cpp qualification
above rather than being inferred from the UI.

## Gate status

The representative Debian central inference, generated include/restart/rollback,
catalog lifecycle, fixed-account Compose delegation, native Windows
OpenCode/Ollama inference, MarkIV ProgramData/LocalService precedence, native
Windows and Linux multi-account behavior, and Paseo presentation are verified.
The M4 gate is **passed**. Windows Server 2025 remains exploratory rather than a
supported v1 qualification target.
