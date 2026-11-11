# LLM Glue — Technical Specification and Codex Implementation Plan

**Status:** v1 implementation specification  
**Project/repository:** `llm-glue`  
**Executable:** `llm-glue` (`llm-glue.exe` on Windows)  
**Language/runtime:** C# / .NET 10  
**Primary platforms:** Debian 13 Linux x64; Windows Server 2025 / Windows 11 x64  
**Initial integration targets:** llama.cpp, LiteLLM, Ollama, OpenCode/Paseo  
**License:** MIT. Include `LICENSE` in repository root and copyright attribution appropriate to the repository owner.
**Versioning:** Canonical monotonic revision `rN`; remain `r0` through all pre-release development. See §8.6.

## 1. Mission

Build a lightweight, portable, self-hosted configuration control plane that automatically discovers available LLMs and their effective capabilities, generates correct downstream client/gateway configuration, and continuously synchronizes those configurations without per-model manual registration.

An operator configures providers/endpoints and target applications once. Adding, replacing, or removing a model at a configured provider must propagate to downstream configuration automatically and safely. The application must not require manually curated manifests for every model.

**Guiding contract:** Agents discover; Servers resolve, generate, and publish; Clients download and apply.

## 2. Initial deployment scenarios

### A. Debian inference host (`labhost03`)

- Same `llm-glue` process enables Agent + Server + Client.
- Agent discovers llama.cpp available/loaded models, effective runtime configuration, model files and GGUF metadata; inspects existing LiteLLM routing.
- Server resolves capability information and publishes versioned configuration for LiteLLM and subscribed OpenCode clients.
- Local Client downloads the generated LiteLLM configuration over the same HTTP API as remote clients, validates it, installs it safely and performs an appropriate controlled reload/restart.
- Remote Windows workstations run Client mode only and download managed OpenCode configuration.
- Inference path: OpenCode/Paseo → LiteLLM → llama.cpp.

### B. Standalone Windows workstation

- Same process enables Agent + Server + Client.
- Agent discovers locally running Ollama and available models through its API.
- Server resolves capabilities and generates OpenCode configuration.
- Client fetches the configuration from its local Server HTTP endpoint and synchronizes OpenCode.
- Inference path: OpenCode/Paseo → Ollama directly; LiteLLM is not required.
- Must work without an externally hosted LLM Glue controller, after initial provisioning; offline metadata limitations are surfaced rather than hidden.

### C. Remote Windows consumers

- Client mode only, subscribed to a configured server URL.
- No local inference runtime or agent is required.
- Poll and download changed configuration, protect user-owned settings, validate, apply and report status.

All three capabilities are optional and composable, including `agent,server,client` within a single process. Local and remote clients use the same documented HTTP contract. Prefer in-process calls for internal analysis, but do not create different synchronization semantics for local clients.

## 3. Goals, priorities and exclusions

### P0 requirements

1. One codebase and self-contained executable per platform; no separate Node.js, Python, or .NET installation required at runtime.
2. Provider setup once; **zero per-model registration** or manual updates in normal operation.
3. Automatic detection of added, removed, changed, loaded and unloaded models where provider capabilities permit.
4. Discover model identity, context capacity, effective serving context, modalities, tool support, reasoning modes, sampling controls and runtime constraints with evidence and confidence/provenance.
5. Distinguish model-native capability from *effective deployment* capability.
6. Generate valid OpenCode model/provider entries including accurate variants; generate valid LiteLLM routing config for the llama.cpp path.
7. Publish target-specific, versioned configuration artifacts through HTTP.
8. Clients download only changed artifacts, safely update their locally configured paths and retain a last-known-good copy.
9. Avoid misrepresenting unsupported or unverified capabilities. In particular, no invented reasoning levels, context size or sampling values.
10. Recover from outages, partial writes, invalid configs, stale discovery and reload failures without deleting functional configuration.
11. Do not expose secrets through generated artifacts or diagnostic logs.
12. Support existing OpenCode/Paseo user settings without clobbering unrelated configuration.
13. Be relocatable: executable and its single sidecar configuration may be moved together to any writable directory without reinstalling program dependencies or relying on a canonical application directory.
14. Support self-installation and uninstallation of the Windows service or systemd unit, plus foreground long-running console mode and ordinary CLI commands.
15. Support GitHub Releases-based, opt-in self-update with integrity verification, rollback and safe service restarts.
16. Default to exposing every discovered model, while allowing server-side exclusion by exact model ID, name, provider or scoped matching rule.

### P1 requirements for v1 completion

