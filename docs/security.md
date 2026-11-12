# Security model

LLM Glue has no remote execution or model-management API in v1. Its remote surface
is inventory submission and read-only catalog/artifact retrieval.

- Kestrel defaults to loopback HTTPS on port 8765. It loads only file-based
  PFX/P12 or PEM/CRT-plus-key material and can create a 398-day self-signed PEM
  pair with same-key renewal 90 days before expiry. Remote plain HTTP requires
  explicit acknowledgement; non-loopback access
  still requires separate read and write bearer credentials.
- Platform certificate validation is the outbound default. A SHA-256
  SubjectPublicKeyInfo pin authenticates a configured self-signed Server without
  trusting it machine-wide. `allow_invalid_server_certificate = true` provides
  encryption without server authentication and is vulnerable to an active
  man-in-the-middle; it is mutually exclusive with pinning.
- TLS reload, same-key self-signed renewal, and regeneration are local-only POST
  operations. When a Server write token is configured they require it even on
  loopback. Candidate
  leaf/key/chain material is fully loaded before the active context changes; a
  failed operation retains the old certificate. There is no certificate watcher.
- Proxy headers never convert a remote peer into loopback. The unauthenticated
  loopback exception applies only when the configured Server listener is itself
  loopback; a non-loopback deployment still authenticates requests arriving from
  a local TLS proxy. Tokens are fixed-time compared and never logged.
- Authenticated OpenCode artifacts embed the LiteLLM client key by default so a
  remote client needs only its LLM Glue read token. Possession of that token can
  therefore retrieve the inference credential. Protect transport, server state,
  and installed JSONC permissions, and rotate both tokens after suspected
  disclosure. An explicit server opt-out uses the client-side
  `LLM_GLUE_INFERENCE_API_KEY` environment variable instead. LiteLLM artifacts,
  diagnostics, inventory, status, health, and logs never contain the key.
- Agent-only hosts use the write credential only for canonical inventory POSTs;
  read credentials cannot authorize submission. Exact server exclusions affect
  publication only and never issue provider or filesystem operations.
- Provider metadata and model cards are untrusted data. LLM Glue does not execute
  repository code or model-card instructions.
- GGUF reads are allowlisted and allocation-bounded. Explicit container-to-host
  prefix mappings still pass through canonical allowlist validation. HTTP
  artifacts have size/schema limits. Target paths are explicit; no recursive
  user-directory deletion exists.
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

Tokens and embedded artifact credentials over bare HTTP are visible to a network
observer. File permissions—not reversible application encryption with a colocated
key—protect sidecars, retained artifacts, and installed JSONC. Operators must
restrict sidecar/log/state/target access and rotate tokens after suspected
exposure. Environment references remain available for managed server secrets and
for inference credentials when embedding is disabled. Headless services may use
`read_token`, `write_token`, or `token` in the
single TOML sidecar when its ACL/mode protects the service identity; literal and
environment sources are mutually exclusive. `doctor` reports only source and
presence, never the literal value.

See [ADR 0021](decisions/0021-file-based-kestrel-tls-and-public-key-pinning.md)
for the TLS file, SNI, self-signed generation, and pinning boundary.
