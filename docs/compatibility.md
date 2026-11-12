# Compatibility and integration evidence

Evidence date: 2026-11-11. “Live” means executed in the current Windows test
environment; “source” means inspected official documentation or the installed
release source. Live production qualification is tracked separately.

Native .NET 10 Kestrel HTTPS is live-qualified on Windows with an auto-generated
30-year PEM certificate/key. A permitted `localhost` SNI handshake returned the
health/status API, a matching SubjectPublicKeyInfo SHA-256 pin succeeded, a wrong
pin failed, and an IP handshake outside the configured SNI list was refused.
The self-contained Linux build also passed live Kestrel HTTPS health and pinned
status retrieval under WSL. Password-protected PFX loading is fixture-qualified;
Linux systemd and cross-machine remote-Agent qualification remain pending.

| Integration | Version | Evidence | Result |
|---|---:|---|---|
| OpenCode | 2.2.7 | Live Windows executable, two native Linux accounts, official schema/docs | System config wins conflicts for both accounts; nonconflicting user/project values survive; effective models available at `/api/model`; source arrays are not effective config; watched files apply without explicit reload in this test. |
| Paseo | 0.15.4 | Live MarkIV desktop UI | Model chooser displayed the generated central and local provider/model IDs; selecting the central route exposed `Thinking off` and `Thinking on`. Inference is qualified separately through the same OpenCode route. |
| LiteLLM Proxy | 1.106.3 and 1.109.0 | Live Debian Docker Compose service plus isolated Windows install/source | Both versions accept the dedicated YAML include and concatenate `model_list`. Debian controlled Compose recreation, health, real llama.cpp forwarding, and atomic rollback passed. Development `--reload` remains unsuitable. |
| Ollama | 0.40.16 | Live local HTTP API, OpenAI-compatible inference, and OpenCode 2.2.7 | `/api/tags`, `/api/show`, `/api/ps`, and `/api/version` match documented shapes. Native and `reasoning_effort` Off/On thinking, effective/native context separation, full VRAM residency, loaded-only route probing, named variant selection, and real OpenCode inference passed for Qwen 3.5 4B. |
| llama.cpp | 8681 (`b8681-Debian`) and router `0.9.0-dev` (`0a94a3f`) | Live Ubuntu direct server and Debian 13.7/CUDA router | Direct and current `data[]` router shapes are distinguished. The router qualified Qwen MTP, Gemma 4, GPT-OSS, multipart GGUF, configured/effective context, KV/GPU/speculative traits, reasoning probes, lifecycle, and outage recovery. |
| .NET SDK | 10.0.401 | Live | Build toolchain available on Windows. |
| .NET SDK (Ubuntu/WSL) | 10.0.115 | Live native Linux build/test/systemd | GNU Make restore/build/test, linux-x64 execution, and isolated fixed-account all-role systemd install/repair/rollback/uninstall passed, including OpenCode ownership-sidecar permissions. |
| Debian | 13.5 (trixie) x64 | Live minimal WSL installation with systemd, initially without ICU | The first self-contained build exposed an undeclared ICU dependency. Invariant globalization removed it; the rebuilt single file then ran, validated the central config, and passed fixed-account all-role systemd install/start/sync/move/repair/restart/rollback/uninstall with cleanup verified. |
| Tomlyn | 2.10.2 | Live NuGet restore/build on net10.0 | Compatible parser selected; strict binding tests are an M1 gate. |
| YamlDotNet | 18.1.0 | Automated net10.0 target tests | LiteLLM primary/generated YAML parsing, include and alias ownership checks. |
| NLog.Extensions.Logging | 6.2.2 | Build/test | Standard logging provider with console and bounded rolling file targets. |
| LLM Glue releases | r0 qualification builds plus official r1/r2/r3 | Live Windows, Ubuntu/WSL, and Debian 13.5/13.7 execution | Development RID outputs report r0 and the Linux file runs without ICU. r1 Windows update/apply/rollback passed; its Linux defects are recorded in ADR 0011. r2 was installed manually after normalizing its CRLF checksums and passed the complete MarkIV OpenCode → LiteLLM → llama.cpp path, but its activation window was shorter than the lab's 45–50 second LiteLLM cold start. Official r3 emitted LF-only metadata, updated successfully from r2 through the post-exit activator, waited through a real slow LiteLLM recreation, converged without errors, and returned `OPENCODE_R3_OK` through OpenCode 2.2.7 `thinking-on`. |

## OpenCode 2.2.7

