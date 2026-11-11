# Security model

LLM Glue has no remote execution or model-management API in v1. Its remote surface
is inventory submission and read-only catalog/artifact retrieval.

- Kestrel defaults to loopback and serves plain HTTP. Remote binding requires
  explicit insecure-transport acknowledgement plus separate read and write bearer
  credentials. Use an encrypted private network or operator TLS reverse proxy.
- Proxy headers never convert a remote peer into loopback. The unauthenticated
  loopback exception applies only when the configured Server listener is itself
  loopback; a non-loopback deployment still authenticates requests arriving from
  a local TLS proxy. Tokens are fixed-time compared, environment-referenced where
  practical, and never logged or embedded in generated artifacts.
- Agent-only hosts use the write credential only for canonical inventory POSTs;
  read credentials cannot authorize submission. Exact server exclusions affect
  publication only and never issue provider or filesystem operations.
- Provider metadata and model cards are untrusted data. LLM Glue does not execute
  repository code or model-card instructions.
- GGUF reads are allowlisted and allocation-bounded. HTTP artifacts have size and
  schema limits. Target paths are explicit; no recursive user-directory deletion
  exists.
- Provider, Agent, Client, and health endpoints accept only HTTP(S) URLs and
  reject URI user-information fields so credentials cannot leak through URL
  diagnostics. Authentication uses the dedicated protected secret settings.
- Local LiteLLM activation accepts a constrained service identity, invokes fixed
  `systemctl`/`sc.exe` operations without a shell, and health-checks before commit.
- LLM Glue itself runs as Windows LocalService or a fixed, non-login Linux
  `llm-glue` account. Service installation grants read access to the executable
  and sidecar plus write access only to configured state, log, and target
  directories. Existing Linux directories are verified, not silently
  re-permissioned; installation fails if the service account cannot write them.
  The host uses the authorized log directory as its content root, avoiding any
  need to broaden read/list ACLs on executable ancestors after relocation.
- Persisted Server catalogs are written under the configured owned state directory
  and restored only after canonical inventory and artifact SHA-256 validation.
  Retention ignores non-hash/operator directories.
- Updates and installers are pinned to `bobapplemac/llm-glue`, accept stable `rN`
  releases only, and verify SHA-256 manifests. Release publication rechecks the
  upstream immediately before mutation.
- Bootstrap installers execute and validate the checksum-verified staged binary
  and candidate TOML before privileged copying. Secret prompts are non-echoing;
  generated sidecars receive owner-only Linux mode or a restricted Windows ACL.
  Existing sidecars are validated and preserved.

Tokens over bare HTTP are visible to a network observer. File permissions—not
reversible application encryption with a colocated key—protect sidecars. Operators
must restrict sidecar/log/state access and rotate tokens after suspected exposure.
Environment references remain preferred for interactive/managed environments.
Headless services may instead use `read_token`, `write_token`, or `token` in the
single TOML sidecar when its ACL/mode protects the service identity; literal and
environment sources are mutually exclusive. `doctor` reports only source and
presence, never the literal value.
