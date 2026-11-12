# LLM Glue agent guide

`docs/llm-glue-spec.md` is the product authority. Read it, `docs/progress.md`, the
relevant ADRs, and `docs/compatibility.md` before changing behavior.

## Engineering rules

- Target .NET 10 and C# with nullable reference types and warnings enabled.
- Keep provider discovery, normalized domain state, capability resolution,
  artifact generation, and target installation separate.
- Unknown is a first-class capability state. Never infer support from a model
  name or silently convert unknown to false.
- Distinguish native model limits, configured runtime values, reported live
  values, and behavior verified by a probe.
- Preserve operator-owned configuration. LLM Glue may reconcile only keys or
  files explicitly assigned to it.
- Direct Ollama/OpenCode output must suppress the duplicate built-in `ollama`
  provider. Preserve unrelated and pre-existing `disabled_providers` values and
  keep the adjacent generated ownership state transactional with target apply.
  OpenCode provider ownership is scoped by artifact source; one controller must
  never remove another controller's provider keys. The default built-in
  `opencode` deny policy follows the same rule: preserve identical operator-owned
  statements and every unrelated policy, and remove only an owned statement.
- OpenCode sources aggregate into the configurable `llm-glue` namespace by
  default. Namespace overrides use exact provider/instance identity and first
  match wins. Never invent unstable suffixes: duplicate model IDs or multiple
  direct endpoints in one namespace must fail with an actionable split request.
- LiteLLM aliases use the configurable Server-scoped namespace, defaulting to a
  normalized machine hostname. Central OpenCode `modelID` must use the exact same
  alias while its visible provider remains governed by the OpenCode namespace.
- Generated artifacts must be deterministic. LiteLLM YAML remains credential-free;
  authenticated OpenCode artifacts embed the configured inference key by default
  under ADR 0017. Never put real credentials in fixtures, logs, diagnostics, or
  unauthenticated responses.
- New Windows service bootstraps default to `%ProgramData%\LLMGlue`; non-service
  installs may remain per-user. Never silently migrate an existing installation
  or shadow an existing `.toml` sidecar with a newly generated `.conf` file.
- Agent model exclusions are a pre-export boundary; excluded inventory must not
  be submitted upstream. Server exclusions retain diagnostic visibility while
  filtering generated artifacts. Exact and wildcard semantics must not be
  conflated.
- Network schemas are versioned independently from the product revision.
- Ordinary development and test builds identify as `r0`. Only the guarded
  publisher may assign an explicitly authorized official `rN` revision; the
  current stable release is `r4`, with stable `AssemblyVersion` 1.0.0.0 and
  revision-tracking `FileVersion` 1.N.0.0.
- Keep the executable globalization-invariant so the self-contained Linux asset
  remains runnable on minimal Debian 13 without ICU; see ADR 0008.
- Add or update tests with every behavioral change. Mark live-environment tests
  separately from fixture/mock tests.
- Update durable documentation and `docs/progress.md` at each milestone.
- Every release revision requires explicit user authorization. Do not publish
  through an unguarded path or modify inference services outside an explicitly
  configured local target.

## Verification

Use `dotnet build`, `dotnet test`, and the platform integration scripts. Release
scripts must be exercised in dry-run mode only unless publication is explicitly
authorized. Never commit secrets, machine-specific paths, raw prompts, or
unsanitized runtime output.
