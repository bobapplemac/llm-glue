#!/usr/bin/env bash
set -euo pipefail

repo="bobapplemac/llm-glue"
install_dir="${LLM_GLUE_INSTALL_DIR:-$HOME/.local/lib/llm-glue}"
install_service="${LLM_GLUE_INSTALL_SERVICE:-0}"
mode="${LLM_GLUE_MODE:-remote-client}"
server_url="${LLM_GLUE_SERVER_URL:-https://replace-with-your-server}"
ollama_url="${LLM_GLUE_OLLAMA_URL:-http://127.0.0.1:11434}"
opencode_path="${LLM_GLUE_OPENCODE_PATH:-/etc/opencode/opencode.json}"
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT

if [[ "${LLM_GLUE_NONINTERACTIVE:-0}" != 1 && -r /dev/tty ]]; then
  printf 'Installation mode [remote-client/standalone-ollama] (%s): ' "$mode" > /dev/tty
  read -r answer < /dev/tty || true
  mode="${answer:-$mode}"
  if [[ "$mode" == remote-client ]]; then
    printf 'LLM Glue server URL (%s): ' "$server_url" > /dev/tty
    read -r answer < /dev/tty || true
    server_url="${answer:-$server_url}"
  else
    printf 'Ollama URL (%s): ' "$ollama_url" > /dev/tty
    read -r answer < /dev/tty || true
    ollama_url="${answer:-$ollama_url}"
  fi
  printf 'OpenCode system config path (%s): ' "$opencode_path" > /dev/tty
  read -r answer < /dev/tty || true
  opencode_path="${answer:-$opencode_path}"
  printf 'Register the systemd service? [y/N]: ' > /dev/tty
  read -r answer < /dev/tty || true
  [[ "$answer" =~ ^[Yy]$ ]] && install_service=1
fi
[[ "$mode" == remote-client || "$mode" == standalone-ollama ]] || { echo 'LLM_GLUE_MODE must be remote-client or standalone-ollama' >&2; exit 2; }

release_json="$(curl --fail --silent --show-error --location -H 'User-Agent: llm-glue-bootstrap' "https://api.github.com/repos/$repo/releases/latest")"
tag="$(printf '%s' "$release_json" | sed -n 's/.*"tag_name":[[:space:]]*"\(r[0-9]*\)".*/\1/p' | head -1)"
[[ "$tag" =~ ^r[1-9][0-9]*$ ]] || { echo 'latest release is not an official stable rN release' >&2; exit 1; }
asset="llm-glue-${tag}-linux-x64.tar.gz"
base="https://github.com/$repo/releases/download/$tag"
curl --fail --silent --show-error --location "$base/$asset" -o "$tmp/$asset"
curl --fail --silent --show-error --location "$base/SHA256SUMS" -o "$tmp/SHA256SUMS"
(cd "$tmp" && grep -E "[[:space:]]${asset}$" SHA256SUMS | sha256sum --check --strict -)
mkdir -p "$tmp/stage"
tar -C "$tmp/stage" -xzf "$tmp/$asset"
mkdir -p "$install_dir"
if [[ -f "$install_dir/llm-glue" ]]; then cp -p "$install_dir/llm-glue" "$install_dir/llm-glue.rollback"; fi
install -m 0755 "$tmp/stage/llm-glue" "$install_dir/llm-glue"
config="$install_dir/llm-glue.conf"
if [[ ! -e "$config" ]]; then
  install -m 0600 /dev/null "$config"
  if [[ "$mode" == remote-client ]]; then
    printf '%s\n' '[roles]' 'agent = false' 'server = false' 'client = true' '' '[client]' "source = \"$server_url\"" 'token_environment = "LLM_GLUE_READ_TOKEN"' '' '[[client.targets]]' 'type = "opencode"' "path = \"$opencode_path\"" > "$config"
  else
    printf '%s\n' '[roles]' 'agent = true' 'server = true' 'client = true' '' '[[providers]]' 'name = "local-ollama"' 'type = "ollama"' "url = \"$ollama_url\"" '' '[client]' 'poll_interval_seconds = 30' '' '[[client.targets]]' 'type = "opencode"' "path = \"$opencode_path\"" > "$config"
  fi
  echo "created $config; validate it before starting LLM Glue" >&2
fi
"$install_dir/llm-glue" --version
"$install_dir/llm-glue" config validate --config "$config"
if [[ "$install_service" == 1 ]]; then sudo -- "$install_dir/llm-glue" service install --config "$config"; fi
echo "installed $tag to $install_dir"