- Background service support through Windows Service Control Manager and systemd.
- Structured diagnostic logs, health/status endpoints, CLI status and validation commands; configurable log levels and file logging (evaluate NLog as preferred library).
- Schema/versioned contracts, concurrency and idempotency tests, operational documentation.
- Capability probing with bounded timeouts and an opt-in policy for expensive model loads.
- Discovery fixtures covering a binary thinking toggle, multi-level reasoning, no reasoning and a direct Ollama route.
- Root-level `install.sh` and `install.ps1` installers with interactive mode selection and GitHub Release downloads.
- Root-level Linux `Makefile` providing self-documenting `make help`, reproducible build/test/publish targets, and guarded GitHub release publication; equivalent root `scripts/` PowerShell release publisher for Windows.

### Explicitly out of scope for v1

- Remotely installing/downloading/deleting GGUF or Ollama models.
- Remote control of arbitrary inference provider startup flags, GPU placement or upgrades.
- Fleet-wide scheduling, automatic model placement, GPU resource allocation, load balancing and a bespoke inference proxy.
- Supporting vLLM, additional cloud APIs or other coding clients beyond extension points and documented future adapters.
- A web management dashboard, multi-user administrative UI, distributed databases or a generalized job scheduler.
- Automatically downloading untrusted code, executing model-card instructions or changing benchmark model sampling without evidence.
- Mandatory TLS termination inside LLM Glue; the embedded HTTP server is plain HTTP, intended for loopback or private protected networks and optional user-provided reverse proxy.

## 4. Core architecture

Single host with three independent hosted services, selected by configuration:

| Capability | Responsibility | Writes to |
|---|---|---|
| Agent | Local runtime/file inspection; discovery snapshots with provenance | Server inventory API (or in-process equivalent) |
| Server | Aggregation, enrichment, conflict handling, capability resolution, template generation, versioned publication | Its own catalog/cache and HTTP responses |
| Client | Fetch target-specific artifact, validate, stage, atomically apply, perform target-specific activation, report health | Explicitly configured local app files |

Internal abstractions:

- `IModelDiscoveryAdapter`: discover provider inventory and metadata.
- `IRuntimeInspector`: inspect effective runtime and file-level characteristics when APIs are insufficient.
- `IModelMetadataEnricher`: enrich with trusted publisher/family metadata where available.
- `ICapabilityProbe`: bounded behavioral verification and parameter tests.
- `IConfigurationGenerator`: target-specific generated configuration, versioned by schema and generator version.
- `IConfigurationTarget`: installation, backup, validation, activation/rollback for a local application.
- `IProviderManagementAdapter` may be reserved for v2, but no v2 methods need implementation.

Interfaces must be versioned at network boundaries. Keep domain models independent of OpenCode, LiteLLM and llama.cpp-specific config formats. Provider-specific fields belong in typed adapter extensions with explicit validation.

## 5. Discovery and capability resolution

### 5.1 Data model

Each discovered deployment has at minimum:

- Stable provider ID, instance ID, model ID and display name; source endpoint and route identity.
- Canonical model identity if resolvable (repository/revision, model family, GGUF path/hash, quantization), without assuming names are unique.
- `discovered`, `available`, `degraded`, `unavailable`, `invalid` lifecycle/status plus timestamps.
- Native model context limit **and** configured/effective serving context; unknown is a valid value.
- Input/output modalities and tool support, with `supported`, `unsupported`, `unknown` states.
- Reasoning capabilities: no reasoning, boolean on/off, discrete named effort modes, token budget, or unknown; only the modes proven or authoritatively supported may be advertised.
- Sampling profiles by mode, retaining API parameter names, values, provenance and compatibility decisions.
- Runtime details: implementation/version, loaded status, quantization, KV settings, speculative decoding, hardware residency where available; never imply these are necessarily client-selectable.
- Evidence sources, timestamps, verification status and warnings per field.
- The exact route that a client will use, including through LiteLLM when applicable.

Represent tri-state and confidence explicitly; never convert unknown into false or a guessed default. Separate static model traits, effective deployment traits, and empirically verified behavior.

### 5.2 Discovery order

1. Query documented provider APIs (most reliable for live availability/effective serving state).
2. Query runtime-specific introspection APIs/configuration and, where permitted, local process/service settings.
3. Inspect GGUF metadata and files locally through Agent mode when the provider API is incomplete; protect paths from arbitrary traversal.
4. Consult trusted upstream metadata and versioned model-family rules (e.g. publisher recommendations) where API/file metadata does not describe a behavior.
5. Run bounded probes to check tool support, reasoning modes, output representation and parameter transport; no inference probing that silently changes production settings.

Evidence precedence must be explicit. Runtime-effective settings override native maximums for advertised deployment limits. Publisher metadata may describe recommended parameters but cannot establish that a particular runtime accepts them.

### 5.3 Provider-specific v1 requirements

