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
Central installation asks for a LiteLLM client key and stores it in the protected
LLM Glue server sidecar. The server embeds that key in authenticated OpenCode
artifacts by default, so remote clients need only the LLM Glue server URL and
read token. LiteLLM's master key or a properly scoped virtual key may be used.
Set `server.embed_opencode_inference_api_key = false` to opt out; every
OpenCode/Paseo process must then receive `LLM_GLUE_INFERENCE_API_KEY`.

On Windows, a new service installation defaults to
`%ProgramData%\LLMGlue`, keeping the executable, sidecar, state, and logs in a
machine-scoped location suitable for LocalService. A non-service installation
defaults to `%LOCALAPPDATA%\LLMGlue` so it remains non-elevated. An explicit
`-InstallDirectory` always wins. If a service rerun detects an existing legacy
per-user installation and no ProgramData installation, it preserves the legacy
path and prints an explicit migration warning; pass the ProgramData path only
when intentionally migrating. Existing `.conf` or `.toml` sidecars are validated
and preserved without creating a competing filename.

Headless PowerShell installs use `-NonInteractive` with the corresponding named
parameters. `central-llama` requires `-InferenceApiKey` by default; use
`-DoNotEmbedInferenceApiKey` only for separately provisioned client environments.
The ordinary `remote-client` flow asks only for the LLM Glue URL and read token.
For compatibility with an opt-out server, explicitly passing `-InferenceApiKey`
still persists `LLM_GLUE_INFERENCE_API_KEY` for the current Windows user; restart
OpenCode/Paseo afterward so it inherits the value.
Headless Linux installs set `LLM_GLUE_NONINTERACTIVE=1`,
`LLM_GLUE_MODE`, and applicable variables: `LLM_GLUE_SERVER_URL`,
`LLM_GLUE_SERVER_TOKEN`, `LLM_GLUE_OLLAMA_URL`, `LLM_GLUE_LLAMA_URL`,
`LLM_GLUE_LISTEN_URL`, `LLM_GLUE_READ_TOKEN`, `LLM_GLUE_WRITE_TOKEN`,
`LLM_GLUE_MODEL_DIRECTORY`, `LLM_GLUE_OPENCODE_PATH`, the
`LLM_GLUE_LITELLM_*` URL/paths/service/health settings, and
`LLM_GLUE_INSTALL_SERVICE`. Central mode uses `LLM_GLUE_INFERENCE_API_KEY`; set
`LLM_GLUE_EMBED_INFERENCE_API_KEY=0` for the environment-reference opt-out.
`LLM_GLUE_INSTALL_DIR` and
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
for one credential. Selecting service installation in either bootstrap registers,
enables, and starts the service; initial target synchronization normally occurs
within seconds and no later than the configured client polling interval.

Before upgrading an older central installation, add either
`opencode_inference_api_key = "..."` or
`opencode_inference_api_key_environment = "..."` under `[server]`. To preserve
the former behavior instead, add `embed_opencode_inference_api_key = false`.
Bootstrap upgrades preserve existing configuration and never insert or rotate
this credential automatically.

Then run:

```text
llm-glue config validate --config /path/to/llm-glue.conf
llm-glue doctor --config /path/to/llm-glue.conf
llm-glue run --config /path/to/llm-glue.conf
```

An Agent-only host uses `agent.submit_to` and
`agent.write_token_environment`; see `examples/remote-agent.conf`. The destination
is the Server root, not the inventory endpoint. To prevent matching inventory
from leaving any Agent host, use case-insensitive exact names or `*`/`?` patterns:

```toml
[agent]
exclude_models = ["private-*", "ollama/workstation/experimental-?"]
```

Patterns match model ID, display name, route, `provider/model`, and
`provider/instance/model`. Agent exclusions are absent from submitted inventory
and therefore from server diagnostics.

Server publication filters use one or more rules such as:

```toml
[[server.exclusions]]
provider_id = "ollama"
instance_id = "workstation"
model_id = "private-model"
```

For a wildcard rule, use `model_pattern` and optionally scope it with exact
fields:

```toml
[[server.exclusions]]
provider_id = "llama.cpp"
instance_id = "labhost03-router"
model_pattern = "bench-*"
```

All supplied fields in a rule must match. Exact fields remain exact; only
`model_pattern` is case-insensitive and treats `*` and `?` specially. Server
exclusions hide catalog publication only: diagnostics retain the deployment and
identify the matching rule.

OpenCode models aggregate under the concise `llm-glue` provider by default:

```toml
[server]
opencode_namespace = "llm-glue"
```

To split a source into another namespace, add an ordered exact-match rule. The
`instance_id` is the source's configured `[[agent.providers]].name`:

```toml
[[server.opencode_namespace_overrides]]
namespace = "lab"
provider_id = "llama.cpp"
instance_id = "labhost03-router"
```

The first matching rule wins. Omit either selector to match all instances of a
provider or every provider with that instance ID. Multiple rules may aggregate
sources into the same namespace. Namespace values accept ASCII letters, digits,
and interior hyphens; built-in `opencode` and `ollama` IDs are reserved. A namespace cannot represent multiple direct endpoints,
and model IDs must be unique within it; split sources when validation reports a
collision. When local and remote LLM Glue controllers write the same OpenCode
target, give each server a distinct namespace so their source-scoped ownership
keys do not collide. Upgrading replaces the previously owned verbose provider
key on the next successful synchronization; saved model selections may need to
be reselected once.

