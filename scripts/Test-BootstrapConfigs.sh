#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
binary="${LLM_GLUE_TEST_BINARY:-$root/artifacts/service-check/linux-x64/llm-glue}"
[[ -x "$binary" ]] || { echo "publish a Linux test binary first: $binary" >&2; exit 2; }
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
mkdir -p "$tmp/release/stage" "$tmp/bin"
cp -- "$binary" "$tmp/release/stage/llm-glue"
tar -C "$tmp/release/stage" -czf "$tmp/release/llm-glue-r1-linux-x64.tar.gz" llm-glue
(cd "$tmp/release" && sha256sum llm-glue-r1-linux-x64.tar.gz > SHA256SUMS)

printf '%s\n' '#!/usr/bin/env bash' 'set -euo pipefail' 'url="${!#}"' 'out=""' 'for ((i=1; i<=$#; i++)); do if [[ "${!i}" == -o ]]; then j=$((i+1)); out="${!j}"; fi; done' 'case "$url" in' '  */releases/latest) printf '\''%s\n'\'' '\''{"tag_name":"r1"}'\'' ;;' '  */SHA256SUMS) cp -- "$MOCK_RELEASE/SHA256SUMS" "$out" ;;' '  */llm-glue-r1-linux-x64.tar.gz) cp -- "$MOCK_RELEASE/llm-glue-r1-linux-x64.tar.gz" "$out" ;;' '  *) echo "unexpected URL: $url" >&2; exit 2 ;;' 'esac' > "$tmp/bin/curl"
chmod +x "$tmp/bin/curl"
export PATH="$tmp/bin:$PATH" MOCK_RELEASE="$tmp/release" LLM_GLUE_NONINTERACTIVE=1

run_mode() {
  local mode="$1" destination="$tmp/$1"
  shift
  env LLM_GLUE_MODE="$mode" LLM_GLUE_INSTALL_DIR="$destination" "$@" "$root/install.sh"
  "$destination/llm-glue" config validate --config "$destination/llm-glue.conf"
  [[ "$(stat -c '%a' "$destination/llm-glue.conf")" == 600 ]]
}

run_mode remote-client env LLM_GLUE_SERVER_URL=https://catalog.example LLM_GLUE_SERVER_TOKEN=test-read LLM_GLUE_OPENCODE_PATH="$tmp/opencode.json"
grep -Fq 'token = "test-read"' "$tmp/remote-client/llm-glue.conf"
remote_hash="$(sha256sum "$tmp/remote-client/llm-glue.conf")"
run_mode remote-client env LLM_GLUE_SERVER_URL=https://changed.example LLM_GLUE_SERVER_TOKEN=changed LLM_GLUE_OPENCODE_PATH="$tmp/changed.json"
[[ "$(sha256sum "$tmp/remote-client/llm-glue.conf")" == "$remote_hash" ]]
[[ -x "$tmp/remote-client/llm-glue.rollback" ]]

run_mode standalone-ollama env LLM_GLUE_OLLAMA_URL=http://127.0.0.1:11434 LLM_GLUE_OPENCODE_PATH="$tmp/opencode.json"
grep -Fq '[[agent.providers]]' "$tmp/standalone-ollama/llm-glue.conf"

run_mode central-llama env LLM_GLUE_READ_TOKEN=test-read LLM_GLUE_WRITE_TOKEN=test-write LLM_GLUE_LISTEN_URL=http://0.0.0.0:9876 LLM_GLUE_MODEL_DIRECTORY="$tmp/models" LLM_GLUE_LITELLM_GENERATED="$tmp/generated.yaml" LLM_GLUE_LITELLM_PRIMARY="$tmp/litellm.yaml"
grep -Fq 'source = "http://127.0.0.1:9876"' "$tmp/central-llama/llm-glue.conf"
grep -Fq 'type = "litellm"' "$tmp/central-llama/llm-glue.conf"

echo 'Bash bootstrap configuration matrix passed.'
