#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
publisher="$root/scripts/release.sh"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
tools="$scratch/tools"
mkdir -p "$tools"
cat > "$tools/gh" <<'EOF'
#!/usr/bin/env bash
if [[ "${1:-} ${2:-}" == "release view" && "${FAKE_GH_RELEASE_EXISTS:-0}" == 1 ]]; then exit 0; fi
if [[ "${1:-} ${2:-}" == "release view" ]]; then exit 1; fi
if [[ "${1:-} ${2:-}" == "release create" && "${FAKE_GH_CREATE_FAIL:-0}" == 1 ]]; then exit 42; fi
exit 0
EOF
chmod +x "$tools/gh"
cat > "$tools/make" <<'EOF'
#!/usr/bin/env bash
if [[ "${1:-}" == package ]]; then
  mkdir -p artifacts/publish/linux-x64 artifacts/publish/win-x64 artifacts/packages
  : > artifacts/publish/linux-x64/llm-glue
  chmod +x artifacts/publish/linux-x64/llm-glue
  : > artifacts/publish/win-x64/llm-glue.exe
  : > artifacts/packages/llm-glue-r1-linux-x64.tar.gz
  : > artifacts/packages/llm-glue-r1-win-x64.zip
  : > artifacts/packages/SHA256SUMS
  : > artifacts/packages/manifest.json
fi
exit 0
EOF
chmod +x "$tools/make"
export PATH="$tools:$PATH"

new_case() {
  local name="$1"
  case_root="$scratch/$name"
  case_bare="$case_root/origin.git"
  case_repo="$case_root/work"
  mkdir -p "$case_root"
  git init --bare --initial-branch=main "$case_bare" >/dev/null
  git clone "$case_bare" "$case_repo" >/dev/null 2>&1
  git -C "$case_repo" config user.name "LLM Glue Test"
  git -C "$case_repo" config user.email "test@example.invalid"
  printf 'fixture\n' > "$case_repo/fixture.txt"
  git -C "$case_repo" add fixture.txt
  git -C "$case_repo" commit -m initial >/dev/null
  git -C "$case_repo" push -u origin main >/dev/null 2>&1
  git -C "$case_repo" config 'url.file://'$case_bare'.insteadOf' 'https://github.com/bobapplemac/llm-glue.git'
  git -C "$case_repo" remote set-url origin 'https://github.com/bobapplemac/llm-glue.git'
}

run_check() { (cd "$case_repo" && FAKE_GH_RELEASE_EXISTS="${FAKE_GH_RELEASE_EXISTS:-0}" bash "$publisher" --revision 1 --dry-run --check-only --branch main); }
expect_pass() { local name="$1"; shift; "$@" >/dev/null 2>&1 || { echo "expected pass: $name" >&2; exit 1; }; }
expect_fail() { local name="$1"; shift; if "$@" >/dev/null 2>&1; then echo "expected fail: $name" >&2; exit 1; fi; }

new_case clean
expect_pass clean run_check

new_case dirty
printf 'dirty\n' > "$case_repo/untracked.txt"
expect_fail dirty run_check

new_case ahead
printf 'ahead\n' >> "$case_repo/fixture.txt"
git -C "$case_repo" commit -am ahead >/dev/null
expect_fail ahead run_check

new_case behind
git clone "$case_bare" "$case_root/other" >/dev/null 2>&1
git -C "$case_root/other" config user.name "LLM Glue Test"
git -C "$case_root/other" config user.email "test@example.invalid"
printf 'behind\n' >> "$case_root/other/fixture.txt"
git -C "$case_root/other" commit -am behind >/dev/null
git -C "$case_root/other" push origin main >/dev/null 2>&1
expect_fail behind run_check

new_case detached
git -C "$case_repo" checkout --detach >/dev/null 2>&1
expect_fail detached run_check

new_case tag_collision
git -C "$case_repo" tag r1
expect_fail tag_collision run_check

new_case release_collision
FAKE_GH_RELEASE_EXISTS=1 expect_fail release_collision run_check
unset FAKE_GH_RELEASE_EXISTS

new_case ambiguous_remote
git -C "$case_repo" remote add backup "$case_bare"
expect_fail ambiguous_remote run_check

new_case wrong_remote
git -C "$case_repo" remote set-url origin 'https://github.com/someone-else/llm-glue.git'
expect_fail wrong_remote run_check

new_case unavailable_remote
git -C "$case_repo" config --unset-all 'url.file://'$case_bare'.insteadOf'
git -C "$case_repo" config 'url.file:///definitely-missing-llm-glue-origin.insteadOf' 'https://github.com/bobapplemac/llm-glue.git'
expect_fail unavailable_remote run_check

new_case partial_release_failure
partial_output="$case_root/partial.log"
if (cd "$case_repo" && printf 'r1\n' | FAKE_GH_CREATE_FAIL=1 bash "$publisher" --revision 1 --branch main >"$partial_output" 2>&1); then
  echo "expected partial release failure" >&2
  exit 1
fi
grep -q 'partial publication: tag r1 was pushed but the GitHub release failed' "$partial_output" || { echo "partial failure guidance was missing" >&2; exit 1; }
git --git-dir="$case_bare" rev-parse --verify refs/tags/r1 >/dev/null || { echo "partial failure did not preserve the pushed local-test tag" >&2; exit 1; }

echo "Bash release preflight matrix: PASS"
