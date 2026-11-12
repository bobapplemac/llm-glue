# CLI reference

This file is generated from the executable's typed CLI reference catalog. Do not edit it manually.

## Global parameters

| Parameter | Description |
|---|---|
| `--config PATH` | Use an explicit TOML sidecar instead of automatic config discovery. |
| `--help, -h, /?` | Show help without loading configuration. |
| `--version` | Print the product revision without loading configuration. |

## `run`

Run enabled roles in the foreground; this is also the default when no command is supplied.

No command-specific parameters.

## `help`

Show help without loading configuration.

No command-specific parameters.

## `setup`

Interactively detect integrations, generate protected system configuration, install the canonical binary, and register/start the system service without loading an existing configuration.

| Parameter | Description |
|---|---|
| `--roles LIST` | Comma-separated agent, server, and/or client roles. |
| `--providers LIST` | Comma-separated ollama and/or llama.cpp Agent providers. |
| `--targets LIST` | Comma-separated opencode and/or litellm Client targets. |
| `--server-url URI` | Remote Server URL for Agent/Client roles when Server is not co-located. |
| `--read-token TOKEN` | Remote Client read token, or explicit local Server read token. |
| `--write-token TOKEN` | Remote Agent write token, or explicit local Server write token. |
| `--server-public-key-sha256 HASH` | Pin a remote Server SubjectPublicKeyInfo SHA-256 instead of accepting any self-signed certificate. |
| `--ollama-url URI` | Ollama API base URL. |
| `--llama-url URI` | llama.cpp API/router base URL. |
| `--model-directory PATH` | Host-visible allowlisted GGUF directory for llama.cpp inspection. |
| `--listen URI` | Server listener; defaults to https://0.0.0.0:8765 during setup. |
| `--tls-server-names LIST` | Comma-separated exact/wildcard SNI names and self-signed certificate SANs. |
| `--litellm-url URI` | Client-reachable LiteLLM URL embedded in generated OpenCode routes. |
| `--inference-api-key TOKEN` | LiteLLM key embedded in authenticated OpenCode artifacts. |
| `--opencode-path PATH` | System OpenCode JSONC destination. |
| `--litellm-generated-path PATH` | Dedicated LLM Glue LiteLLM model-list destination. |
| `--litellm-primary-path PATH` | Operator-owned LiteLLM primary YAML containing the managed include. |
| `--litellm-service NAME` | Allowlisted service restarted after generated LiteLLM changes. |
| `--litellm-health-url URI` | Post-activation LiteLLM health endpoint. |
| `--non-interactive` | Use supplied options and defaults without prompting. |
| `--dry-run` | Print canonical installation intent without writing files or services. |
| `--replace-config` | Explicitly replace an existing canonical sidecar. |

## `uninstall`

Stop and remove the system service and binary, reconcile owned OpenCode entries, and empty managed LiteLLM routes while preserving configuration, state, and logs.

| Parameter | Description |
|---|---|
| `--clean` | Also remove owned configuration, state, logs, certificates, update staging, and the fixed Linux service account; fail closed around operator-owned integration references. |
| `--dry-run` | Print the exact uninstall scope without changing the system. |

## `status`

Fetch and print Server catalog status.

No command-specific parameters.

## `discover`

Run one discovery pass and print the normalized snapshot without publishing it.

No command-specific parameters.

## `doctor`

Check configured secret sources, providers, and target paths.

No command-specific parameters.

## `tls validate`

Load and validate the configured certificate, private key, validity, Server Authentication usage, and chain without changing the running Server.

No command-specific parameters.

## `tls reload`

Ask the local running Server to validate and adopt the certificate files for new TLS connections without restarting.

No command-specific parameters.

## `tls renew`

Issue and immediately adopt a fresh self-signed certificate using the existing RSA private key and configured validity, preserving the public-key pin.

No command-specific parameters.

## `tls regenerate`

Ask the local running Server to transactionally replace its current certificate with a new self-signed certificate and key using the configured validity, then adopt it immediately.

| Parameter | Description |
|---|---|
| `--force` | Allow replacement when the current certificate is not self-signed. |

## `config validate`

Parse and validate the selected TOML configuration.

No command-specific parameters.

## `config preview TARGET`

Discover once and print a generated artifact.

| Parameter | Description |
|---|---|
| `TARGET` | opencode or litellm; defaults to opencode when omitted by the current parser. |

## `sync --once`

Perform one Client artifact synchronization cycle.

| Parameter | Description |
|---|---|
| `--once` | Required sync action selector. |

## `service ACTION`

Manage the operating-system service.

| Parameter | Description |
|---|---|
| `ACTION` | install, repair, start, stop, status, or uninstall; defaults to status. |
| `--dry-run` | Print service installation intent without changing service state. |

## `update check`

Check GitHub Releases for a newer stable revision.

No command-specific parameters.

## `update apply`

Download, verify, stage, and activate a stable update.

| Parameter | Description |
|---|---|
| `--service` | Restart and health-check the registered service during activation. |
| `--allow-downgrade` | Allow applying an older stable revision. |

## `update rollback`

Restore the retained rollback executable.

| Parameter | Description |
|---|---|
| `--service` | Restart and health-check the registered service after rollback. |

## `docs generate`

Generate the CLI and TOML reference documents without loading configuration.

| Parameter | Description |
|---|---|
| `--output DIRECTORY` | Destination directory; defaults to docs under the current directory. |
| `--check` | Do not write; return nonzero if committed references differ. |

## `update activate-staged`

Internal Linux post-exit update activator.

> Internal command. It is documented for completeness and is not an operator workflow.

| Parameter | Description |
|---|---|
| `--wait-process-id PID` | Initiating process to wait for. |
| `--source PATH` | Staged replacement executable. |
| `--destination PATH` | Installed executable destination. |
| `--fallback PATH` | Rollback executable. |
| `--service` | Restart and verify the service. |
| `--health-url URI` | Optional authenticated Server health URL. |
| `--result PATH` | Activation result file. |
