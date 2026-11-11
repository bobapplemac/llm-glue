#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
binary="${LLM_GLUE_TEST_BINARY:-$root/artifacts/bootstrap-test/linux-x64/llm-glue}"
[[ -x "$binary" ]] || { echo "missing executable test binary: $binary" >&2; exit 1; }

scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
mkdir -p "$scratch/bin"
cp -- "$binary" "$scratch/activator"
cp -- "$binary" "$scratch/source"
cp -- /bin/false "$scratch/destination"
cp -- /bin/false "$scratch/fallback"
chmod 0755 "$scratch/activator" "$scratch/source" "$scratch/destination" "$scratch/fallback"

sleep 1 &
parent_pid=$!
"$scratch/activator" update activate-staged \
  --wait-process-id "$parent_pid" \
  --source "$scratch/source" \
  --destination "$scratch/destination" \
  --fallback "$scratch/fallback" \
  --health-url '' \
  --result "$scratch/result.log"

[[ "$(tr -d '\r\n' < "$scratch/result.log")" == success ]]
[[ -x "$scratch/destination" ]]
"$scratch/destination" --version | grep -Eq '^LLM Glue r[0-9]+$'

printf '#!/bin/sh\nexit 0\n' > "$scratch/bin/systemctl"
chmod 0755 "$scratch/bin/systemctl"
PATH="$scratch/bin:$PATH" "$scratch/destination" service status \
  --config "$root/examples/remote-opencode-client.conf"

echo 'Linux single-file staged update activation: PASS'