Current schema uses `providers` and supports explicit model `capabilities`,
`reasoning`, `limit`, settings, and variants. The generator must emit explicit
capability metadata: custom compatible models without it received invented
low/medium/high variants during the live test. The system locations are
`%ProgramData%\opencode\opencode.jsonc` and `/etc/opencode/opencode.jsonc` for
new LLM Glue installations. OpenCode 2.2.7 live-discovered and parsed the Windows
system `.jsonc` path with a line comment. Existing `.json` paths remain compatible.
On MarkIV, a reversible live probe then placed a unique provider/model at that
exact ProgramData path and the effective `/api/model` endpoint exposed it, despite
the installed user allowlist. This qualifies current-user system-path precedence.
`debug config` listed the same system document twice and briefly retained the
deleted source in one immediate post-cleanup invocation; a fresh invocation three
seconds later listed only the user document. Source arrays remain diagnostic, not
effective-state evidence.

An isolated WSL qualification created two actual Linux accounts. For each,
`/etc/opencode/opencode.json` won conflicting provider/model ID, name, endpoint,
capability, and variant values while the account's unrelated provider/model was
preserved. Both effective `/api/model` and source listings were checked. The
harness refuses existing accounts or `/etc/opencode`, then removes only resources
it created. This qualifies all-users precedence on Linux, not Windows.

The M2 generated golden artifact was loaded by the same 2.2.7 executable. Its
effective `/api/model` entry retained the exact `thinking-on` request body,
reported context 65536, and did not invent additional reasoning variants.

On the native Windows host, OpenCode's built-in `ollama` provider discovered all
three local models with native context but no reasoning variants. A live isolated
test showed `disabled_providers: ["ollama"]` removed those entries while all three
enriched `llm-glue-ollama-*` entries remained. Direct Ollama artifacts therefore
disable the built-in provider to avoid duplicates. Client ownership state tracks
only a disablement LLM Glue added; pre-existing and unrelated user values survive
reconciliation and rollback.

The central MarkIV sync initially exposed that prefix-wide cleanup let a remote
artifact erase the locally managed Ollama provider. Ownership schema v2 now
records provider keys and disablements per artifact source URL. A byte-for-byte
restore preceded the corrected rerun; the live migrated target retained all three
local Ollama models, `disabled_providers: [ollama]`, and the central Qwen provider.
Unowned and cross-source provider-key collisions fail closed.

On MarkIV, OpenCode 2.2.7 accepted an operator-owned `enabled_providers`
allowlist, but the current schema deprecates that compatibility input. The
current `policies` schema explicitly supports `provider.use`; a deny for resource
`opencode` removes that built-in provider from the catalog and model selection.
LLM Glue OpenCode targets now default to that exact policy with transactional,
source-scoped ownership while preserving identical operator-owned and unrelated
statements.

Sources: <https://opencode.ai/docs/config>,
<https://opencode.ai/config.json>, <https://opencode.ai/docs/api>, and
<https://github.com/anomalyco/opencode/releases/tag/v2.2.7>.

## LiteLLM 1.109.0

The include path is resolved relative to the declaring YAML. Included lists are
concatenated. Non-list values replace an existing value; mappings are not deeply
merged. Consequently, the generated include is restricted to `model_list`.

`--reload` is documented as development-only. In the live Windows test, changing
the include did not reload; changing the root was noticed but did not produce a
working restarted child. A controlled full process restart loaded the change.
Production activation therefore requires an operator-configured service action,
post-restart health check, and rollback.

Executable forwarding qualification found that the previously parseable
`openai_like/<model>` route is no longer mapped for chat completion requests in
1.109.0. Generated routes therefore use `openai/<model>` with `api_base`; a live
proxy-to-capturing-upstream test verified model alias resolution, response
forwarding, `temperature`, `top_p`, `max_tokens`, and the environment-backed
provider Authorization header. The verified nonstandard variant field
`enable_thinking = true` also arrived unchanged. Version 1.109.0 refuses proxy
startup without a master key by default; the test and deployment guidance
preserve that secure default. OpenCode 2.2.7 accepted both a literal provider
`settings.apiKey` and the documented `{env:LLM_GLUE_INFERENCE_API_KEY}`
substitution. LLM Glue emits the literal form by default and retains the
environment form as a server opt-out. This captured-upstream test is not a substitute for representative
llama.cpp model inference.

The same harness optionally launched OpenCode 2.2.7 with the central golden
artifact. Its real noninteractive `run` path selected `thinking-on`, authenticated
to LiteLLM through the environment reference, resolved the generated alias, and
streamed the captured response. The upstream capture proved OpenCode/LiteLLM sent
`temperature = 0.6` and `enable_thinking = true`. Provider-level `env` metadata is
required in addition to the setting reference for OpenCode to activate the custom
provider.

