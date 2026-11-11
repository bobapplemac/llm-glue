# Troubleshooting

Start with `llm-glue doctor --config <path>`. It reports the selected sidecar,
enabled roles, server/client endpoints, token-environment presence, provider HTTP
reachability, and resolved target paths without printing token values.

## Catalog is empty

- Run `llm-glue discover --config <path>` and inspect field provenance/warnings.
- Ollama must answer `/api/version`, `/api/tags`, and `/api/show`.
- llama.cpp direct mode exposes only the loaded model; router discovery requires a
  compatible `/models`. A GGUF file alone is never a routable deployment.
- Unknown reasoning or unverified routes are intentionally not advertised as
  capabilities.

## OpenCode does not show a model

Run `opencode api GET /api/model --standalone` in the relevant profile. Do not use
`/api/config` as effective state; it lists sources. Confirm OpenCode 2.x and the
system document path. LLM Glue reserves only `llm-glue-*` provider keys.
For a direct Ollama target, confirm `disabled_providers` contains `ollama` and the
adjacent `.llm-glue-ownership.json` is readable by the service identity. This
suppresses OpenCode's lower-fidelity native duplicate; deleting or corrupting the
ownership file prevents safe automatic reversal and should be repaired from the
last-known-good backup or by an operator-reviewed resynchronization.

## LiteLLM synchronization fails

The primary YAML must include the generated file and must not manually own the
same alias. `--reload` is development-only and was unreliable in the qualified
Windows test; configure the actual service name and health URL. A failed restart
restores and reactivates the prior file.

## Service will not start

Run foreground `llm-glue run --config <path>` first. Check that the service
identity can read the sidecar and write configured targets/logs. After relocating
the executable or sidecar, run elevated `service repair`.

## Update failed

A failed check never interrupts synchronization. Verification failures leave the
current executable untouched. Service-mode update and rollback automatically
restore the fallback executable when restart or health verification fails. On
Linux the command reports the combined activation/rollback result directly. On
Windows inspect the printed `update-result.log` or `rollback-result.log`; the
post-exit helper records `success`, `failed and rolled back`, or `failed; rollback
unhealthy`. Do not delete `.rollback` or `.pre-rollback` until healthy.
