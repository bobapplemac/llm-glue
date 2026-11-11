#!/usr/bin/env bash
set -euo pipefail

out="${1:-artifacts}"
revision="${2:-0}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
stage="$root/$out/packages"
mkdir -p "$stage"

if [[ ! "$revision" =~ ^[0-9]+$ ]]; then echo "revision must be an integer" >&2; exit 2; fi

for rid in linux-x64 win-x64; do
  source_dir="$root/$out/publish/$rid"
  [[ -d "$source_dir" ]] || { echo "missing publish output: $source_dir" >&2; exit 1; }
  if [[ "$rid" == linux-x64 ]]; then
    archive="$stage/llm-glue-r${revision}-${rid}.tar.gz"
    tar -C "$source_dir" -czf "$archive" .
  else
    archive="$stage/llm-glue-r${revision}-${rid}.zip"
    (cd "$source_dir" && zip -qr "$archive" .)
  fi
done

(cd "$stage" && sha256sum llm-glue-r${revision}-* > SHA256SUMS)
commit="$(git -C "$root" rev-parse HEAD)"
major=0
assembly="0.0.0.0"
file="0.0.0.0"
if ((revision > 0)); then major=1; assembly="1.0.0.0"; file="1.${revision}.0.0"; fi
printf '{"product":"LLM Glue","revision":"r%s","assemblyVersion":"%s","fileVersion":"%s","commit":"%s","assets":["linux-x64","win-x64"]}\n' "$revision" "$assembly" "$file" "$commit" > "$stage/manifest.json"