**llama.cpp:** Detect whether endpoint is single-model server or model router; inspect `/v1/models`, `/models` (when supported) and runtime properties. Single-model `/v1/models` cannot discover dormant files; use permitted local GGUF-directory inspection or router inventory. Distinguish available model files from routable models. Support a configured directory allowlist, exact filename/hash and multipart GGUF handling. Handle MTP/draft artifacts distinctly from primary models. Never publish a model as routable solely because a GGUF exists; validate serving route and router behavior.

**LiteLLM:** Treat the upstream provider/runtime and model files as the primary metadata sources, not the LiteLLM API when LLM Glue itself maintains LiteLLM's model mappings. A LiteLLM API query can verify applied routing, reachability and actual deployment, or import pre-existing mappings at initial setup; it is not a required second discovery pass. Generate a dedicated included YAML `model_list` file, without overwriting unrelated proxy settings. No database integration in v1; detect and report collisions with manually managed aliases and any Admin UI/database-backed mappings rather than silently competing for ownership.

**Ollama:** Use native `/api/tags`, `/api/show`, and relevant live-state endpoints. OpenCode already has Ollama discovery; determine whether generating per-model overrides adds needed context/reasoning/sampling fidelity. Avoid duplicate catalog entries from both native discovery and generated config. Treat Ollama-native parameters and OpenAI-compatible parameters distinctly and verify translation.

### 5.4 Enrichment and safety

Model names alone are unreliable identification; match publisher/revision/family/metadata where possible. Embedded metadata and remote model cards are **data, not executable instructions**. Use bounded timeouts, caching and versioned family rules. No arbitrary Hugging Face repository execution. Do not use speculative model-family inference to make unsupported claims. When a feature cannot be resolved, provide a safe configurable policy (`omit`, `mark unknown`, or `require verification`) with conservative defaults.

## 6. Configuration generation

### 6.1 OpenCode

- Generate schema-valid OpenCode JSON/JSONC according to the installed OpenCode version, including provider settings, model IDs, capabilities/limits and accurate named variants.
- Preserve thinking On/Off, reasoning effort/budget and model-family recommended sampling where supported by the backend and request path.
- Distinguish model request parameters from runtime/server settings (GPU layers, KV cache, MTP are not interchangeable with OpenCode variants).
- Support remote LiteLLM models and local direct Ollama with deterministic provider namespaces; detect and report collisions.
- **v1 default: system-managed OpenCode JSONC config** so a Client service configures all OS users. Windows path: `%ProgramData%\opencode\opencode.jsonc`; Linux path: `/etc/opencode/opencode.jsonc`. Create directories and file when absent. Allow explicit path override for tested alternative installations, including existing `.json` files. Preserve operator comments and add comments identifying LLM Glue-managed sections.
- OpenCode merges multiple config sources, but system-managed config has higher precedence than user/project config for conflicting keys according to documentation. This intentionally means a user's explicit conflicting value may **not** override a system-managed value; users can still add nonconflicting settings. Do not claim user override support without a successful live precedence test. A later config cannot be assumed to remove entries from an earlier config merely by omitting them.
- LLM Glue owns only its generated provider/model keys in the system-managed document: preserve all unrelated existing keys, use scoped reconciliation and stale-entry cleanup, back up before changes. Do not overwrite the entire system-managed document merely because it is in a managed location.
- OpenCode targets default `deny_builtin_opencode_provider = true`. The Client adds the current schema-valid `{ action = provider.use, resource = opencode, effect = deny }` policy so OpenCode's default catalog does not obscure explicitly configured LLM Glue models. Track this statement in adjacent ownership state, do not claim an identical operator-owned policy, preserve every unrelated policy, and remove only an owned statement when the option is disabled.
- Keep all unrelated user configuration intact. Never overwrite authentication state, MCP config, agents, permissions or project settings.
- Discover OpenCode configuration path(s) using documented platform/global/project conventions and configured overrides; leave user/project files untouched. For `opencode debug config` or `opencode api GET /api/config`, interpret the returned array as **configuration sources**, not an effective merged configuration; verify the effective result independently with live behavior or a separately validated API. `opencode reload` reporting success does not prove changed values were applied.
- Validate with available OpenCode schema/debug commands and functional model selection. Paseo must show correct named modes. Verify all-users applicability by testing a second Windows/Linux user account.

### 6.2 LiteLLM

