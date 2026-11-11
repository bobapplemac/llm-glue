# Release process

Official publication is intentionally fail-closed and is not authorized merely by
building the repository. Public identity is `rN`; development remains `r0`.

Prerequisites: .NET 10 SDK, Git, GNU make/bash/zip on Linux or PowerShell on
Windows, and GitHub CLI authenticated to the repository. The release branch must
be clean, attached, tracking an upstream, already pushed, and exactly equal to the
freshly fetched upstream. The next revision must increment by one and must not
already exist as a tag or release.

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

The first official release additionally requires explicit owner authorization and
all live M4 gates. Do not publish `r0`.

CI runs isolated local-Git matrices for both publishers covering a clean/no-tag
success path plus dirty, ahead, behind, detached, tag collision, GitHub release
collision, ambiguous/wrong remote, and unavailable-remote failures. Bash also
simulates a post-tag GitHub release failure against a temporary bare repository
and verifies the non-destructive recovery message. These tests use a fake `gh`
and cannot create a real GitHub release; the clean authenticated remote path remains
an explicit live release gate.
