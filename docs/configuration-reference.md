# TOML configuration reference

This file is generated from the same setting catalog used to reject unknown TOML keys. Do not edit it manually.

## `root`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `[roles]` | `table` | `required` | Enables the composable Agent, Server, and Client roles. |
| `[agent]` | `table` | `empty` | Agent discovery, export, and remote-submission policy. |
| `[server]` | `table` | `empty` | Server listener, publication, authentication, naming, and persistence settings. |
| `[client]` | `table` | `empty` | Artifact source, polling, authentication, and target settings. |
| `[logging]` | `table` | `empty` | Console and rolling-file logging settings. |

## `roles`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `roles.agent` | `boolean` | `false` | Run provider discovery and optional remote inventory submission. |
| `roles.server` | `boolean` | `false` | Run the catalog API, reconciliation, and artifact generation. |
| `roles.client` | `boolean` | `false` | Poll and apply generated target artifacts. |

## `agent`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `[[agent.providers]]` | `array of tables` | `[]` | Provider discovery endpoints. Required when the Agent role is enabled. |
| `agent.exclude_models` | `array of strings` | `[]` | Case-insensitive exact or * / ? model patterns removed before probing, publication, or submission. |
| `agent.submit_to` | `HTTP(S) URI` | `none` | Remote Server root for Agent-only inventory submission. |
| `agent.write_token_environment` | `string` | `none` | Environment variable containing the remote inventory write token. |
| `agent.write_token` | `secret string` | `none` | Literal remote inventory write token; mutually exclusive with write_token_environment. |
| `agent.allow_invalid_server_certificate` | `boolean` | `false` | Accept any TLS certificate for agent.submit_to; encrypts traffic without authenticating the Server. |
| `agent.trusted_server_public_key_sha256` | `64-character hexadecimal SHA-256` | `none` | Require the Server certificate SubjectPublicKeyInfo hash to match, regardless of platform trust. |
| `agent.discovery_interval_seconds` | `integer 1..3600` | `60` | Delay after each completed Agent discovery cycle. |
| `agent.removal_grace_seconds` | `integer 1..3600` | `600` | Time an absent deployment remains degraded before removal. |

## `agent.providers`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `agent.providers[].type` | `enum` | `required` | Provider adapter: ollama or llama.cpp. |
| `agent.providers[].name` | `string` | `required` | Stable provider instance ID used by diagnostics, exclusions, and namespace overrides. |
| `agent.providers[].url` | `HTTP(S) URI` | `required` | Provider API base URL. |
| `agent.providers[].model_directories` | `array of paths` | `[]` | Allowlisted host GGUF directories for llama.cpp inspection. |
| `agent.providers[].model_path_mappings` | `array of strings` | `[]` | Container-to-host path mappings written as provider-prefix=local-directory. |
| `agent.providers[].inspect_gguf` | `boolean` | `true` | Inspect allowlisted GGUF metadata for llama.cpp deployments. |
| `agent.providers[].hash_model_files` | `boolean` | `false` | Compute full model-file hashes; requires inspect_gguf. |
| `agent.providers[].probe_mode` | `enum` | `off` | Inference probing policy: off, loaded-only, or allow-load. |
| `agent.providers[].probe_timeout_seconds` | `integer 1..3600` | `15` | Per-inference-probe timeout. |
| `agent.providers[].discovery_timeout_seconds` | `integer 1..3600` | `300` | Provider inventory and router metadata-refresh timeout. |

