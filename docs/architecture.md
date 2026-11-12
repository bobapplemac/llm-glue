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
using `/v1/models`, `/models?reload=1`, and `/props`. The refresh query makes
router preset additions/removals visible and may run bounded metadata measurement
children without loading dormant models for inference. Because current direct
servers and routers can both serve a top-level `data` array, entry-level nested status/source
evidence identifies router inventory; plain OpenAI model entries use direct mode.
Agent discovery runs immediately at service start and then waits 60 seconds by
default after each completed cycle. `agent.discovery_interval_seconds` can tune
that cadence. Client conditional artifact polling independently defaults to 60
seconds and is configurable through `client.poll_interval_seconds`.
Local GGUF inspection is constrained
to configured directory allowlists; a file is never considered routable merely
because it exists.

For a provider-confirmed llama.cpp route, optional GGUF enrichment uses the
direct server's reported `model_path` or an exact route-basename match, and only
when the resolved file remains inside an allowlisted directory. Canonicalized
paths, including symlink targets, are checked before bounded metadata reads. Metadata may
fill otherwise-unknown native context, architecture, GGUF type, multipart count,
and an architecture-scoped positive `nextn_predict_layers` MTP marker. MTP is
retained only as a runtime artifact trait; it cannot establish routing, reasoning
support, or a client-selectable variant. Direct-server `/props` speculative state
is recorded separately as effective runtime configuration. Local paths are never
placed in the network model. Whole-file SHA-256 is separately opt-in because model
files can be very large.

Containerized routers can report a path such as `/models/x.gguf` that is not the
host agent's mount path. `model_path_mappings` explicitly maps a provider prefix
to a host directory before the same canonical allowlist check; no basename or
model-name guess is permitted.

Functional text-route probes are also off by default. Router inventory membership
is independent of loaded state: an available dormant route is published with
unknown capability fields omitted. `loaded-only` probes only a
deployment the runtime already reports loaded; `allow-load` is the explicit opt-in
that may wake a dormant model. Each probe has its own timeout, bounded generation,
and a one-MiB response cap. Success adds verified text-route evidence. For
llama.cpp only, an already-loaded route with unknown reasoning receives two more
bounded requests: `reasoning_effort=none` and `medium`. Binary Off/On profiles are
published only when the first returns assistant content without reasoning and the
second returns explicit nonempty `reasoning_content`. Every other result remains
unknown; tools and named effort semantics are never inferred.

## Configuration ownership

