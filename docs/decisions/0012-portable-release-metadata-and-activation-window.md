# ADR 0012: Portable release metadata and bounded cold-start activation

Status: accepted  
Date: 2026-11-11

## Context

The guarded Windows publisher for r2 wrote `SHA256SUMS` with CRLF line endings.
The Linux bootstrap's exact filename match retained the carriage return, so
`sha256sum --check --strict` rejected the otherwise valid official checksum
before changing the installed service. Manual normalization proved the archive
matched its published digest.

The subsequent live deployment also showed that the labhost03 LiteLLM Compose
service can require 45–50 seconds to become healthy after recreation. The r2
activator allowed only ten attempts separated by two seconds. It therefore
restored the prior generated file even though LiteLLM was starting normally.

## Decision

- Release metadata is UTF-8 without a BOM and uses LF on every publisher host.
- The Linux bootstrap additionally strips carriage returns from downloaded
  checksum input before selecting and strictly verifying the requested asset.
  Defense at both boundaries supports existing immutable releases and prevents
  recurrence.
- Controlled service health checks retain a five-second per-request bound and
  retry at two-second intervals for at most 60 attempts. This provides a bounded
  approximately two-minute cold-start window without weakening the requirement
  that activation must become healthy or roll back.

## Consequences

The Bash bootstrap regression fixture deliberately serves CRLF checksums. The
same workflow still rejects a mismatched digest. Slow but healthy model routers
can converge, while failed activation remains time-bounded and transactional.
r2 itself remains immutable and retains both limitations; the corrections take
effect in the next authorized release.