- Generate model alias/mapping, backend API base and applicable metadata/config without embedding literal secrets.
- **v1 default:** Operator's primary LiteLLM YAML stays operator-owned and references a one-time LLM Glue generated YAML include (e.g., `include: [llm-glue-models.yaml]`, subject to actual version-specific syntax validation). Client downloads/updates only that included file. Its content is limited to generated `model_list` entries; avoid depending on deep YAML merging or DB state. Existing manually managed routes remain in the main file.
- Make alias generation deterministic and stable across discovery cycles; avoid ambiguous file basename collisions.
- Do not blindly forward unsupported sampling parameters or assume LiteLLM preserves every request field. Verify end-to-end forwarding with representative probes, including thinking kwargs.
- Preserve unrelated operator-owned settings and existing routes; never replace the main config. Document that the one-time include needs to be added or authorized at initial setup; detect missing includes and fail with actionable diagnostics rather than assuming the generated file is loaded.
- Discover actual LiteLLM config location when practical (service command line, environment, process configuration); otherwise request the location once at setup. Create managed configuration for an absent installation only when the intended target is explicitly configured and owned.
- Activation is narrowly scoped to LiteLLM config deployment/reload; no generic remote service-management feature in v1.

### 6.3 Artifact publishing

Expose versioned endpoints (illustrative route names):

- `GET /api/v1/health`
- `GET /api/v1/inventory`
- `GET /api/v1/models`
- `GET /api/v1/artifacts/opencode?profile=<configured-profile>`
- `GET /api/v1/artifacts/litellm?profile=<configured-profile>`
- `GET /api/v1/status`
- authenticated Agent inventory submission endpoint, if Agent and Server are separate.

Use stable representation, content hashes/ETags, conditional requests and an explicit schema version. A published artifact must only reference a model after its route is configured/verified or explicitly state a pending state not advertised to users. Publish roll-forward changes only after prerequisite LiteLLM configuration has become healthy. The server must retain last-known-good artifacts and history sufficient for rollback.

## 7. Client reconciliation

Client is intentionally simple: **download published target config; verify; install local file(s); activate when required**.

Algorithm:

1. Poll configured HTTP server with an ETag/revision, plus jitter/backoff and bounded timeouts.
2. On change, download into a safe staging location; validate transport/authentication, schema and target compatibility.
3. Compute effective change against the client-owned target; avoid writes when unchanged.
4. Back up previous managed artifact; atomically replace via rename on the destination filesystem.
5. Activate using target adapter (OpenCode refresh/restart if proven necessary; controlled LiteLLM config reload/restart where needed).
6. Verify target accepts the artifact and, for gateway routing changes, expected endpoints are healthy. Roll back and surface actionable diagnostics on failure.
7. Report revision, success/error, timestamps and effective target state; never echo credentials.

Protect against concurrent client runs with a process/file lock. Keep backup retention bounded. Never recursively remove user directories. Support explicit user-configured target paths and cross-platform platform defaults. Resolve ownership and permissions at initial setup; no automatic privilege escalation. Offline clients retain working last-known-good files. Removal policy includes grace periods and verification to prevent transient outages from deleting catalog entries.

## 8. Configuration, portability, installation and updates

Exactly one human-managed **TOML** sidecar configuration file per installation; CLI and service modes share settings. TOML is the approved v1 format. It supports comments, explicit types and readable nested sections without YAML's indentation/implicit-type ambiguity. Use a maintained TOML parser, strict validation, useful line/column errors for invalid settings and documented defaults. Do not introduce a second user-facing JSON/YAML/INI configuration format in v1. Generated target files (for example OpenCode JSONC and LiteLLM YAML) retain their respective native formats.

```toml
[roles]
agent = true
server = true
client = true

[[agent.providers]]
type = "llama.cpp"
name = "local-llama"
url = "http://127.0.0.1:8081"
model_directories = ["/mnt/local-nvme-01/AI/models/gguf"]

[server]
listen = "http://127.0.0.1:8765"

[client]
source = "http://127.0.0.1:8765"

[[client.targets]]
type = "litellm"
path = "/opt/litellm/generated-models.yaml"
```

Windows standalone would enable all three roles with an Ollama provider and OpenCode target; remote Windows consumer enables only Client with a remote HTTPS/NetBird server URL. Configuration examples must be tested, not treated as canonical until adapters are implemented.

Ship self-contained, single-file publishes for `linux-x64` and `win-x64` where .NET dependencies allow. Support foreground invocation, Windows Service and systemd installation instructions with least-privilege identities. Include bootstrap validation, `status`, `discover`, `config preview`, `config validate`, `sync --once` and `doctor` commands. Exact CLI grammar finalized during implementation; preserve headless operation.

### 8.1 Path and sidecar requirements

- Default config resolution: explicitly supplied `--config` path; otherwise `llm-glue.conf` then `llm-glue.toml` beside the executable, then the same names in the platform canonical configuration folder. On Linux retain `/etc/llm-glue.conf` and `/etc/llm-glue.toml` as legacy fallbacks. Define precedence and report the selected path in `doctor`.
- No mandatory working-directory assumption. Resolve relative data/log paths against the configuration directory (or explicitly documented configurable base), not an arbitrary current working directory.
- Keep all persistent state (cache, downloaded revision history, logs) in configurable locations. Do not silently write into the executable directory when it is read-only.
- Copying the executable and configuration to another location or machine is a supported workflow. Machine-specific secrets/paths may require reconfiguration, but binaries have no install-location dependency.
- Self-installed services refer to the actual resolved executable and config paths. If users relocate them after service registration, provide `service reinstall`/`service repair` commands rather than pretending service metadata follows automatically.

