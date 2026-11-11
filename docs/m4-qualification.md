# M4 live qualification

Date: 2026-11-10

## Standalone Windows/Ollama — partial pass

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
OpenCode catalog visibility. It does **not** validate functional inference,
effective loaded context, parameter transport, model add/remove propagation,
service restart recovery, or Paseo UI behavior. No production configuration was
modified; the run used isolated temporary paths and all processes were stopped.

## Windows service lifecycle — pass

An elevated, isolated all-role r1 qualification ran as
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

The run also exposed and led to fixes for two real issues: duplicate DI adapter
registration caused duplicate model identities on the second cycle, and the
default Windows Event Log provider failed under a non-elevated identity. The
application now registers each adapter once and uses explicit portable logging
providers.

## Debian llama.cpp/LiteLLM — not run

`labhost03`, its model directories, llama.cpp router, LiteLLM service identity,
and representative Qwen/Gemma/GPT-OSS/MTP models are unavailable in this
environment. The following remain mandatory:

- Real direct/router discovery and GGUF multipart/draft classification.
- LiteLLM include deployment followed by controlled service restart and health.
- Captured Off/On and effort-level request bodies at llama.cpp.
- Functional inference and context/KV/runtime observation.
- Model add, change, removal, outage, stale-grace, restart, and rollback scenarios.
- Native systemd install/repair/restart/update/rollback/uninstall behavior.

## Multi-user and UI — not run

Two actual OS accounts and Paseo are unavailable. System precedence has only the
M0 isolated-profile evidence. Real all-users behavior and OpenCode/Paseo variant
selection remain gate items.

## Gate status

M4 is **not passed**. The available Windows catalog path is verified; the
remaining live requirements cannot be simulated or waived.