Post-r4 generation presents aggregated sources under the configurable
`llm-glue` OpenCode namespace while retaining the previously qualified stable
LiteLLM aliases as internal `modelID` values. Golden and reconciliation tests
cover this shape; live OpenCode/Paseo qualification of the concise visible key
remains required before the next release.

Post-r4 LiteLLM aliases also replace provider/instance provenance with the
configurable normalized Server hostname, for example
`labhost03-bench-01-01`. OpenCode references the same alias as its internal
`modelID`. Automated golden and cross-generator tests pass; the renamed alias
still requires end-to-end labhost03 qualification before release.

Reproduce with Python 3.10+ and `litellm[proxy]==1.109.0` installed by running
`make test-litellm PYTHON=/path/to/venv/bin/python`. Add
`OPENCODE=/path/to/opencode` to include the OpenCode 2.2.7 path. The harness binds
only ephemeral loopback ports and uses test credentials.

Sources: <https://github.com/BerriAI/litellm/releases/tag/v1.109.0> and
<https://docs.litellm.ai/docs/proxy/ui_credentials>.

The Debian lab additionally qualified the installed `v1.106.3` image. A
disposable same-image container exposed both a manual root route and a generated
included route, proving `include: [/app/llm-glue-models.yaml]` for that exact
version. The operator root YAML and manual aliases stayed intact; the host
generated file was mounted read-only into the container. A narrowly scoped
systemd unit performed Compose recreation and `/health/liveliness` verification.
A fail-once live unit then proved exact include rollback, prior-state
reactivation, HTTP 200 recovery, and canonical reapply.

## Ollama 0.40.16

The live model reported native context `262144`, completion/vision/tools, and
thinking values `[false]`. No model was loaded, so `/api/ps` correctly supplied
no effective runtime context. An omitted thinking object is unknown; `[false]`
is authoritative non-reasoning; strings and booleans are preserved exactly.

In the later inference run, the standard Qwen 3.5 4B route returned a plain `OK`
with `think=false` and a separate thinking trace plus `OK` with `think=true`.
While loaded, `/api/ps` reported effective context 4096 and equal total/VRAM
sizes of 3,140,495,604 bytes. A loaded-only LLM Glue discovery probe verified
that route without waking either dormant route; the model was then unloaded.

The installed OpenAI-compatible endpoint was also probed with the same model.
`reasoning_effort = none` returned `OK` with no reasoning, while `medium` returned
`OK` with a 400-character reasoning trace. LLM Glue therefore emits verified
`thinking-off` (`none`) and `thinking-on` (`medium`) variants for boolean-thinking
models on the qualified 0.40.16-or-newer contract; named efforts preserve the
reported model-defined name. Earlier versions remain fail-closed without these
profiles. A real installed OpenCode 2.2.7 run selected the generated
`#thinking-on` variant and received `LLM_GLUE_WINDOWS_OK`. The isolated test did
not modify the user's normal OpenCode configuration, exposed no duplicate native
`ollama/<model>` row, and unloaded the model.

A separate secondary native Ollama 0.40.16 instance used isolated manifest and
metadata copies plus temporary same-volume hard links to the existing immutable
blobs. The real composed LLM Glue path propagated an added alias, a same-ID change
from Qwen 3.5 reasoning metadata to Qwen3-VL non-reasoning metadata across an
application restart, transient provider failure/recovery within removal grace,
alias removal, and removal of every isolated model. OpenCode reflected each
catalog transition; after the empty transition it restored its built-in provider.
The temporary store was deleted. A before/after production-store fingerprint
(content hashes for manifests/metadata and size/timestamp inventory for immutable
blobs) and the production tag set were identical.

Sources: <https://github.com/ollama/ollama/blob/main/docs/api.md>,
<https://github.com/ollama/ollama/blob/main/docs/openapi.yaml>, and
<https://github.com/ollama/ollama/blob/main/docs/capabilities/thinking.mdx>, and
<https://github.com/ollama/ollama/blob/main/docs/api/openai-compatibility.mdx>.

## llama.cpp target

