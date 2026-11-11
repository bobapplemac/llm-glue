# M0 evidence and integration design

## Gate conclusion

M0 is sufficient to begin implementation with explicit version gates and
fail-closed unknown states. Destructive ownership and merge semantics are resolved
by ADR 0001. Live production qualification items remain M4 gates and are not
represented as production-ready.

## Capability matrix

| Required capability | Discovery source | Verification | Unknown/fallback behavior |
|---|---|---|---|
| Provider/runtime version | Ollama `/api/version`; llama.cpp response/build metadata | Version range adapter | Mark unsupported version; diagnostics only |
| Routable model | Ollama tags/show; llama direct `/v1/models` or `/models` with OpenAI `data`; router-only `/models` shape | Provider route and bounded request probe | Never publish a file-only discovery |
| Native context | Ollama show `*.context_length`; allowlisted GGUF metadata selected by an exact route or provider-reported path; llama model metadata | Typed numeric and canonical-path validation | Null/unknown |
| Effective context | Ollama `/api/ps` when loaded; llama `/props` or router effective args | Compare live response to configured state | Null; never substitute native max |
| Modalities/tools | Provider capability fields, GGUF architecture, bounded probe | Per-field evidence | Tri-state unknown |
| Reasoning modes | Ollama show `thinking`; llama versioned flags/model evidence (`reasoning_format` controls output parsing, not capability) | End-to-end request/response probe for controllable modes | Omit modes; never infer from name or a response parsing format |
| Sampling transport | Versioned provider/LiteLLM path | Mock capture plus live representative probes | Drop unsupported fields with warning |
| Runtime residency/KV/MTP | Runtime live APIs/args; architecture-scoped GGUF `nextn_predict_layers` | Reported versus observed labels; versioned `/props` shapes | Diagnostics only; not client variants |
| LiteLLM route | Generated include and proxy model endpoint | Restart, `/v1/models`, functional completion | Roll back and retain LKG |
| OpenCode visibility | Generated 2.x schema and effective `/api/model` | Model/variant selection | Reject incompatible schema/version |

## Configuration transcripts

The files under `tests/fixtures/m0` are sanitized and contain no production
credentials. Their provenance labels distinguish live capture, official API-shape
recording, and specification reference observations.

### Qwen reference through llama.cpp and LiteLLM

Recorded reference only; not a universal family rule:

```text
Thinking Off -> enable_thinking=false, temperature=.7, top_p=.8,
                top_k=20, min_p=0, presence_penalty=1.5, repeat_penalty=1
Thinking On  -> enable_thinking=true, temperature=.6, top_p=.95,
                top_k=20, min_p=0, presence_penalty=0, repeat_penalty=1
```

These profiles may be emitted only for the exact qualified deployment/path.

### Gemma and GPT-OSS

Sanitized API-shape fixtures exercise alternative architecture metadata and
discrete reasoning effort levels. They are not live-qualified in this environment.
Adapters must leave runtime acceptance unknown until bounded probes succeed.

### Ollama

The live Windows-compatible probe of 0.40.16 returned one Qwen3-VL model with
completion, vision, and tools; native context 262144; and thinking `[false]`.
`/api/ps` was empty, so no effective loaded context was claimed.

A later Qwen 3.5 4B probe verified the installed OpenAI-compatible transport:
`reasoning_effort=none` disabled thinking and `medium` enabled it. This corrects
the earlier conservative omission of direct-Ollama named variants; the qualified
adapter now emits explicit `thinking-off`/`thinking-on` request profiles only for
the verified 0.40.16-or-newer contract (ADR 0006).

## OpenCode transcript summary

Two isolated user configuration roots plus a shared system root and project file
were tested with 2.2.7. `/api/config` returned sources. `/api/model` returned the
effective merged catalog: the system value won the conflicting model, and unique
models from every layer survived. Editing the system document triggered watched
updates and changed effective state before explicit reload. Missing explicit
capabilities caused OpenCode to invent reasoning variants, requiring conservative
explicit metadata. A subsequent isolated WSL test repeated precedence with two
actual Linux accounts and proved the system conflict wins while unrelated user
models survive for each account.

## LiteLLM transcript summary

1.109.0 parsed a primary config including a relative generated YAML. Operator and
generated `model_list` entries coexisted and unrelated settings survived. Installed
source confirmed list concatenation and replacement—not deep merge—for other
values. An included-file change was not watched. A root-file change was noticed by
`--reload` but did not produce a working restart on this Windows run. A controlled
restart applied the generated change. Production design therefore uses validated
atomic replacement plus configured restart/health/rollback.

Later executable forwarding qualification corrected two assumptions that parsing
could not validate: chat routes must use `openai/<model>` plus `api_base` because
1.109.0 rejects `openai_like/<model>` as unmapped, and central OpenCode artifacts
must use the public LiteLLM URL plus generated alias rather than the private
llama.cpp discovery endpoint. LiteLLM also requires a master key by default.
These corrections are captured in ADR 0005 and a repeatable captured-upstream test.
The same test subsequently drove OpenCode 2.2.7 through LiteLLM; it established
that the generated custom provider must declare its credential environment name
at provider level as well as reference it in settings before OpenCode routes it.

## TOML choice

Tomlyn 2.10.2 is the selected maintained .NET TOML parser. Its package restored
and built successfully in a .NET 10 validation project. Configuration loading binds a
parsed syntax tree into explicit option types, rejects unknown/duplicate or
ill-typed settings, and reports parser line/column diagnostics. The specification's
Linux example and Windows/Ollama and remote-client examples are maintained as
test fixtures and validated by the M1 configuration test suite.

## Remaining qualification

The later M4 run qualified actual llama.cpp Qwen/Gemma/GPT-OSS/MTP inference,
LiteLLM forwarding, lifecycle, outage recovery, and rollback on Debian 13.7 plus
MarkIV OpenCode. Paseo UI presentation and least-privilege delegation of the lab
Compose restart unit subsequently passed. Windows multi-account/system-target
behavior remains mandatory.
