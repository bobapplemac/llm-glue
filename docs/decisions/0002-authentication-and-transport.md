# ADR 0002: Authentication and transport policy

- Status: Accepted for r0 implementation
- Date: 2026-11-10

## Decision

Loopback-only HTTP may omit authentication. Any non-loopback listener requires
bearer authentication for reads and a distinct write credential for Agent
inventory submission. Remote bare HTTP is rejected unless the operator explicitly
acknowledges insecure transport; an encrypted private network or reverse proxy is
recommended. Forwarding headers never determine whether a peer is loopback.

Tokens are supplied through protected sidecar values or environment references,
are compared without diagnostic disclosure, support rotation, and are redacted
from logs. Generated artifacts contain environment references, never secrets.

## Threats addressed

- Catalog/config disclosure and malicious inventory injection.
- Bearer-token interception over unencrypted networks.
- Spoofed proxy headers bypassing remote authentication.
- Path traversal, oversized submissions, and secret leakage through diagnostics.
- Remote command execution disguised as target activation.

Kestrel request limits, strict schemas, local allowlisted adapters, safe path
resolution, bounded timeouts, and log redaction provide defense in depth. TLS
termination remains outside LLM Glue v1.
