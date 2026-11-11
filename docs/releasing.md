# Release process

Official publication is intentionally fail-closed and is not authorized merely by
building the repository. Public identity is `rN`; development remains `r0`.

Prerequisites: .NET 10 SDK, Git, GNU make/bash/zip on Linux or PowerShell on
Windows, and GitHub CLI authenticated to the repository. The release branch must
be clean, attached, tracking an upstream, already pushed, and exactly equal to the
freshly fetched upstream. The next revision must increment by one and must not
already exist as a tag or release.

The latest pre-release qualification ran the complete PowerShell path in an
isolated clean clone at commit `292fec7`: preflight, 103 tests, both self-contained
RIDs, r1/`1.1.0.0` version checks, archives, checksums, and exact-commit manifest
passed. `-DryRun` stopped before tag or GitHub release creation. This evidence
does not replace the required authenticated check against the final pushed commit
or live update/apply from the official immutable asset.

An authenticated publication rehearsal at `51932ef` also confirmed that the
guarded path leaves no tag or GitHub release after a local pre-publication error.
It exposed and corrected a PowerShell remainder-argument binding defect in the
annotated-tag call; the final release must be rebuilt and rechecked from the
subsequent correction commit.

Official `r1` was published from
`dd070d215d91f3518ce71a443d8867ddd11b91e0` on 2026-11-11. The published archive
SHA-256 values are `467502d273c8ed39d3f0c1bbc376fc41e4235b0924422a4899bfc12c16d76ea5`
for linux-x64 and
`a5525380d87a94874c34bcf75fc94d9bca80c71361be1903839d881a440885a9`
for win-x64. GitHub's asset digests matched `SHA256SUMS`; a live isolated r0
Windows executable downloaded, verified, applied, and rolled back the official
asset with exact original-hash restoration.

Both publishers require exactly one remote named `origin`, an `origin/<branch>`
upstream, and the pinned raw remote URL `github.com/bobapplemac/llm-glue`. This
prevents an otherwise clean checkout from publishing the project identity from an
ambiguous or substituted repository.

Dry runs:

```text
make release-check REVISION=1
make release REVISION=1 DRY_RUN=1
pwsh ./scripts/Publish-Release.ps1 -Revision 1 -DryRun
```

Both implementations build/test both RIDs, create immutable archives, SHA-256
checksums, and a commit-linked manifest. They fetch and compare the remote again
immediately before publication. They never commit, push normal commits, rewrite
tags, force-push, or clean the worktree. A partial remote failure is reported for
manual recovery; existing remote state is never automatically deleted.

Release properties map rN to stable AssemblyVersion `1.0.0.0`, revision-tracking
FileVersion `1.N.0.0`, package version `1.N.0`, and runtime product identity `rN`.
The development build remains Assembly/File `0.0.0.0` and reports `r0`.
Direct `make package REVISION=N` and `scripts/package.sh ... N` calls reject
nonzero revisions. Only the guarded official release entry points set the scoped
authorization needed to build an `rN` package after preflight. This prevents test
or qualification artifacts from accidentally acquiring a public release identity.

The first official release additionally requires explicit owner authorization and
all live M4 gates. Do not publish `r0`.

CI runs isolated local-Git matrices for both publishers covering a clean/no-tag
success path plus dirty, ahead, behind, detached, tag collision, GitHub release
collision, ambiguous/wrong remote, and unavailable-remote failures. Bash also
simulates a post-tag GitHub release failure against a temporary bare repository
and verifies the non-destructive recovery message. These tests use a fake `gh`
and cannot create a real GitHub release; the clean authenticated remote path remains
an explicit live release gate.
