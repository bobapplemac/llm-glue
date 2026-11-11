# LLM Glue

LLM Glue discovers models served by llama.cpp and Ollama, normalizes evidence
about their capabilities, publishes versioned configuration artifacts, and
safely synchronizes those artifacts into OpenCode or LiteLLM.

The project is under active `r0` development. No official `r1` release has been
authorized. See [progress](docs/progress.md) for implemented and qualified
functionality and the [completion audit](docs/completion-audit.md) for the exact
remaining live gates.

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

## Development

Prerequisites: .NET SDK 10 and Git. From the repository root:

```text
dotnet restore
dotnet build
dotnet test
```

Operator documentation: [installation](docs/installation.md),
[troubleshooting](docs/troubleshooting.md), [security](docs/security.md), and
[release process](docs/releasing.md).

Validated starting configurations are available in [`examples`](examples). The
all-in-one example discovers local Ollama and targets the system OpenCode file;
the Linux example discovers llama.cpp and owns only a LiteLLM include; the remote
example is a Client-only OpenCode consumer. `remote-agent.conf` demonstrates an
Agent-only host submitting inventory with a separate write credential.

The root bootstrap scripts install a checksum-verified stable release and can
configure a remote OpenCode client, standalone Ollama/OpenCode host, or central
llama.cpp/LiteLLM host. They validate the candidate sidecar before installation,
preserve an existing sidecar, and retain the previous executable for rollback.
Central mode requires a LiteLLM URL reachable by OpenCode clients so generated
catalogs follow the required OpenCode → LiteLLM → llama.cpp path.
See [installation](docs/installation.md) before using the network-pipe form.

OpenCode destination scope is explicit: configure the system file for a global
catalog, or repeat `[[client.targets]]` with one path per selected user. Existing
JSON/JSONC is merged using an adjacent, source-scoped ownership document; LLM
Glue never scans user profiles or claims unrelated settings.

Useful commands include `run`, `status`, `discover`, `doctor`, `config validate`,
`config preview opencode|litellm`, `sync --once`, `service
install|repair|start|stop|status|uninstall`, and `update check|apply|rollback`.
All commands accept `--config`; otherwise the executable-side `llm-glue.conf` is
used, with `/etc/llm-glue.conf` as the Linux fallback.

## Security

The HTTP server defaults to loopback. Bearer authentication is mandatory on
non-loopback listeners, and bare remote HTTP requires an explicit insecure
transport acknowledgement because bearer tokens do not provide encryption.
Generated artifacts never embed provider credentials.

Copyright and licensing are provided under the repository's MIT license.
