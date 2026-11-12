# ADR 0021: file-based Kestrel TLS and public-key pinning

## Status

Accepted, 2026-11-11.

## Context

LLM Glue read/write tokens and authenticated OpenCode artifacts cross the Server
boundary. Relying on an external TLS proxy or operating-system certificate store
adds provisioning work, while private lab deployments commonly value transport
encryption more than public PKI trust.

## Decision

Kestrel supports only file-based certificates: PFX/P12 with an optional password,
or PEM/CRT plus a private-key file. The default HTTPS listener automatically
creates a 3072-bit RSA, SHA-256, Server Authentication certificate and PKCS#8 key
with a 30-year lifetime. Generated material is stored under the configured state
directory with owner-only Unix modes. Existing or partial material is never
silently replaced.

`tls_server_names` supplies exact, `*.suffix`, or catch-all SNI patterns. These
names become SANs for generated certificates, and unmatched handshakes are
refused. `localhost` remains an allowed SNI name for local management regardless
of that list. `localhost`, the machine hostname, and loopback IP SANs are included
in automatic certificates for local operations.

Agent submission and Client retrieval use normal chain/name validation by
default. Operators can pin the lowercase hexadecimal SHA-256 hash of the
certificate's SubjectPublicKeyInfo. A configured pin is authoritative regardless
of platform trust and rejects every nonmatching certificate. An explicit
accept-any option is also available for encryption
without authentication; it is mutually exclusive with a pin and applies only to
the configured LLM Glue Server, never provider or update traffic.

## Consequences

Clients do not need machine-wide trust-store changes. The Server prints its
public-key identifier through `doctor` without exposing private material.
Changing the key requires distributing a new pin; reissuing a certificate with
the same key does not. Accept-any mode does not prevent active interception and
must be documented accordingly. Windows imports a file key into a process-owned
cryptographic key container because Schannel cannot serve ephemeral private keys;
the certificate itself is not installed in a certificate store.

Certificate changes are explicit rather than watched automatically. `tls
validate` checks files without mutating the running process; authenticated local
`tls reload` swaps a validated certificate context for new TLS connections; and
`tls regenerate` creates and adopts a new self-signed certificate. Failed loads
leave the prior in-memory certificate active. Retired contexts remain alive until
shutdown so concurrent handshakes cannot observe disposed key material. External
automation such as ACME should install files, validate them, then call reload.
