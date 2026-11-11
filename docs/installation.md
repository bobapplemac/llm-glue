# Installation and quick start

Official release assets are self-contained single files for `win-x64` and
`linux-x64`. No official release exists while the project remains `r0`.

## Bootstrap trust

`irm .../install.ps1 | iex` and `curl .../install.sh | bash` execute network
content immediately. The safer procedure is to download the script from the
pinned `bobapplemac/llm-glue` repository, inspect it, then run the local copy. The
script retrieves only the latest stable `rN` release, verifies the archive against
the release `SHA256SUMS`, stages extraction, preserves existing configuration,
and keeps the previous executable as `.rollback`.

```text
# Windows, after inspection
./install.ps1 -InstallDirectory C:\Tools\LLMGlue

# Linux, after inspection
LLM_GLUE_INSTALL_DIR=$HOME/.local/lib/llm-glue ./install.sh
```

Interactive installation asks for `remote-client` or `standalone-ollama`, the
relevant server/provider URL, the OpenCode system path, and optional service
registration. Headless installs set `-NonInteractive` plus PowerShell parameters,
or `LLM_GLUE_NONINTERACTIVE=1`, `LLM_GLUE_MODE`, `LLM_GLUE_SERVER_URL`/
`LLM_GLUE_OLLAMA_URL`, `LLM_GLUE_OPENCODE_PATH`, and
`LLM_GLUE_INSTALL_SERVICE` on Linux. Existing configuration is never replaced.
The generated configuration is validated before optional service registration.
Remote-client tokens remain environment references and must be set separately.

Then run:

```text
llm-glue config validate --config /path/to/llm-glue.conf
llm-glue doctor --config /path/to/llm-glue.conf
llm-glue run --config /path/to/llm-glue.conf
```

An Agent-only host uses `agent.submit_to` and
`agent.write_token_environment`; see `examples/remote-agent.conf`. The destination
is the Server root, not the inventory endpoint. Exact server publication filters
use one or more rules such as:

```toml
[[server.exclusions]]
provider_id = "ollama"
instance_id = "workstation"
model_id = "private-model"
```

All supplied fields in a rule must match. Exclusions hide catalog publication
only: diagnostics retain the deployment and identify the matching rule.

`server.state_directory` controls durable catalog/artifact storage and resolves
relative to the sidecar directory. The service identity must be able to create
and replace files there. `server.artifact_retention` defaults to five validated
states. Linux service installations conventionally use `/var/lib/llm-glue`, while
a portable foreground installation may use a relative `state` directory.

Service registration requires elevation and preserves the sidecar and data on
uninstall. Copying the executable and sidecar is supported; use `service repair`
after relocation because OS service metadata does not follow moved files.