### 8.2 CLI, foreground and service lifecycle

- `run` foreground mode hosts enabled Agent/Server/Client until Ctrl+C/SIGTERM; prints useful console diagnostics.
- `service install`, `service uninstall`, `service start`, `service stop`, and `service status` act locally with explicit elevation when required. Uninstall must preserve sidecar config and user data by default; no broad recursive deletion.
- Installation and uninstallation must be idempotent and report exact modified service/unit paths. Windows uses SCM; Linux uses systemd; use standard .NET hosting integrations where appropriate.
- Regular administrative CLI commands (status, discovery, configuration diagnostics, sync) must not require a background service if they can operate read-only.

### 8.3 Bootstrap scripts

- Repository root includes `install.sh` and `install.ps1`, designed for `curl ... | bash` and `irm ... | iex` usage, but documentation must explicitly explain the trust implications and provide a safer download/inspect/run alternative.
- Choose appropriate OS/architecture, fetch the latest stable GitHub Release with pinned repository identity, verify release assets via published SHA-256 checksums and preferably cryptographic release provenance/signature, and reject failed verification.
- Offer a sensible writable install directory; allow override. Ask which roles to enable and whether to register a service. Prompt for required initial server URL/token and target/provider paths only when necessary.
- Elevate via `sudo`/UAC only for the operations that require elevation; avoid executing arbitrary network-fetched content as root/admin before validation. Fail with clear instructions when noninteractive elevation is impossible.
- Never silently overwrite existing configuration, run a destructive migration or erase stored credentials. Re-runs are idempotent/upgrades with backup.

### 8.4 Self-update

- Explicit `update check` and `update apply` commands; optional scheduled checking, but no automatic binary replacement by default without user opt-in.
- Use GitHub Releases metadata for stable version discovery; download only official matching assets; verify checksum and release provenance where available. Reject downgrades unless explicitly requested.
- Stage the replacement outside the active binary, retain rollback copy, and coordinate restart/stop using OS-appropriate strategy (not an in-place overwrite of a running Windows executable).
- Service-mode update must preserve config, logs, state and service registration, then restart and health-check; revert to last-known-good binary when installation/health checks fail.
- Offline operation remains functional. Failed update checks must never interrupt inference configuration synchronization.

### 8.5 Logging

