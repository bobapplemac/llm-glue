# ADR 0003: Release, update, and service safety

- Status: Accepted for r0 implementation
- Date: 2026-11-11

## Decision

Public releases use only immutable `rN` tags from the pinned
`bobapplemac/llm-glue` repository. Both release entry points require a clean,
attached, pushed, nondivergent branch; exact next revision; absent tag/release;
successful tests and dual-RID publishes; SHA-256 manifest; a second remote-head
check; and explicit confirmation. They never repair Git state automatically.
They also require exactly one `origin` remote, the expected upstream branch, and
the pinned raw GitHub repository URL. Transport rewriting is used only inside
isolated local-Git tests.

Bootstrap and update clients accept stable `rN` releases only, select the exact
RID asset, and verify `SHA256SUMS` before replacement. They retain one executable
rollback. Windows stages replacement through a hidden post-exit helper because a
running executable cannot be overwritten; Linux atomically replaces the path.
With explicit `--service`, both platforms restart the fixed LLM Glue service and
verify HTTP health or service running state. Activation failure or interruption
restores and reverifies the fallback; Windows records the asynchronous result.

Service installation invokes fixed SCM/systemd operations without a shell and
records absolute executable and sidecar paths. Relocation requires `service
repair`. Uninstall removes only the service registration/unit and preserves
configuration, logs, state, and target files.

## Consequences

`r0` can never be published by the release tooling. A partial remote publication
is reported for manual recovery and is never automatically deleted. Automated
fault-injection covers successful activation, failed health, interruption, and
fallback verification. Live SCM/systemd update/rollback qualification remains
mandatory before r1 authorization.
