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
