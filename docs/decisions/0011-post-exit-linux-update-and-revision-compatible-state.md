# ADR 0011: Post-exit Linux update and revision-compatible state

- Status: Accepted after r1 incident
- Date: 2026-11-11

## Context

The first live Linux official-asset upgrade from the persistent labhost03 r0
service exposed four assumptions that Windows-only official-asset qualification
did not cover:

1. Replacing a running .NET single-file executable invalidated later lazy bundle
   loads. The updater then failed to load `System.IO.Pipes` when launching
   `systemctl`; rollback verification failed for the same reason.
2. `System.Formats.Tar` extraction did not restore the executable bit, while the
   service replacement path copied the extracted mode.
3. Artifact validation required the ownership header to equal the currently
   running product revision, so r1 rejected valid, hash-verified r0 LiteLLM state.
4. The service health probe did not attach the configured read bearer token even
   though a non-loopback Server correctly required authentication.

The original r0 process never stopped during the first failure. A subsequent
manual r1 start exposed the state-compatibility defect and was immediately
restored from the preserved r0 binary. No state or generated target was lost.

## Decision

Linux update and rollback use a staged post-exit activator. The initiating process
downloads and verifies the archive, restores the replacement mode from the
installed executable, preserves the fallback, starts an untouched staged binary,
and exits. The staged binary waits for the initiator, atomically replaces the
destination, restarts and verifies the service, and records a result file. It can
therefore load runtime assemblies and launch child processes from a stable bundle.

Health credentials are passed only through the activator environment, never its
command line or result log. Authenticated HTTP health remains mandatory when a
Server health URL is available; client-only services continue to use service
running state.

Persisted LiteLLM artifacts accept a syntactically valid generated ownership
header from any canonical `rN` revision. Content hash, state revision, schema,
target, filename, and secret checks remain unchanged. Revision identity is
proven by release metadata, not by requiring old generated content to claim it
was produced by the new executable.

## Consequences

The immutable r1 Linux updater cannot repair itself. Existing r0/r1 Linux
installations must use the bootstrap/manual stop-copy-start path for the
corrective release. Subsequent releases can use self-update normally.

CI now executes the published Linux single file as a staged activator, verifies
wait-and-replace behavior and executable mode, and launches a child `systemctl`
substitute from the installed result. Unit tests cover earlier-revision artifact
acceptance and activation output. A simulated r2 single file also loaded a copied
labhost03 r0 state and passed an authenticated health probe against the real
protected endpoint.
