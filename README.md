# LLM Glue

LLM Glue discovers models served by llama.cpp and Ollama, normalizes evidence
about their capabilities, publishes versioned configuration artifacts, and
safely synchronizes those artifacts into OpenCode or LiteLLM.

The project is under active `r0` development. No official `r1` release has been
authorized. See [progress](docs/progress.md) for implemented and qualified
functionality.

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

Installation, service, update, and operator quick-start instructions will be
completed as the corresponding milestones qualify.

Validated starting configurations are available in [`examples`](examples). The
all-in-one example discovers local Ollama and targets the system OpenCode file;
the Linux example discovers llama.cpp and owns only a LiteLLM include; the remote
example is a Client-only OpenCode consumer.

## Security

The HTTP server defaults to loopback. Bearer authentication is mandatory on
non-loopback listeners, and bare remote HTTP requires an explicit insecure
transport acknowledgement because bearer tokens do not provide encryption.
Generated artifacts never embed provider credentials.

Copyright and licensing are provided under the repository's MIT license.