LiteLLM aliases use a separate Server-specific namespace. By default LLM Glue
normalizes the machine hostname to lowercase ASCII letters, digits, and hyphens;
on labhost03 a model `bench-01-01` therefore becomes the LiteLLM alias
`labhost03-bench-01-01`. Pin it explicitly when the hostname may change:

```toml
[server]
litellm_namespace = "labhost03"
```

Central OpenCode entries reference that exact alias internally as `modelID`, but
users still select `llm-glue/bench-01-01`. Direct Ollama models also remain under
the single visible OpenCode namespace and do not use a LiteLLM alias. Model IDs
must normalize to unique aliases within one LiteLLM namespace; collisions fail
publication.

Without `--config`, lookup order is executable-adjacent `llm-glue.conf`, then
`llm-glue.toml`, followed by the same filenames in `/etc/llm-glue` on Linux or
`%ProgramData%\LLMGlue` on Windows. `/etc/llm-glue.conf` and
`/etc/llm-glue.toml` remain legacy Linux fallbacks. Run `llm-glue --help` without
a configuration file to see the complete command list.

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
# Allow the router's changed-preset metadata sweep to finish.
discovery_timeout_seconds = 300
```

Router discovery refreshes configured presets and publishes dormant routes without
loading them. `allow-load` may cause the runtime to load a dormant model and must
be selected explicitly. A loaded llama.cpp route also receives bounded `reasoning_effort=none`
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
Schema v3 records provider keys, native-provider disablements, and managed policy
statements per artifact source, allowing separately named local and remote
catalogs to coexist and reverse only their own changes. V1 and v2 state migrate
automatically. Keep this
machine-generated state with the target and do not edit it. Service installation
grants access through the configured target directory; corrupt state and
unowned/cross-source collisions fail closed before the document is changed.

OpenCode scope is selected by destination path. Use the system document for one
machine-wide catalog:

```toml
[[client.targets]]
type = "opencode"
path = 'C:\ProgramData\opencode\opencode.jsonc'
deny_builtin_opencode_provider = true
```

Or list one target per selected user (and omit the system target):

```toml
[[client.targets]]
type = "opencode"
path = 'C:\Users\Andrew\.config\opencode\opencode.jsonc'
deny_builtin_opencode_provider = true

[[client.targets]]
type = "opencode"
path = 'C:\Users\OtherUser\.config\opencode\opencode.jsonc'
deny_builtin_opencode_provider = true
```

The artifact is fetched once and applied independently to each file. LLM Glue
does not enumerate profiles because a service identity cannot reliably infer the
intended interactive users. Every selected directory must be writable by the
service identity; `service install`/`repair` provisions the configured target
directories. Duplicate normalized target paths are rejected. Existing JSON/JSONC
is merged, unrelated settings remain operator-owned, and each destination's
adjacent ownership document records exactly which provider keys,
`disabled_providers` entries, and policy statements each artifact source may
later change or remove.
Operator comments on unrelated settings and providers survive reconciliation;
LLM Glue emits a comment above each managed group. Comments within a managed
provider value are replaced with that owned value on the next synchronization.
Normally configure either the system target or selected user targets: when both
define the same OpenCode keys, OpenCode's system precedence wins.

OpenCode targets default `deny_builtin_opencode_provider = true`. LLM Glue adds
this current, schema-valid policy to hide OpenCode's built-in/default provider
catalog:

```jsonc
"policies": [
  { "action": "provider.use", "resource": "opencode", "effect": "deny" }
]
```
An identical pre-existing operator policy is preserved without being claimed.
Every unrelated policy and comment survives reconciliation. Set
`deny_builtin_opencode_provider = false` on that target to omit the default or
remove only the statement LLM Glue previously added. The deprecated
`enabled_providers` input is not used for this policy; the separate direct-Ollama
duplicate suppression remains compatible with the qualified OpenCode version.

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

Do not use the immutable r1 Linux binary's `update apply` command. A live r0 to
r1 service attempt exposed post-publication defects in running-bundle replacement,
tar mode restoration, authenticated health verification, and prior-revision
artifact loading. The service and state were restored intact. These defects are
corrected after r1, but r0/r1 cannot retroactively acquire the safe post-exit
activator. Upgrade those Linux installations to the corrective release using the
inspected bootstrap installer or an explicit stop/copy/start, then use normal
self-update for later revisions. The corrected service bootstrap stops an active
LLM Glue unit before replacing its executable and restores/restarts the prior
binary automatically if installation or activation fails.

Service mode preserves the sidecar, state, logs, and registration; restarts the
fixed `LLMGlue`/`llm-glue.service` identity; and checks `/api/v1/health` when the
Server role is enabled or SCM/systemd running state otherwise. Failed or
interrupted activation restores and reverifies the previous executable. Windows
and corrected Linux releases use post-exit helpers so the running single-file
bundle is never replaced in place. Their result-log paths are printed before the
initiating CLI exits.

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
