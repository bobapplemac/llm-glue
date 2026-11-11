# LLM Glue agent guide

`llm-glue-spec.md` is the product authority. Read it, `docs/progress.md`, the
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
- Generated artifacts must be deterministic and must not contain credentials.
- Network schemas are versioned independently from the product revision.
- Product version remains `r0` until the user authorizes the first release.
- Keep the executable globalization-invariant so the self-contained Linux asset
  remains runnable on minimal Debian 13 without ICU; see ADR 0008.
- Add or update tests with every behavioral change. Mark live-environment tests
  separately from fixture/mock tests.
- Update durable documentation and `docs/progress.md` at each milestone.
- Do not publish releases or modify inference services outside an explicitly
  configured local target.

## Verification

Use `dotnet build`, `dotnet test`, and the platform integration scripts. Release
scripts must be exercised in dry-run mode only unless publication is explicitly
authorized. Never commit secrets, machine-specific paths, raw prompts, or
unsanitized runtime output.