## `server`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `server.listen` | `HTTP(S) URI` | `https://127.0.0.1:8765` | Kestrel listener; HTTPS uses file-based TLS settings below. |
| `server.opencode_inference_url` | `HTTP(S) URI` | `none` | Client-reachable LiteLLM base URL used by central OpenCode artifacts. |
| `server.embed_opencode_inference_api_key` | `boolean` | `true` | Embed the LiteLLM client key in authenticated OpenCode artifacts. |
| `server.opencode_inference_api_key_environment` | `string` | `none` | Environment variable containing the embedded LiteLLM client key. |
| `server.opencode_inference_api_key` | `secret string` | `none` | Literal embedded LiteLLM client key; mutually exclusive with its environment setting. |
| `server.opencode_namespace` | `namespace` | `llm-glue` | Default concise OpenCode provider namespace. |
| `[[server.opencode_namespace_overrides]]` | `array of tables` | `[]` | Ordered exact-match rules that split selected sources into other OpenCode namespaces. |
| `server.litellm_namespace` | `namespace` | `normalized hostname` | Server-scoped prefix for generated LiteLLM aliases. |
| `server.read_token_environment` | `string` | `none` | Environment variable containing the artifact/API read token. |
| `server.write_token_environment` | `string` | `none` | Environment variable containing the Agent inventory write token. |
| `server.read_token` | `secret string` | `none` | Literal artifact/API read token. |
| `server.write_token` | `secret string` | `none` | Literal Agent inventory write token. |
| `server.acknowledge_insecure_remote_http` | `boolean` | `false` | Required acknowledgement for a non-loopback plaintext HTTP listener. |
| `server.tls_auto_generate` | `boolean` | `true for HTTPS` | Create a 30-year self-signed PEM certificate/key when the configured files are absent. |
| `server.tls_certificate_path` | `path` | `state/tls/server.crt when auto-generated` | PFX, PEM, or CRT server certificate file. |
| `server.tls_private_key_path` | `path` | `state/tls/server.key when auto-generated` | PEM private-key file; required for PEM/CRT certificates and forbidden for PFX. |
| `server.tls_certificate_password_environment` | `string` | `none` | Environment variable containing a PFX password. |
| `server.tls_certificate_password` | `secret string` | `none` | Literal PFX password; mutually exclusive with the environment setting. |
| `server.tls_server_names` | `array of DNS patterns` | `[]` | Exact, *.suffix, or * SNI names; unmatched TLS handshakes are refused, localhost remains available for local management, and names become auto-certificate SANs. |
| `[[server.exclusions]]` | `array of tables` | `[]` | Publication-only exact/wildcard exclusion rules. |
| `server.state_directory` | `path` | `state` | Durable catalog and artifact history directory. |
| `server.artifact_retention` | `integer 1..3600` | `5` | Number of validated catalog/artifact states retained. |

## `server.opencode_namespace_overrides`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `server.opencode_namespace_overrides[].namespace` | `namespace` | `required` | OpenCode provider namespace assigned by this rule. |
| `server.opencode_namespace_overrides[].provider_id` | `string` | `none` | Exact provider ID selector. |
| `server.opencode_namespace_overrides[].instance_id` | `string` | `none` | Exact provider instance selector; at least one selector is required. |

## `server.exclusions`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `server.exclusions[].provider_id` | `string` | `none` | Exact provider ID selector. |
| `server.exclusions[].instance_id` | `string` | `none` | Exact provider instance selector. |
| `server.exclusions[].model_id` | `string` | `none` | Exact model ID selector. |
| `server.exclusions[].display_name` | `string` | `none` | Exact display-name selector. |
| `server.exclusions[].provider_model` | `string` | `none` | Exact provider/instance/model selector. |
| `server.exclusions[].model_pattern` | `wildcard string` | `none` | Case-insensitive * / ? pattern matched across model identities. |

## `client`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `client.source` | `HTTP(S) URI` | `in-process Server listener` | Server root from which artifacts and status are fetched. |
| `client.token_environment` | `string` | `none` | Environment variable containing the Server read token. |
| `client.token` | `secret string` | `none` | Literal Server read token; mutually exclusive with token_environment. |
| `client.allow_invalid_server_certificate` | `boolean` | `false` | Accept any TLS certificate for client.source; encrypts traffic without authenticating the Server. |
| `client.trusted_server_public_key_sha256` | `64-character hexadecimal SHA-256` | `none` | Require the Server certificate SubjectPublicKeyInfo hash to match, regardless of platform trust. |
| `client.poll_interval_seconds` | `integer 1..3600` | `60` | Delay between conditional artifact synchronization cycles. |
| `[[client.targets]]` | `array of tables` | `[]` | Explicit local OpenCode or LiteLLM destinations. |

## `client.targets`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `client.targets[].type` | `enum` | `required` | Target adapter: opencode or litellm. |
| `client.targets[].path` | `path` | `required` | Managed target file path. |
| `client.targets[].primary_config_path` | `path` | `required for litellm` | Operator-owned LiteLLM primary YAML containing the generated include. |
| `client.targets[].service_name` | `string` | `required for litellm` | Allowlisted service identity restarted after LiteLLM changes. |
| `client.targets[].health_url` | `HTTP(S) URI` | `none` | Post-activation health endpoint. |
| `client.targets[].deny_builtin_opencode_provider` | `boolean` | `true` | Add an owned policy hiding OpenCode's built-in catalog. |

## `logging`

| Setting | Type | Default / requirement | Description |
|---|---|---|---|
| `logging.level` | `enum` | `Information` | Trace, Debug, Information, Warning, Error, or Critical. |
| `logging.file` | `path` | `logs/llm-glue.log` | Rolling log file path. |
| `logging.archive_size_mb` | `integer 1..3600` | `10` | Maximum active log size before rotation. |
| `logging.retention` | `integer 1..3600` | `7` | Number of archived log files retained. |

