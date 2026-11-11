# Troubleshooting

Start with `llm-glue doctor --config <path>`. It reports the selected sidecar,
enabled roles, server/client endpoints, token-environment presence, provider HTTP
reachability, and resolved target paths without printing token values. Exit code
zero means every configured secret source was available and every provider probe
returned success. Missing secret environments, connection failures, and non-2xx
provider responses produce exit code one, so the command is safe to use in
bootstrap and monitoring scripts.

## Catalog is empty

- Run `llm-glue discover --config <path>` and inspect field provenance/warnings.
- Ollama must answer `/api/version`, `/api/tags`, and `/api/show`.
- llama.cpp direct mode exposes only the loaded model; router discovery requires a
  compatible `/models?reload=1`. A GGUF file alone is never a routable deployment.
  If a changed preset sweep exceeds the default five minutes, increase that
  provider's `discovery_timeout_seconds`.
- Dormant router routes are published. Unknown reasoning, tools, or modalities are
  omitted from capability declarations until evidence is available.

## OpenCode does not show a model

Run `opencode api GET /api/model --standalone` in the relevant profile. Do not use
`/api/config` as effective state; it lists sources. Confirm OpenCode 2.x and the
system document path. LLM Glue changes only provider keys recorded for that
artifact source in the adjacent ownership state; an unowned `llm-glue-*`
collision fails closed.
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

If an r1 Linux update reports missing `System.IO.Pipes`, do not retry it. The
message is caused by replacing the active single-file bundle before a lazily
loaded runtime assembly is needed; the extracted Linux file also lacks its
executable mode. Confirm the original service is healthy and both the installed
binary and `.rollback` report the expected prior revision. Restore the fallback
with a conventional stopped-service copy if necessary, and use the corrective
release's bootstrap/manual upgrade path rather than r1 self-update.

A failed check never interrupts synchronization. Verification failures leave the
current executable untouched. Service-mode update and rollback automatically
restore the fallback executable when restart or health verification fails.
Corrected Linux and Windows releases print an `update-result.log` or
`rollback-result.log` path for their post-exit activator. The helper records
`success` or a failure description. Do not delete `.rollback` or `.pre-rollback`
until the service is healthy.
