# ADR 0020: source-generated CLI and configuration references

## Status

Accepted, 2026-11-11.

## Context

Operator documentation must cover every CLI parameter and every accepted TOML
setting. Independent hand-maintained lists drift as commands and configuration
scopes evolve, particularly when defaults or validation rules change.

## Decision

Maintain typed CLI and configuration reference catalogs in the C# source. TOML
unknown-key validation derives its per-scope allow-lists from the configuration
catalog. Built-in CLI help derives from the CLI catalog. Deterministic Markdown
generation writes `docs/cli-reference.md` and
`docs/configuration-reference.md` using UTF-8 without a byte-order mark and LF
line endings.

A normal non-RID build runs the generator. `llm-glue docs generate` supports an
explicit output directory, while `--check` performs no writes and exits nonzero
when output is absent or stale. Make targets and guarded release publishers
expose and enforce that check. Cross-RID publishes skip executing their target
apphost because a host cannot execute an apphost built for another platform.

## Consequences

Adding a TOML key requires catalog metadata before the parser can accept it.
Adding a command or parameter requires updating the CLI catalog used by help and
the reference page. Generated Markdown is reviewable and linkable but must not
be edited directly. Tests cover catalog uniqueness, deterministic output, and
drift detection.
