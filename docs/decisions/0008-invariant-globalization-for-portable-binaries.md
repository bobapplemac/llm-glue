# ADR 0008: Invariant globalization for portable binaries

## Status

Accepted for r1.

## Context

The self-contained Linux x64 qualification binary aborted on a clean Debian 13.5
installation because the OS did not contain ICU. The bootstrap contract promises
a portable single-file application without an additional .NET or application
dependency installation. LLM Glue has no localized user interface or locale-aware
business rules: network contracts, hashes, configuration numbers, timestamps,
version parsing, and generated artifacts are intentionally culture-independent.

## Decision

Publish and run LLM Glue with .NET invariant globalization enabled. Parsing and
serialization that form product contracts continue to use explicit invariant or
machine formats. User-facing diagnostics remain English for r1.

This removes the undeclared ICU runtime dependency from the Linux artifact. It
does not remove normal platform dependencies supplied by the supported OS, such
as glibc and the system service manager.

## Consequences

- The self-contained binary runs on the supported minimal Debian 13 environment
  without installing ICU.
- Locale-sensitive formatting, collation, and non-invariant culture creation are
  intentionally unavailable. New code must not introduce a requirement for them
  without revisiting this decision and the installer contract.
- Windows and Linux builds use the same invariant product behavior.
