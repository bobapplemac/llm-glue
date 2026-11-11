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

Interactive installation offers `remote-client`, `standalone-ollama`, and
`central-llama`. It asks only for the URLs, secrets, provider/target paths, and
optional service registration needed by that mode. The central mode configures
llama.cpp discovery, authenticated Server publication, and the owned LiteLLM
include; it never edits the operator's primary LiteLLM YAML.

Headless PowerShell installs use `-NonInteractive` with the corresponding named
parameters. Headless Linux installs set `LLM_GLUE_NONINTERACTIVE=1`,
`LLM_GLUE_MODE`, and applicable variables: `LLM_GLUE_SERVER_URL`,
`LLM_GLUE_SERVER_TOKEN`, `LLM_GLUE_OLLAMA_URL`, `LLM_GLUE_LLAMA_URL`,
`LLM_GLUE_LISTEN_URL`, `LLM_GLUE_READ_TOKEN`, `LLM_GLUE_WRITE_TOKEN`,
`LLM_GLUE_MODEL_DIRECTORY`, `LLM_GLUE_OPENCODE_PATH`, the
`LLM_GLUE_LITELLM_*` paths/service/health settings, and
`LLM_GLUE_INSTALL_SERVICE`. `LLM_GLUE_INSTALL_DIR` and
`LLM_GLUE_CONFIG_PATH` override destinations. Linux service installs otherwise
use `/opt/llm-glue` and `/etc/llm-glue.conf`; foreground installs remain
user-writable.

The checksum-verified staged executable and candidate configuration are validated
before copying anything into the final location. Existing configuration is
validated and never replaced; upgrades retain the prior executable as
`.rollback`. Service-mode remote clients require a literal token because the
installed service does not inherit the bootstrap environment. Interactive secret
prompts do not echo. Generated sidecars use mode `0600` on Linux and a restricted
owner/SYSTEM/Administrators ACL on Windows; service registration then grants its
fixed identity read access. Never configure both literal and environment forms
for one credential.

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

Provider functional inference is disabled by default. For already-loaded models:

```toml
[[agent.providers]]
type = "llama.cpp"
name = "router"
url = "http://127.0.0.1:8081"
probe_mode = "loaded-only"
probe_timeout_seconds = 30
```

`allow-load` may cause the runtime to load a dormant model and must be selected
explicitly. These probes verify only the text route; reasoning and sampling remain
unknown until separately qualified.

Service registration requires elevation and preserves the sidecar and data on
uninstall. Windows installs the service as `NT AUTHORITY\\LocalService`, granting
read/execute access to the single-file executable, read access to the sidecar,
and modify access only to configured state, log, and target directories. Linux
creates or reuses the fixed, non-login `llm-glue` system account, removes all
other-user access from the sidecar, and grants that account access only to the
configured writable directories. A missing Linux writable directory is created
with group access; an existing directory that the account cannot write causes
installation to fail instead of changing operator-owned permissions. Uninstall
preserves the Linux account and all ACL/mode changes so retained configuration
and data ownership remain stable. Copying the executable and sidecar is supported;
use `service repair` after relocation because OS service metadata does not follow
moved files.

Updates are opt-in. Use `update check`, then `update apply` for a foreground
installation. For a registered LLM Glue service, run elevated:

```text
llm-glue update apply --service --config /path/to/llm-glue.conf
llm-glue update rollback --service --config /path/to/llm-glue.conf
```

Service mode preserves the sidecar, state, logs, and registration; restarts the
fixed `LLMGlue`/`llm-glue.service` identity; and checks `/api/v1/health` when the
Server role is enabled or SCM/systemd running state otherwise. Failed or
interrupted activation restores and reverifies the previous executable. Windows
uses a hidden post-exit helper because a running executable cannot be replaced;
its result log is printed before the CLI exits.