OpenCode synchronization performs scoped reconciliation inside the documented
explicitly configured document. The path may select the system file or one of
several named user files; profiles are never enumerated. Artifacts are fetched
once per target type and applied independently, with the conditional-read state
advancing only after all selected destinations succeed. Adjacent ownership schema
v2 scopes provider keys and
native-provider disablements by artifact source URL, so local Ollama and remote
central controllers can share one target. V1 state migrates through a legacy
scope and transfers keys when their current source next claims them. Unowned and
cross-source collisions fail closed. Every unrelated property is preserved;
user and project documents are read only.
The default destination uses the `.jsonc` extension. Reconciliation preserves
comment-bearing raw syntax for unowned root properties, providers, and disabled
provider entries while regenerating owned values under explicit managed comments.
An explicitly configured legacy `.json` destination remains accepted.
For a direct Ollama deployment, the generated document also disables OpenCode's
built-in `ollama` provider so enriched models do not appear twice. The target
merges `disabled_providers`, preserves unrelated and pre-existing values, and
records only entries it added in adjacent generated ownership state. A
destination-scoped process lock covers read/merge/write; activation rollback
restores both configuration and ownership state.

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
settings embed the resolved LiteLLM client key in the authenticated OpenCode
artifact by default, so remote clients need only their LLM Glue read token. Set
`embed_opencode_inference_api_key = false` to restore the
`{env:LLM_GLUE_INFERENCE_API_KEY}` provider setting and provision that variable
to every OpenCode process. LiteLLM YAML remains credential-free in either mode.
OpenCode presentation is intentionally separate from the internal route identity.
All published sources aggregate under the server's `opencode_namespace`, which
defaults to `llm-glue`, so normal selections read `llm-glue/model`. Ordered
`[[server.opencode_namespace_overrides]]` rules can match exact `provider_id`
and/or `instance_id` values and split those sources into another namespace. The
instance ID is the configured `[[agent.providers]].name`. Collision-safe LiteLLM
aliases remain as the internal `modelID`. Duplicate model IDs within a namespace,
or multiple direct endpoints assigned to one namespace, fail publication with an
actionable split requirement rather than receiving an unstable automatic suffix.
LiteLLM route identity is separately Server-scoped. `litellm_namespace` defaults
to a lowercase, hyphen-normalized local hostname, so labhost03 normally publishes
`labhost03-model-id`. The same alias is written to LiteLLM and used as central
OpenCode's internal `modelID`; it does not alter the visible `llm-glue/model-id`.
Operators can pin the namespace when a hostname is ephemeral or when moving a
configuration without changing existing routes. Duplicate normalized aliases
within one LiteLLM namespace fail generation instead of acquiring unstable suffixes.
Each OpenCode target also defaults to an owned `provider.use` deny policy for the
built-in `opencode` provider. Ownership schema v3 distinguishes that managed
statement from identical or unrelated operator policies and migrates v1/v2 state.

See [ADR 0001](decisions/0001-configuration-ownership-and-activation.md),
[ADR 0005](decisions/0005-central-litellm-routing.md), and
[ADR 0009](decisions/0009-live-router-paths-probes-and-source-ownership.md), and
[ADR 0010](decisions/0010-explicit-opencode-target-scope.md), and
[ADR 0013](decisions/0013-owned-opencode-catalog-policy.md), and
[ADR 0018](decisions/0018-concise-opencode-namespaces.md), and
[ADR 0019](decisions/0019-server-scoped-litellm-aliases.md).

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

The OpenCode target parses JSON/JSONC, removes/replaces only exact provider keys
recorded for its artifact source, and retains all other properties. The LiteLLM
target parses both YAML documents, requires the authorized include, rejects
operator/generated alias collisions, and restarts only a syntactically constrained
local service identity before checking its configured health URL.

## Configuration

There is one human-managed TOML configuration, named either `llm-glue.conf` or
`llm-glue.toml`. Explicit `--config` wins, followed by `.conf` then `.toml`
beside the executable, then the same order under `/etc/llm-glue` on Linux or
`%ProgramData%\LLMGlue` on Windows. Legacy Linux root-level files remain final
fallbacks. Agent exclusion patterns remove matching inventory before publication
or submission; server exclusion patterns retain diagnostics while filtering
generated artifacts.
Relative data paths resolve against the selected config directory.

The typed configuration catalog in `ReferenceDocumentation` is also the source
of truth for accepted keys: TOML validation derives each scope's allow-list from
it. The same catalog generates `docs/configuration-reference.md`. A parallel CLI
catalog generates both built-in help and `docs/cli-reference.md`. This makes
missing setting documentation and stale hand-maintained usage text build-time
concerns instead of release-time archaeology; see ADR 0020.

## Operations and release

The executable hosts under Windows Service or systemd using official .NET 10
hosting integrations. Registration stores absolute executable/config paths and is
separate from target-service activation. Local administrative operations call
fixed OS tools with argument lists and never a command shell.

Service registration uses stable least-privilege identities: the built-in
LocalService account on Windows (no dedicated Windows account is created) and the
non-login `llm-glue` system account on Linux. The installer grants
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

The executable uses invariant globalization because all product contracts are
culture-independent. This keeps the self-contained Linux file runnable on
minimal Debian 13 without an undeclared ICU dependency; see ADR 0008.

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
