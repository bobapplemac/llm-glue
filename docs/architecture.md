# Architecture

## Boundaries

LLM Glue is a single .NET 10 application with composable Agent, Server, and
Client hosted services. The domain model is independent of provider and target
schemas.

```text
Provider APIs/files -> discovery adapters -> normalized deployments
                    -> reconciliation/capability resolution
                    -> versioned artifact generators -> HTTP publication
                    -> client staging/validation -> target adapters
```

Discovery records evidence per field, including source, timestamp, confidence,
and verification status. Static model traits, configured deployment traits,
runtime-reported effective state, and empirically observed behavior are distinct.

## Provider integration

Ollama discovery uses `/api/tags`, `/api/show`, `/api/ps`, and `/api/version`.
llama.cpp discovery distinguishes direct single-model servers from model routers
using `/v1/models`, `/models`, and `/props`. Local GGUF inspection is constrained
to configured directory allowlists; a file is never considered routable merely
because it exists.

## Configuration ownership

OpenCode synchronization performs scoped reconciliation inside the documented
system-managed document. It owns only its deterministic provider namespace and
model keys and preserves every unrelated property. User and project documents
are read only.

LiteLLM synchronization owns one generated include whose content is limited to
`model_list`. The operator owns the primary YAML and must authorize the include
once. Activation uses an explicitly configured, narrowly allowlisted local
restart/health-check mechanism; it does not rely on development auto-reload.

See [ADR 0001](decisions/0001-configuration-ownership-and-activation.md).

## Network and persistence

Network contracts use `/api/v1`. Artifacts are immutable by revision, expose
content ETags, and retain last-known-good history. The embedded server is HTTP
only. It defaults to loopback; remote access requires bearer authentication and
an encrypted private network or operator-supplied reverse proxy unless the
operator explicitly acknowledges insecure transport.

Server publication generates and validates all target artifacts before swapping
the visible inventory, then stores bounded per-target history. Conditional reads
return 304 for matching ETags. Agent submissions must carry a canonical content
hash and schema version; remote reads and writes use separate bearer credentials.

Client installation is staged on the destination filesystem, validated, backed
up, atomically renamed, activated, verified, and rolled back on failure. A local
lock prevents concurrent reconciliation. Offline clients keep the last-known-good
artifact.

The OpenCode target parses JSON/JSONC, removes/replaces only the reserved
`llm-glue-*` provider namespace, and retains all other properties. The LiteLLM
target parses both YAML documents, requires the authorized include, rejects
operator/generated alias collisions, and restarts only a syntactically constrained
local service identity before checking its configured health URL.

## Configuration

There is one human-managed TOML sidecar, `llm-glue.conf`. Explicit `--config`
wins, followed by an executable-side sidecar and, on Linux, `/etc/llm-glue.conf`.
Relative data paths resolve against the selected config directory.

## Operations and release

The executable hosts under Windows Service or systemd using official .NET 10
hosting integrations. Registration stores absolute executable/config paths and is
separate from target-service activation. Local administrative operations call
fixed OS tools with argument lists and never a command shell.

Update discovery is pinned to official stable GitHub `rN` releases. Assets are
RID-specific and checksum-verified before staging; the current executable is
retained as `.rollback`. Release tooling implements the same fail-closed contract
on GNU Make/bash and PowerShell. See ADR 0003.
