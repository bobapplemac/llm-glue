# Installation and quick start

Official release assets are self-contained single files for `win-x64` and
`linux-x64`. The Linux executable uses invariant globalization and does not
require ICU; it was executed and service-qualified on Debian 13.5. Normal OS
facilities such as glibc and systemd remain platform dependencies. Official `r1`
assets are available from GitHub Releases; ordinary development builds continue
to identify as `r0` outside the guarded release path.

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
include; it never edits the operator's primary LiteLLM YAML. Central mode also
requires the externally reachable LiteLLM base URL (`-LiteLlmUrl` or
`LLM_GLUE_LITELLM_URL`). LLM Glue publishes that URL and its generated stable
aliases to OpenCode; it must not be the private llama.cpp discovery URL.
Each OpenCode/Paseo runtime must receive a valid LiteLLM client key as
`LLM_GLUE_INFERENCE_API_KEY` in its process/service environment. This is a
one-time client credential setup; generated configuration contains only the
OpenCode `{env:...}` reference and never the key. LiteLLM's master key or a
properly scoped virtual key may be used according to the operator's policy.

Headless PowerShell installs use `-NonInteractive` with the corresponding named
parameters. Headless Linux installs set `LLM_GLUE_NONINTERACTIVE=1`,
`LLM_GLUE_MODE`, and applicable variables: `LLM_GLUE_SERVER_URL`,
`LLM_GLUE_SERVER_TOKEN`, `LLM_GLUE_OLLAMA_URL`, `LLM_GLUE_LLAMA_URL`,
`LLM_GLUE_LISTEN_URL`, `LLM_GLUE_READ_TOKEN`, `LLM_GLUE_WRITE_TOKEN`,
`LLM_GLUE_MODEL_DIRECTORY`, `LLM_GLUE_OPENCODE_PATH`, the
`LLM_GLUE_LITELLM_*` URL/paths/service/health settings, and
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
explicitly. A loaded llama.cpp route also receives bounded `reasoning_effort=none`
and `medium` probes when reasoning is unknown; Off/On profiles appear only after
distinct response behavior is verified. Tools and named effort semantics remain
unknown.

When a container reports model paths under a different prefix than the host,
configure the mapping explicitly rather than relying on filenames:

```toml
model_directories = ["/srv/models/gguf"]
model_path_mappings = ["/models=/srv/models/gguf"]
```

The mapped canonical path must still remain inside `model_directories`.

Service registration requires elevation and preserves the sidecar and data on
uninstall. Windows always uses the built-in `NT AUTHORITY\\LocalService` account;
it does not create a dedicated Windows user. Installation grants read/execute
access to the single-file executable, read access to the sidecar,
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

A Linux LiteLLM target may need to restart a root-owned systemd or Docker Compose
unit. Do not run LLM Glue as root for that purpose. Install `polkitd`, keep the
activation unit root-owned, and install a rule based on
`examples/49-llm-glue-litellm.rules` after changing its exact unit name to match
`service_name`. The example grants the `llm-glue` account only the
`org.freedesktop.systemd1.manage-units` `restart` verb for that one unit. Verify
both the allowed restart and denial of an unrelated unit before starting the
LLM Glue service. Existing state created by an earlier root-run installation must
be deliberately migrated to `llm-glue:llm-glue`; the installer refuses to seize
operator-owned directories automatically.

OpenCode synchronization defaults to an `opencode.jsonc` document and may create
`<opencode-target>.llm-glue-ownership.json` beside it. Existing `.json` targets
remain supported when explicitly configured.
Schema v2 records provider keys and native-provider disablements per artifact
source, allowing local Ollama and remote central catalogs to coexist and reverse
only their own changes. V1 state migrates automatically. Keep this
machine-generated state with the target and do not edit it. Service installation
grants access through the configured target directory; corrupt state and
unowned/cross-source collisions fail closed before the document is changed.

OpenCode scope is selected by destination path. Use the system document for one
machine-wide catalog:

```toml
[[client.targets]]
type = "opencode"
path = 'C:\ProgramData\opencode\opencode.jsonc'
```

Or list one target per selected user (and omit the system target):

```toml
[[client.targets]]
type = "opencode"
path = 'C:\Users\Andrew\.config\opencode\opencode.jsonc'

[[client.targets]]
type = "opencode"
path = 'C:\Users\OtherUser\.config\opencode\opencode.jsonc'
```

The artifact is fetched once and applied independently to each file. LLM Glue
does not enumerate profiles because a service identity cannot reliably infer the
intended interactive users. Every selected directory must be writable by the
service identity; `service install`/`repair` provisions the configured target
directories. Duplicate normalized target paths are rejected. Existing JSON/JSONC
is merged, unrelated settings remain operator-owned, and each destination's
adjacent ownership document records exactly which provider keys and
`disabled_providers` entries each artifact source may later change or remove.
Operator comments on unrelated settings and providers survive reconciliation;
LLM Glue emits a comment above each managed group. Comments within a managed
provider value are replaced with that owned value on the next synchronization.
Normally configure either the system target or selected user targets: when both
define the same OpenCode keys, OpenCode's system precedence wins.

To hide OpenCode's built-in/default provider catalog and show only selected LLM
Glue providers, add an operator-owned provider allowlist to the same JSONC file:

```jsonc
"enabled_providers": [
  "llm-glue-ollama-local-ollama",
  "llm-glue-llama-cpp-labhost03-router"
]
```

Use the provider keys generated for that installation. OpenCode 2.2.7 normalizes
this legacy-compatible setting to a deny-all `provider.use` policy followed by
explicit allows. LLM Glue deliberately preserves this setting instead of owning
or generating it: an operator may still need a non-LLM-Glue provider, and a
catalog change must not silently alter that policy.

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

Maintainers can repeat the isolated elevated lifecycle gates with
`scripts/Test-WindowsServiceLive.ps1` (optionally `-AllRoles`) and, on a disposable
systemd host with no existing LLM Glue resources,
`scripts/Test-LinuxServiceLive.sh`. Both scripts refuse an existing service; the
Linux script additionally refuses an existing account or owned service directory.
To combine the current LocalService ownership-sidecar gate with real ProgramData
and second-user effective-catalog qualification, run elevated:

```powershell
.\scripts\Test-WindowsServiceLive.ps1 -AllRoles -SystemOpenCode `
  -OpenCodeExecutable C:\path\to\opencode.exe
```

This mode also refuses an existing `%ProgramData%\opencode` directory. It creates
a random temporary standard account, runs the supplied OpenCode binary under that
account, verifies the generated system provider through `/api/model`, and removes
the account, profile, copied binary, service, and ProgramData directory it created.
The random password is held only in process memory and is never logged.
The current Windows PowerShell 5.1 run passed against OpenCode 2.2.7 and real
Ollama 0.40.16, including LocalService relocation/repair and service rollback.