- Console and rolling file targets with configurable levels (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`) and retention/size limits. Evaluate NLog as preferred implementation, but keep logging behind standard `Microsoft.Extensions.Logging` abstractions.
- No secrets, API tokens or raw inference prompts in logs by default. Include correlation IDs, model/provider IDs and sync revision without sensitive request bodies.

### 8.6 Canonical versioning and .NET mapping

- **Canonical product version** is a single monotonically increasing integer, displayed as `rN` (`r0`, `r1`, `r2`, …). Never reset or reuse a released revision, and do not adopt SemVer major/minor/patch as the product's public identity.
- All development, test builds, CI artifacts and release candidates remain **`r0` until the first official public release**. Differentiate them by Git commit SHA, build timestamp and optional build metadata, not by incrementing the public revision.
- First official release is **`r1`**. Each subsequent official release increments the revision by exactly one, regardless of the size of the change. No skipped/reused revision without explicit documented exceptional handling.
- **AssemblyVersion** tracks only the major API/binding-compatibility generation: `Major.0.0.0`. All three remaining components must always be zero. It is `0.0.0.0` before first official release, then `1.0.0.0` across all `r1`, `r2`, `r3`, etc. releases, unless an explicitly approved major breaking API change increments Major.
- **FileVersion** tracks the canonical revision: `Major.N.0.0`. Before first official release, Major is `0`; upon first official release, Major becomes `1`. `N` equals the canonical `rN` integer and never resets, even if Major changes. The last two components remain zero.
- Examples: `r0` → Assembly `0.0.0.0`, File `0.0.0.0`; `r1` → Assembly `1.0.0.0`, File `1.1.0.0`; `r2` → Assembly `1.0.0.0`, File `1.2.0.0`; `r57` → Assembly `1.0.0.0`, File `1.57.0.0`. An approved breaking major at `r58` could yield Assembly `2.0.0.0` and File `2.58.0.0`.
- Set .NET `AssemblyVersion` and `FileVersion` separately; never derive assembly version from the full file version. `InformationalVersion` should include `rN` and optionally a commit identifier. Where packaging tooling demands SemVer, use mechanically derived `Major.N.0` and retain `rN` as the user-facing identity; document any constraint.
- The CLI (`llm-glue --version`), logs, diagnostics, release notes, update metadata and GitHub release tags must show the canonical `rN` and commit/build identifier where useful. The update engine compares canonical integer revisions and never uses lexical string ordering (`r10` must compare newer than `r9`).
- GitHub Releases should publish canonical `rN` tags; pre-release/nightly builds before `r1` must not be mistaken for official updates. Self-update must respect the release channel and reject unapproved downgrade or major compatibility transitions.
- Define any future API schema compatibility independently of the product's release revision; do not assume two consecutive `rN` revisions necessarily have incompatible APIs.

### 8.7 Build and guarded release tooling

- Repository root must contain a **GNU Make-compatible `Makefile`** intended for Linux. Running `make` without arguments must display help or invoke a documented safe default, and `make help` must list available targets with short descriptions and relevant options/overrides.
- Provide at minimum `help`, `restore`, `build`, `test`, `clean`, `publish-linux`, `publish-windows`, `package`, `release-check`, and `release` targets (names can differ only with documented rationale). Support configuration such as `CONFIG=Release`, `RID=linux-x64`, output directory and optional version preview. Do not let `clean` remove unowned directories.
- Repository root must contain a PowerShell script in `scripts/` (e.g. `scripts/Publish-Release.ps1`) that performs the **same guarded GitHub release workflow on Windows** as `make release` on Linux. Factor shared release rules into a documented contract; parity is required even if platform implementation differs.
- Release publication uses GitHub CLI/API with the user's existing authenticated credentials; never embed or solicit a long-lived personal access token in source. Explicit `release` invocation is required—ordinary `make build` and `make publish-*` must never publish to GitHub.
- **Mandatory preflight before creating tags or publishing:** require a Git repository on the expected branch; no staged, unstaged, untracked or unresolved changes (except explicitly documented ignored build output); an upstream tracking branch; successful fetch of remote refs; local HEAD equal to the designated remote branch HEAD; zero ahead/behind divergence; all release commits already pushed; and no conflicting existing `rN` tag or GitHub release. Reject detached HEAD and ambiguous remotes by default.
- Recheck the remote HEAD immediately before the irreversible publication step to reduce time-of-check/time-of-use races. Never auto-commit, auto-push, force-push, rewrite tags, bypass branch protection, or silently resolve divergence.
- Validate the requested canonical `rN` against the last official published revision: `r0` cannot be published as an official release; ordinary new official releases increment by exactly one; verify `AssemblyVersion`, `FileVersion` and user-facing version mapping match §8.6.
- Run release builds and all required tests, verify expected `linux-x64` and `win-x64` outputs, create checksums and package metadata, and produce a manifest linking artifacts to the exact commit SHA. Abort without publishing when a gate fails.
- Publish the immutable `rN` tag and GitHub Release with matching binaries, checksum manifest and release notes only after gates pass. If any partial publication fails, report exact remote state and recovery steps; do not automatically delete an existing release/tag without explicit approval.
- Support `-WhatIf`/`-DryRun` in PowerShell and `DRY_RUN=1` (or equivalent) in Make, with preflight and intended actions but no remote mutations. Provide a clear pre-publication confirmation, with an explicit noninteractive flag for CI.
- Document required tooling (`dotnet`, `make`, `git`, `gh`, PowerShell on Windows), authentication, branch selection and artifact naming. Tests should simulate clean, dirty, ahead, behind, detached, tag collision, partial failure, and unavailable remote scenarios without actually publishing.

## 9. Networking, security and resilience

- Embedded Kestrel web server is HTTP-only; no native HTTPS feature for v1. Default to loopback binding. Remote HTTP access is explicitly enabled only on a trusted encrypted private network (e.g. NetBird), or placed behind a user-provided HTTPS reverse proxy. Refuse insecure remote exposure without an explicit operator acknowledgment and clearly warn that API tokens are not protected by bare HTTP transport.
- Separate read-only discovery credentials from artifact download and Agent inventory publication permissions. Prepare an authorization boundary for v2 management, but do not expose remote execution endpoints in v1.
- **Approved v1 authentication policy:** API bearer-token authentication is **mandatory for non-loopback connections**, including remote Agent submission and remote Client artifact download. Authentication is optional only for loopback-only deployments. Determine endpoint permissions by role: remote read operations must be authenticated; inventory submissions need separate write authorization. Reject missing/invalid tokens with a non-leaking response, support token rotation, and never log tokens. Prevent proxy/header misconfiguration from accidentally treating non-loopback requests as loopback. A reverse proxy does not remove the server's remote authentication requirement by default.
- Keep API token material in one sidecar file only if file permissions can protect it; prefer references to environment variables or OS credential stores when appropriate. Password encryption at rest is not intrinsically useful if the decryption key is shipped beside the config. Document the threat model before selecting optional encryption.
- Use file permissions on configs and generated artifacts; do not embed provider credentials in downloaded client configurations.
- Agent-side exclusion patterns operate before export or remote submission and support exact model names plus documented `*`/`?` matching. Server-side exclusion rules operate by exact model ID, display name and provider-qualified ID, with an explicit wildcard model-pattern field. Default is no exclusions. Agent-hidden models do not leave the host; server-hidden models remain visible in operator diagnostics with their exclusion reason, but not in published client catalogs. Never delete underlying model files as a result of an exclusion.
- Do not expose raw model files, arbitrary filesystem paths, environment variables or provider API keys through HTTP. Generated client config references secrets through supported environment substitution.
- Validate requests, limit payload sizes, prevent path traversal, sanitize logs and redact secrets.
- No arbitrary process execution from remote requests. Local target activation uses an allowlisted adapter and explicit configured service identity.
- Polling is idempotent and interruptible. Use cancellation tokens, timeouts, retry with exponential backoff/jitter, circuit-breaker behavior and stale inventory TTL/grace handling.
- Concurrent discovery, generation and file installation must not race. Artifacts are immutable by revision and clients apply monotonically unless explicit rollback is requested.
- Model metadata discovery must not automatically load very large models unless configured. Expensive probes are opt-in or queued with resource limits.
- Provide clear diagnostics for unsupported backend versions, unavailable APIs, incomplete metadata, partial downloads and ambiguous model identities.

## 10. Quality and verification strategy

Use fixtures generated from real initial experiments. The repository must include sanitized examples and expected outputs for at least:

1. Qwen3.6 35B-A3B MTP Q8_0 through llama.cpp + LiteLLM with Thinking Off/On. Reference observed parameters: Off `{enable_thinking:false,temperature:0.7,top_p:0.8,top_k:20,min_p:0,presence_penalty:1.5,repeat_penalty:1}`; On `{enable_thinking:true,temperature:0.6,top_p:0.95,top_k:20,min_p:0,presence_penalty:0,repeat_penalty:1}`. These are *reference observations*, not universal family defaults.
2. A Gemma 4 model on the same route to expose alternative architecture/templates/quantization or thinking behavior.
3. GPT-OSS (or another verified effort-level model) to exercise multiple reasoning levels.
4. Direct Ollama on Windows with at least one reasoning model and one non-reasoning model.
5. Missing metadata, provider outage, stale catalog, alias collision, invalid generated config, failed activation, interrupted sync, changed context allocation and model removal.

Tests must include unit tests for parsing/resolution/generation; schema/golden-file regression tests; HTTP integration tests; atomic-write/rollback tests; service shutdown/restart tests; an end-to-end test of LiteLLM request forwarding; and OpenCode/Paseo model/variant visibility checks. Where live runtimes are unavailable in CI, use recorded fixtures and test containers/mocks; live qualification is a separate gate.

Never claim context/GPU/MTP settings were verified merely because a startup flag or config says so. Distinguish configured, reported and observed state.

## 11. Milestones and acceptance gates

### M0 — Evidence collection and interface validation

**Deliverables:** Runtime/API capability matrix with version numbers; sanitized discovery fixtures; manual configuration transcripts for Qwen reference, Gemma, GPT-OSS and Ollama; OpenCode config auto-location/layering and **effective precedence** tests across at least two OS users; LiteLLM include syntax/merging/ownership/reload test on the installed version; unresolved metadata gaps documented with proposed fallbacks; validated TOML configuration examples and parser choice; initial token/network threat model implementing the approved mandatory-remote/optional-loopback token policy. Windows Server 2025 exploratory evidence (2026-11-10): creating `%ProgramData%\opencode\opencode.json` caused `opencode debug config` to list that document alongside `C:\Users\Administrator\.config\opencode\opencode.jsonc`; adding `"username": "LLM Glue System Test"` appeared in the **source listing**; `opencode reload` responded `Configuration reloaded`. `opencode api GET /api/config` returned four source entries (`type`, `path`, optional `info`), **not** the effective merged config. `opencode api GET /config` did not yield parseable JSON in this test. This proves discovery and parsing only; effective precedence, all-users behavior and reload effects remain unverified.

**Gate:** Every required v1 capability has a documented discovery source, verification mechanism or explicit unknown-state behavior. No code implementation before resolving destructive sync/merge semantics.

### M1 — Core domain and discovery engine

Implement role composition, configuration parsing, model/deployment/capability types, provenance, GGUF reader or safe library integration, llama.cpp and Ollama discovery adapters, deterministic inventory snapshots.

**Gate:** From live hosts or recorded fixtures, discovers models without manual per-model entries and correctly distinguishes model-native vs effective serving context and reasoner mode categories.

### M2 — Server and configuration generation

Implement catalog reconciliation, stale/grace states, conflict handling, OpenCode generator, LiteLLM generator, validation, ETags/versioned API and minimal diagnostics.

**Gate:** Golden-file fixtures produce expected OpenCode/LiteLLM configs, no credential leakage; request-body variants are represented correctly; no inappropriate model entries are published.

### M3 — Client updater and application targets

Implement polling, staging, atomic replacement, last-known-good backup/rollback, target validation/activation, OpenCode and LiteLLM targets, idempotent sync and service hosting.

**Gate:** Cross-platform tests prove no-op when unchanged, safe recovery after interruption/invalid file/reload failure and preservation of unrelated user configuration.

### M4 — Live end-to-end qualification

Deploy all three roles on Debian labhost03 and remote Client on Windows; deploy all three roles for standalone Windows/Ollama; run real discovery→publish→download→apply→functional inference paths. Add/remove/change a model and verify updates without manual registration. Compare selected variants and transmitted parameters against expected profiles.

**Gate:** Both supported paths work unattended after one-time setup; restart/outage recovery works; no false capabilities or silent config loss; OpenCode/Paseo displays valid catalog and reasoning options.

### M5 — Release hardening

Produce MIT-licensed `rN`-versioned self-contained binaries (with distinct stable-major .NET `AssemblyVersion` and revision-tracking `FileVersion`), executable bootstrap installers (`install.sh`, `install.ps1`), Linux Makefile with `make help` and guarded GitHub release target, equivalent Windows `scripts/Publish-Release.ps1`, actual Windows service/systemd self-install and uninstall, portable sidecar config behavior, console and rolling-file logs, GitHub Releases self-update/rollback, onboarding guides, troubleshooting/doctor commands, security defaults, architecture ADRs, developer docs, reproducible CI/release workflow and a v2 roadmap.

**Gate:** A new user can bootstrap, service-install, move/reconfigure, update, rollback and uninstall safely on both platforms; configure both supported paths without editing per-model configuration. Linux `make help`/build/test and Windows release-script dry-runs work; clean-and-pushed Git preflights correctly permit publication, and dirty/divergent/unpushed states fail closed without creating remote releases. CI and live acceptance suites pass; documented limitations are accurate.

## 12. Codex implementation instructions

Treat this document as the authoritative product intent, but inspect current official provider/OpenCode/LiteLLM APIs before writing adapters. Their schemas and behavior may change between versions. At project inception, record exact versions and compatibility assumptions in `docs/compatibility.md`.

Proceed milestone by milestone. At each gate:

1. Write/update an implementation plan and acceptance checklist.
2. Make the smallest cohesive implementation that satisfies the milestone.
3. Execute build, tests, static checks and available integration tests.
4. Update README, architecture decisions, fixtures, compatibility matrix and operational docs.
5. Report verified results, remaining unknowns and evidence. Do not mark gates passed based on self-authored smoke tests alone.
6. Commit cleanly, with no secrets or user-specific credentials in source control.

Use `AGENTS.md` for durable engineering instructions and project conventions. Do not introduce infrastructure dependencies beyond those needed for the actual supported paths. Prefer standard ASP.NET Core hosting, `HttpClientFactory`, background services, `System.Text.Json`, explicit schema validation and narrow provider adapters. Avoid a distributed database, custom RPC framework, feature-heavy web UI or generic orchestration engine for v1.

When a design decision affects configuration ownership, model identity, fallback rules, authentication, or deletion semantics, document an ADR and seek review before implementation. Do not silently weaken the zero-touch requirement by introducing per-model configuration entries.

## 13. v2 roadmap (non-binding)

Authorized remote operations: download/install models from Hugging Face, assign models to multiple inference hosts, configure runtime settings, activate/deactivate deployments, manage provider services, return progress and handle partial failure. This requires separate authentication/authorization, idempotent operation IDs, capacity planning, cancellation, rollback and auditable changes. **Do not implement v2 management APIs in v1.** Preserve clean extension boundaries only.

## 14. Definition of done for v1

After configuring the llama.cpp/LiteLLM provider and subscribing an OpenCode workstation once, adding a genuinely routable model on Debian causes it to be discovered, enriched, published, configured in LiteLLM and made selectable through OpenCode/Paseo without manual per-model edits. Correct reasoning modes and sampling values are exposed only when supported/evidenced. Removing or changing a model propagates safely after appropriate checks. An independent Windows/Ollama installation achieves equivalent zero-touch catalog and config synchronization without LiteLLM or an external central server. Both survive transient failures and protect unrelated user settings.
