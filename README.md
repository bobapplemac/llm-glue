# LLM Glue

LLM Glue discovers models served by llama.cpp and Ollama, normalizes evidence
about their capabilities, publishes versioned configuration artifacts, and
safely synchronizes those artifacts into OpenCode or LiteLLM.

The current stable release is
[LLM Glue r6](https://github.com/bobapplemac/llm-glue/releases/tag/r6).
Ordinary development builds identify as `r0`; the guarded release publisher
assigns the authorized `rN` product and file versions. See the authoritative
[product specification](docs/llm-glue-spec.md), [progress](docs/progress.md),
and [completion audit](docs/completion-audit.md).

## Quick install

The bootstrap configures an interactive installation and downloads the latest
stable checksum-verified binary. Review the
[PowerShell script](https://raw.githubusercontent.com/bobapplemac/llm-glue/main/install.ps1)
or [Bash script](https://raw.githubusercontent.com/bobapplemac/llm-glue/main/install.sh)
and the [installation guide](docs/installation.md) first.

Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/bobapplemac/llm-glue/main/install.ps1 | iex
```

New Windows service installations default to `%ProgramData%\LLMGlue`; installs
without service registration remain under `%LOCALAPPDATA%\LLMGlue`. Use an
explicit `-InstallDirectory` with the inspected local script to override either.

Linux Bash:

```bash
curl -fsSL https://raw.githubusercontent.com/bobapplemac/llm-glue/main/install.sh | bash
```

## Architecture

One executable composes three independently enabled roles:

- **Agent** discovers local provider/runtime state.
- **Server** reconciles inventory and publishes deterministic artifacts.
- **Client** downloads, validates, atomically installs, activates, and rolls
  back managed target configuration.

Provider-specific data is normalized before generation. Unknown capabilities
remain unknown, and effective serving limits are kept separate from model-native
limits. See [architecture](docs/architecture.md) and
[compatibility](docs/compatibility.md).

llama.cpp router discovery refreshes configured presets and publishes dormant
routes without loading them. Loaded state controls optional inference probing,
not catalog membership.

## Development

Prerequisites: .NET SDK 10 and Git. From the repository root:

```text
dotnet restore
dotnet build
dotnet test
```

Operator documentation: [installation](docs/installation.md),
[troubleshooting](docs/troubleshooting.md), [security](docs/security.md), and
[release process](docs/releasing.md). Complete generated references cover every
[CLI command and parameter](docs/cli-reference.md) and every accepted
[TOML setting](docs/configuration-reference.md).

The reference pages are generated from typed catalogs in the source. A normal
`dotnet build` refreshes them; `make docs` regenerates explicitly, and
`make docs-check` fails when committed output is stale.

Validated starting configurations are available in [`examples`](examples). The
all-in-one example discovers local Ollama and targets the system OpenCode file;
the Linux example discovers llama.cpp and owns only a LiteLLM include; the remote
example is a Client-only OpenCode consumer. `remote-agent.conf` demonstrates an
Agent-only host submitting inventory with a separate write credential.

The root bootstrap scripts install a checksum-verified stable release and can
configure a remote OpenCode client, standalone Ollama/OpenCode host, or central
llama.cpp/LiteLLM host. Interactive setup begins with any non-empty combination
of Agent, Server, and Client roles; it does not present provider implementations
as deployment modes. Later Agent-provider and Client-target prompts support
multiple selections and conservatively suggest locally
detected Ollama, llama.cpp, OpenCode, and LiteLLM integrations before allowing
an override. They validate the candidate sidecar before installation, preserve
an existing sidecar, and retain the previous executable for rollback.
Central mode requires a LiteLLM URL reachable by OpenCode clients so generated
catalogs follow the required OpenCode → LiteLLM → llama.cpp path.
Remote OpenCode clients normally need only that server URL and the LLM Glue read
token. The central server embeds its configured LiteLLM key in the authenticated
OpenCode artifact by default; operators can opt out and provision
`LLM_GLUE_INFERENCE_API_KEY` to every OpenCode process instead.
See [installation](docs/installation.md) before using the network-pipe form.

New Servers default to native Kestrel HTTPS on port 8765 and can create a
398-day self-signed PEM certificate/key automatically. File-based PFX/P12 and
PEM/CRT certificates are supported; certificate-store configuration is not.
Remote Agents and Clients can use normal platform trust, pin the Server public
key's SHA-256 identifier, or explicitly accept any invalid certificate.
Certificate validation, same-key self-signed renewal, guarded regeneration, and
explicit hot reload are available through `llm-glue tls`; certificate files are
not watched.

OpenCode destination scope is explicit: configure the system file for a global
catalog, or repeat `[[client.targets]]` with one path per selected user. Existing
JSON/JSONC is merged using an adjacent, source-scoped ownership document; new
installations default to `opencode.jsonc`, preserve operator comments, and mark
managed groups with comments. OpenCode targets hide the built-in `opencode`
provider catalog by default through an owned policy; set
`deny_builtin_opencode_provider = false` on a target to opt out. LLM Glue never
scans user profiles or claims unrelated settings.

Useful commands include `run`, `status`, `discover`, `doctor`, `config validate`,
`config preview opencode|litellm`, `sync --once`, `service
install|repair|start|stop|status|uninstall`, and `update check|apply|rollback`.
Run `llm-glue --help` (also `help`, `/?`, or `-h`) for built-in usage. All
commands accept `--config`; otherwise LLM Glue checks `llm-glue.conf` then
`llm-glue.toml` beside the executable, followed by the platform configuration
folder (`/etc/llm-glue` or `%ProgramData%\LLMGlue`). Legacy Linux root-level
paths remain supported.

Model export can be filtered at two boundaries. Agent `exclude_models` patterns
prevent matching inventory from leaving that host. Server `model_pattern` rules
retain diagnostic visibility while omitting matches from generated catalogs.

OpenCode models appear under the concise `llm-glue` provider namespace by
default. Servers can change `opencode_namespace` or use ordered
`[[server.opencode_namespace_overrides]]` rules to place selected provider
instances in separate namespaces. LiteLLM route aliases use the normalized
Server hostname by default and can be pinned with `server.litellm_namespace`;
that internal route name does not change OpenCode's visible namespace.

## Security

The HTTPS server defaults to loopback. Bearer authentication is mandatory on
non-loopback listeners, and bare remote HTTP requires an explicit insecure
transport acknowledgement because bearer tokens do not provide encryption.
LiteLLM artifacts never embed provider credentials. Authenticated OpenCode
artifacts intentionally carry the LiteLLM client key by default for single-token
client provisioning, so protect the LLM Glue read token, transport, server state,
and installed OpenCode JSONC accordingly.

Copyright and licensing are provided under the repository's MIT license.