`/v1/models` on a direct server represents one loaded model. In live build 8681,
`/models` is also a compatibility alias containing both a `models` array and the
OpenAI-compatible `data` array. The live router also returns `data`, but its
entries carry `source` and nested status evidence. LLM Glue therefore uses entry
shape rather than `data` alone. Router responses report dormant/loaded models,
argument arrays, memory measurements, and provider-confirmed paths;
`/props` provides effective context, generation defaults, and `build_info`.
`/models?reload=1` refreshes router inventory. On the live labhost03 build it
changed the cached response from ten to eleven configured routes and returned
`bench-01-02` as unloaded with text input/output architecture, context 262144,
and measured memory metadata. The r4 candidate then discovered all eleven routes,
generated all eleven LiteLLM aliases and OpenCode entries, and OpenCode 2.2.7
listed every entry without loading `bench-01-02`. Its unknown reasoning field was
omitted rather than represented as unsupported. Local files still require route
validation. Reasoning, effort, budget, and nonstandard sampling fields require
an end-to-end request transport probe. Refresh discovery is bounded by
`discovery_timeout_seconds` (default 300).

Live `/props` from build 8681 reported `reasoning_format: none`. Official llama.cpp
documentation defines this as response parsing behavior: thoughts remain in
`message.content`. It is not evidence that reasoning is unsupported, so LLM Glue
keeps reasoning capability unknown for `none` and every other parsing format
unless separate model and request evidence exists.

The same build reports effective speculative decoding under
`default_generation_settings.params["speculative.type"]`; current upstream also
documents a top-level `speculative` boolean and source emits the plural
`speculative.types` form. The adapter accepts both explicit shapes, normalizes
them to diagnostics, and derives enabled/disabled only when an explicit type is
present (`none` versus a named implementation). A live build-8681 run reported
`speculative.enabled=false` and `speculative.types=none`.

The implemented GGUF path enriches only provider-confirmed routes. A direct
server's `/props.model_path` may identify an aliased route; otherwise the route
must exactly match an allowlisted filename. In either case the canonical file,
including any symlink target, must remain inside a configured directory. It
records `general.architecture`, architecture context, `general.type`, file type,
`split.count`, and positive architecture-scoped `nextn_predict_layers` when
present. The latter becomes `gguf.mtp=present` plus an observed layer count, and
is deliberately not converted into a reasoning or sampling variant. Full-file SHA-256 is disabled by default and
explicitly opt-in. Native Ubuntu/WSL qualification inspected the aliased SmolLM2
GGUF through its reported path and published no local path. Synthetic GGUF
coverage is now complemented by the live Qwen 3.6 35B-A3B Q8 route: GGUF
architecture `qwen35moe`, one NextN/MTP layer, configured/native/effective
context 262144, Q8 KV cache, all GPU layers, `draft-mtp`, and draft maximum 2
were observed. An explicit
`/models=/mnt/local-nvme-01/AI/models/gguf` mapping resolved the container path
without publishing it. A four-part Qwen GGUF reported split count 4. Dormant
Gemma 4 and GPT-OSS metadata were inspected without loading them.

The implemented opt-in functional probe uses `/v1/chat/completions` for llama.cpp
and native `/api/chat` for Ollama, with bounded time and response size. Fixture
tests verify route/text evidence, loaded-only no-load behavior, timeout, failure
redaction, and both request contracts. It deliberately does not claim reasoning,
tool, sampling, or LiteLLM forwarding support; those require the M4 representative
live probes. Qwen and Gemma demonstrated `none` with no reasoning and `medium`
with explicit reasoning. Qwen returned the same behavior through generated
LiteLLM and installed OpenCode variants. GPT-OSS completed both requests but
emitted reasoning even for `none`, so its Off mode remains unknown and is not
advertised. The model itself remains published. Low/high request acceptance was
not treated as proof of distinct effort semantics.

Sources: <https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md>,
<https://github.com/ggml-org/llama.cpp/blob/master/src/llama-arch.cpp>.

## Unqualified environments

- The lab central process now runs under the fixed `llm-glue` account. Debian
  polkit 126 delegates only restart of the exact root-owned LiteLLM Compose unit;
  real sync, health, unrelated-unit denial, persistent-service handoff, and
  authenticated inference passed. Other deployments must install and verify an
  equivalent unit-specific rule; LLM Glue does not grant itself privileges.
- Two-account system precedence is qualified on Linux/WSL. MarkIV current-user
  ProgramData precedence and a temporary second native Windows account under the
  current LocalService-managed JSONC/ownership target are qualified. Windows
  Server 2025 remains exploratory rather than a supported v1 target.
- OpenCode functional inference and CLI variant selection are qualified on native
  Windows; Paseo 0.15.4 catalog and binary-variant presentation are also
  live-qualified.

These limits do not authorize optimistic capability publication.
