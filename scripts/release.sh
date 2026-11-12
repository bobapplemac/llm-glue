#!/usr/bin/env bash
set -euo pipefail

revision=""
dry_run=0
check_only=0
branch="main"
while (($#)); do
  case "$1" in
    --revision) revision="$2"; shift 2 ;;
    --dry-run) dry_run=1; shift ;;
    --check-only) check_only=1; shift ;;
    --branch) branch="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

[[ "$revision" =~ ^[1-9][0-9]*$ ]] || { echo "official revision must be a positive integer; r0 cannot be released" >&2; exit 2; }
for tool in git gh dotnet make; do command -v "$tool" >/dev/null || { echo "required tool is missing: $tool" >&2; exit 1; }; done
root="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "not in a Git repository" >&2; exit 1; }
cd "$root"
[[ "$(git symbolic-ref --short -q HEAD)" == "$branch" ]] || { echo "release requires branch $branch and rejects detached HEAD" >&2; exit 1; }
[[ -z "$(git status --porcelain=v1 --untracked-files=all)" ]] || { echo "worktree must be completely clean" >&2; exit 1; }
upstream="$(git rev-parse --abbrev-ref '@{upstream}' 2>/dev/null)" || { echo "branch has no upstream" >&2; exit 1; }
mapfile -t remotes < <(git remote)
[[ "${#remotes[@]}" -eq 1 && "${remotes[0]}" == "origin" ]] || { echo "release requires exactly one remote named origin" >&2; exit 1; }
[[ "$upstream" == "origin/$branch" ]] || { echo "upstream must be origin/$branch" >&2; exit 1; }
origin_url="$(git config --get remote.origin.url)"
[[ "$origin_url" =~ ^(https://github\.com/|git@github\.com:)bobapplemac/llm-glue(\.git)?$ ]] || { echo "origin is not the pinned bobapplemac/llm-glue repository" >&2; exit 1; }
git fetch --prune
local_head="$(git rev-parse HEAD)"
remote_head="$(git rev-parse '@{upstream}')"
[[ "$local_head" == "$remote_head" ]] || { echo "local HEAD must equal $upstream (no ahead/behind/divergence)" >&2; exit 1; }
tag="r$revision"
! git rev-parse -q --verify "refs/tags/$tag" >/dev/null || { echo "tag $tag already exists" >&2; exit 1; }
! gh release view "$tag" >/dev/null 2>&1 || { echo "GitHub release $tag already exists" >&2; exit 1; }
latest="$(git tag -l 'r[1-9]*' | sed 's/^r//' | sort -n | tail -1)"
latest="${latest:-0}"
[[ "$revision" -eq $((latest + 1)) ]] || { echo "revision must increment exactly: expected r$((latest + 1))" >&2; exit 1; }

echo "preflight passed for $tag at $local_head"
((check_only)) && exit 0
dotnet test LlmGlue.slnx -c Release
dotnet src/LlmGlue/bin/Release/net10.0/llm-glue.dll docs generate --output docs --check
[[ -z "$(git status --porcelain=v1 --untracked-files=all)" ]] || { echo "build or generated reference documentation changed the worktree" >&2; exit 1; }
LLM_GLUE_OFFICIAL_RELEASE_BUILD=1 make package REVISION="$revision"
[[ -x "artifacts/publish/linux-x64/llm-glue" && -f "artifacts/publish/win-x64/llm-glue.exe" ]] || { echo "required publish outputs are missing" >&2; exit 1; }
git fetch --prune
[[ "$(git rev-parse '@{upstream}')" == "$local_head" ]] || { echo "remote changed after build; aborting" >&2; exit 1; }
if ((dry_run)); then echo "dry run: would create tag and GitHub release $tag"; exit 0; fi
read -r -p "Publish immutable GitHub release $tag? Type $tag: " confirmation
[[ "$confirmation" == "$tag" ]] || { echo "confirmation did not match" >&2; exit 1; }
git tag -a "$tag" -m "LLM Glue $tag"
if ! git push origin "$tag"; then
  echo "publication stopped after creating local tag $tag; the remote tag was not confirmed. Inspect local and origin state manually." >&2
  exit 1
fi
if ! gh release create "$tag" artifacts/packages/llm-glue-r${revision}-* artifacts/packages/SHA256SUMS artifacts/packages/manifest.json --verify-tag --title "LLM Glue $tag" --generate-notes; then
  echo "partial publication: tag $tag was pushed but the GitHub release failed. Inspect the remote tag and create the matching release manually; nothing was deleted." >&2
  exit 1
fi
