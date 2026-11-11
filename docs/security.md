# Security model

LLM Glue has no remote execution or model-management API in v1. Its remote surface
is inventory submission and read-only catalog/artifact retrieval.

- Kestrel defaults to loopback and serves plain HTTP. Remote binding requires
  explicit insecure-transport acknowledgement plus separate read and write bearer
  credentials. Use an encrypted private network or operator TLS reverse proxy.
- Proxy headers never convert a remote peer into loopback. Tokens are fixed-time
  compared, environment-referenced where practical, and never logged or embedded
  in generated artifacts.
- Provider metadata and model cards are untrusted data. LLM Glue does not execute
  repository code or model-card instructions.
- GGUF reads are allowlisted and allocation-bounded. HTTP artifacts have size and
  schema limits. Target paths are explicit; no recursive user-directory deletion
  exists.
- Local LiteLLM activation accepts a constrained service identity, invokes fixed
  `systemctl`/`sc.exe` operations without a shell, and health-checks before commit.
- Updates and installers are pinned to `bobapplemac/llm-glue`, accept stable `rN`
  releases only, and verify SHA-256 manifests. Release publication rechecks the
  upstream immediately before mutation.

Tokens over bare HTTP are visible to a network observer. File permissions—not
reversible application encryption with a colocated key—protect sidecars. Operators
must restrict sidecar/log/state access and rotate tokens after suspected exposure.
