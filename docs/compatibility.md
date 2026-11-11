# Compatibility and integration evidence

Evidence date: 2026-11-11. “Live” means executed in the current Windows test
environment; “source” means inspected official documentation or the installed
release source. Live production qualification is tracked separately.

| Integration | Version | Evidence | Result |
|---|---:|---|---|
| OpenCode | 2.2.7 | Live Windows executable, official schema/docs | System config wins conflicts; nonconflicting user/project values survive; effective models available at `/api/model`; source arrays are not effective config; watched files apply without explicit reload in this test. |
| LiteLLM Proxy | 1.109.0 | Live isolated Windows install and installed source | Relative YAML includes work; lists concatenate; non-list include values replace rather than deep-merge. Development `--reload` did not watch the included file and did not reliably restart after a root edit on Windows. |
| Ollama | 0.40.16 | Live local HTTP API | `/api/tags`, `/api/show`, `/api/ps`, and `/api/version` match the current documented shapes. `/api/show` supplied native context and explicit thinking metadata. |
| llama.cpp | v0.9.0 stable target | Official server documentation/source | Direct `/v1/models`, router `/models`, and `/props` have distinct discovery roles. Runtime executable/model qualification remains pending. |
| .NET SDK | 10.0.401 | Live | Build toolchain available on Windows. |
| .NET SDK (Ubuntu/WSL) | 10.0.115 | Live native Linux build/test | GNU Make restore/build/test and linux-x64 executable startup passed. |
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

Tests using two isolated user configuration roots showed system values winning
the same provider/model key while unique system, user, and project models were
preserved. These are simulated profiles, not two actual OS accounts; the latter
remains an M4 qualification item.

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

Sources: <https://github.com/BerriAI/litellm/releases/tag/v1.109.0> and
<https://docs.litellm.ai/docs/proxy/ui_credentials>.

## Ollama 0.40.16

The live model reported native context `262144`, completion/vision/tools, and
thinking values `[false]`. No model was loaded, so `/api/ps` correctly supplied
no effective runtime context. An omitted thinking object is unknown; `[false]`
is authoritative non-reasoning; strings and booleans are preserved exactly.

Sources: <https://github.com/ollama/ollama/blob/main/docs/api.md>,
<https://github.com/ollama/ollama/blob/main/docs/openapi.yaml>, and
<https://github.com/ollama/ollama/blob/main/docs/capabilities/thinking.mdx>.

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
- Two real Windows/Linux OS accounts and Windows Server 2025 are not available.
- Gemma and GPT-OSS runtime behavior is represented only by sanitized recorded
  fixtures until live qualification.
- OpenCode/Paseo functional inference and variant UI require M4 deployment.

These limits do not authorize optimistic capability publication.
