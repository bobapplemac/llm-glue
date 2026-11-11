# Compatibility and integration evidence

Evidence date: 2026-11-11. “Live” means executed in the current Windows test
environment; “source” means inspected official documentation or the installed
release source. Live production qualification is tracked separately.

| Integration | Version | Evidence | Result |
|---|---:|---|---|
| OpenCode | 2.2.7 | Live Windows executable, two native Linux accounts, official schema/docs | System config wins conflicts for both accounts; nonconflicting user/project values survive; effective models available at `/api/model`; source arrays are not effective config; watched files apply without explicit reload in this test. |
| LiteLLM Proxy | 1.109.0 | Live isolated Windows install and installed source | Relative YAML includes work; lists concatenate; non-list include values replace rather than deep-merge. Development `--reload` did not watch the included file and did not reliably restart after a root edit on Windows. |
| Ollama | 0.40.16 | Live local HTTP API, OpenAI-compatible inference, and OpenCode 2.2.7 | `/api/tags`, `/api/show`, `/api/ps`, and `/api/version` match documented shapes. Native and `reasoning_effort` Off/On thinking, effective/native context separation, full VRAM residency, loaded-only route probing, named variant selection, and real OpenCode inference passed for Qwen 3.5 4B. |
| llama.cpp | v0.9.0 stable target | Official server documentation/source | Direct `/v1/models`, router `/models`, and `/props` have distinct discovery roles. Runtime executable/model qualification remains pending. |
| .NET SDK | 10.0.401 | Live | Build toolchain available on Windows. |
| .NET SDK (Ubuntu/WSL) | 10.0.115 | Live native Linux build/test/systemd | GNU Make restore/build/test, linux-x64 execution, and isolated fixed-account systemd install/repair/rollback/uninstall passed. |
| Tomlyn | 2.10.2 | Live NuGet restore/build on net10.0 | Compatible parser selected; strict binding tests are an M1 gate. |
| YamlDotNet | 18.1.0 | Automated net10.0 target tests | LiteLLM primary/generated YAML parsing, include and alias ownership checks. |
| NLog.Extensions.Logging | 6.2.2 | Build/test | Standard logging provider with console and bounded rolling file targets. |
| LLM Glue release outputs | r1 qualification build | Live Windows and Ubuntu/WSL execution | Both RID outputs are single files and report `LLM Glue r1`; Windows FileVersion is `1.1.0.0`. No release was published. |

## OpenCode 2.2.7

Current schema uses `providers` and supports explicit model `capabilities`,
`reasoning`, `limit`, settings, and variants. The generator must emit explicit
capability metadata: custom compatible models without it received invented
low/medium/high variants during the live test. The system locations are
`%ProgramData%\opencode\opencode.json` and `/etc/opencode/opencode.json`.

An isolated WSL qualification created two actual Linux accounts. For each,
`/etc/opencode/opencode.json` won conflicting provider/model ID, name, endpoint,
capability, and variant values while the account's unrelated provider/model was
preserved. Both effective `/api/model` and source listings were checked. The
harness refuses existing accounts or `/etc/opencode`, then removes only resources
it created. This qualifies all-users precedence on Linux, not Windows.

The M2 generated golden artifact was loaded by the same 2.2.7 executable. Its
effective `/api/model` entry retained the exact `thinking-on` request body,
reported context 65536, and did not invent additional reasoning variants.

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
preserve that secure default. Central OpenCode output references the client
credential with the documented `{env:LLM_GLUE_INFERENCE_API_KEY}` substitution. This
captured-upstream test is not a substitute for representative
llama.cpp model inference.

The same harness optionally launched OpenCode 2.2.7 with the central golden
artifact. Its real noninteractive `run` path selected `thinking-on`, authenticated
to LiteLLM through the environment reference, resolved the generated alias, and
streamed the captured response. The upstream capture proved OpenCode/LiteLLM sent
`temperature = 0.6` and `enable_thinking = true`. Provider-level `env` metadata is
required in addition to the setting reference for OpenCode to activate the custom
provider.

Reproduce with Python 3.10+ and `litellm[proxy]==1.109.0` installed by running
`make test-litellm PYTHON=/path/to/venv/bin/python`. Add
`OPENCODE=/path/to/opencode` to include the OpenCode 2.2.7 path. The harness binds
only ephemeral loopback ports and uses test credentials.

Sources: <https://github.com/BerriAI/litellm/releases/tag/v1.109.0> and
<https://docs.litellm.ai/docs/proxy/ui_credentials>.

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
not modify the user's normal OpenCode configuration and unloaded the model.

Sources: <https://github.com/ollama/ollama/blob/main/docs/api.md>,
<https://github.com/ollama/ollama/blob/main/docs/openapi.yaml>, and
<https://github.com/ollama/ollama/blob/main/docs/capabilities/thinking.mdx>, and
<https://github.com/ollama/ollama/blob/main/docs/api/openai-compatibility.mdx>.

## llama.cpp target

`/v1/models` on a direct server represents one loaded model. Router `/models`
can report dormant and loaded models plus effective arguments; `/props` provides
effective context and generation defaults. `/models?reload=1` refreshes router
inventory. Local files still require route validation. Reasoning, effort, budget,
and nonstandard sampling fields require an end-to-end request transport probe.

The implemented GGUF path enriches only provider-confirmed routes with an exact
allowlisted filename. It records `general.architecture`, architecture context,
`general.type`, file type, and `split.count` when present. Full-file SHA-256 is
disabled by default and explicitly opt-in. This fixture-qualified metadata path
does not qualify live multipart/MTP classification or reasoning behavior.

The implemented opt-in functional probe uses `/v1/chat/completions` for llama.cpp
and native `/api/chat` for Ollama, with bounded time and response size. Fixture
tests verify route/text evidence, loaded-only no-load behavior, timeout, failure
redaction, and both request contracts. It deliberately does not claim reasoning,
tool, sampling, or LiteLLM forwarding support; those require the M4 representative
live probes.

Source: <https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md>.

## Unqualified environments

- Debian `labhost03`, actual llama.cpp models, MTP/draft artifacts, and LiteLLM
  service ownership are unavailable in this workspace.
- Two-account system precedence is qualified on Linux/WSL, but not Windows or
  Windows Server 2025.
- Gemma and GPT-OSS runtime behavior is represented only by sanitized recorded
  fixtures until live qualification.
- OpenCode functional inference and CLI variant selection are qualified on native
  Windows; Paseo UI behavior remains unqualified.

These limits do not authorize optimistic capability publication.
