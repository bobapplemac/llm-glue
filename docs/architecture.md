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

For a provider-confirmed llama.cpp route, optional GGUF enrichment requires an
exact route-basename match inside an allowlisted directory. Canonicalized paths,
including symlink targets, are checked before bounded metadata reads. Metadata may
fill otherwise-unknown native context, architecture, GGUF type, and multipart
count; it cannot establish routing or reasoning support. Local paths are never
placed in the network model. Whole-file SHA-256 is separately opt-in because model
files can be very large.

Functional text-route probes are also off by default. `loaded-only` probes only a
deployment the runtime already reports loaded; `allow-load` is the explicit opt-in
that may wake a dormant model. Each probe has its own timeout and one-token response
limit plus a one-MiB response cap. Success adds verified text-route evidence only.
It never infers tools, reasoning modes, sampling transport, or absence of a feature;
failure makes that deployment unavailable for publication without exposing the
provider response body.

## Configuration ownership

OpenCode synchronization performs scoped reconciliation inside the documented
system-managed document. It owns only its deterministic provider namespace and
model keys and preserves every unrelated property. User and project documents
are read only.

LiteLLM synchronization owns one generated include whose content is limited to
`model_list`. The operator owns the primary YAML and must authorize the include
once. Activation uses an explicitly configured, narrowly allowlisted local
restart/health-check mechanism; it does not rely on development auto-reload.

Central llama.cpp deployments set `server.opencode_inference_url` to the
client-reachable LiteLLM base URL. Their OpenCode artifact uses that URL and the
same deterministic aliases generated into LiteLLM, while the LiteLLM include
continues to use each private llama.cpp route. Configuration validation rejects a
Server/LiteLLM target without this boundary. Standalone Ollama deployments omit
the setting and continue to publish direct provider routes. Central provider
settings reference `LLM_GLUE_INFERENCE_API_KEY` through OpenCode's `{env:...}`
substitution and declare that environment source at provider level so OpenCode
activates the custom provider; the secret itself never enters an artifact.

See [ADR 0001](decisions/0001-configuration-ownership-and-activation.md) and
[ADR 0005](decisions/0005-central-litellm-routing.md).

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
Agent-only mode submits through that authenticated HTTP boundary and retains its
local grace state for retry. Server publication is serialized and refuses older
snapshots so a delayed request cannot roll artifacts backward.

Authentication bypass for a loopback peer is enabled only when `server.listen`
is also loopback. A Server bound for remote access requires bearer credentials
even when the immediate connection comes from a same-host TLS reverse proxy; its
co-located Client uses the read credential as well.

Exact, conjunctive `[[server.exclusions]]` rules are evaluated only at the
publication boundary. Hidden deployments remain in inventory/model diagnostics
with their exclusion reason but are omitted from both target artifacts; provider
state is never changed. See [ADR 0004](decisions/0004-inventory-publication-and-exclusions.md).

The Server commits each validated inventory plus its generated artifacts into a
configured state directory before changing in-memory visibility. A durable current
marker selects a state ID derived from both inventory and artifact hashes, so a
generator change is retained even when provider inventory is unchanged. Startup
restores only schema- and hash-valid state; corrupt or partial state fails closed.
Retention deletes only owned hash-named revision directories.

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

Service registration uses stable least-privilege identities: LocalService on
Windows and the non-login `llm-glue` system account on Linux. The installer grants
read/execute access to the executable, read access to the sidecar, and write
access only to resolved state, log, and target directories. Linux systemd
hardening additionally uses `ProtectSystem=strict`, `ProtectHome=read-only`, and
quoted `ReadWritePaths` entries. Existing Linux writable directories must already
authorize the service account; only newly created directories receive group
permissions from LLM Glue.

The generic host content root is the configured log directory rather than the
executable directory. LLM Glue serves no static content, and this prevents
ASP.NET from requiring directory-list permission around a portable single-file
binary while retaining explicit configuration-relative data paths.

Update discovery is pinned to official stable GitHub `rN` releases. Assets are
RID-specific and checksum-verified before staging; the current executable is
retained as `.rollback`. Service-mode replacement coordinates the fixed SCM or
systemd identity, verifies the LLM Glue health endpoint/running state, and restores
and reverifies the fallback on activation failure or interruption. Windows uses a
post-exit helper with a durable result log. Release tooling implements the same
fail-closed contract on GNU Make/bash and PowerShell. See ADR 0003.

Console and bounded rolling-file output flow through
`Microsoft.Extensions.Logging` into NLog. Every HTTP response carries a bounded,
sanitized `X-Correlation-ID`; a valid caller value is preserved and an unsafe or
missing value is replaced. Request handling and each background discovery or sync
cycle place that ID in a logging scope, alongside structured provider, target,
model revision, and result fields where applicable. Bodies, prompts, and tokens
are never logged.
