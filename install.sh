#!/usr/bin/env bash
set -euo pipefail

repo="bobapplemac/llm-glue"
install_service="${LLM_GLUE_INSTALL_SERVICE:-0}"
mode="${LLM_GLUE_MODE:-remote-client}"
server_url="${LLM_GLUE_SERVER_URL:-https://replace-with-your-server}"
server_token="${LLM_GLUE_SERVER_TOKEN:-}"
read_token="${LLM_GLUE_READ_TOKEN:-}"
write_token="${LLM_GLUE_WRITE_TOKEN:-}"
listen_url="${LLM_GLUE_LISTEN_URL:-http://0.0.0.0:8765}"
ollama_url="${LLM_GLUE_OLLAMA_URL:-http://127.0.0.1:11434}"
llama_url="${LLM_GLUE_LLAMA_URL:-http://127.0.0.1:8081}"
model_directory="${LLM_GLUE_MODEL_DIRECTORY:-/mnt/models/gguf}"
opencode_path="${LLM_GLUE_OPENCODE_PATH:-/etc/opencode/opencode.json}"
litellm_generated="${LLM_GLUE_LITELLM_GENERATED:-/opt/litellm/llm-glue-models.yaml}"
litellm_primary="${LLM_GLUE_LITELLM_PRIMARY:-/opt/litellm/config.yaml}"
litellm_service="${LLM_GLUE_LITELLM_SERVICE:-litellm.service}"
litellm_url="${LLM_GLUE_LITELLM_URL:-http://replace-with-your-litellm:4000}"
litellm_health="${LLM_GLUE_LITELLM_HEALTH:-http://127.0.0.1:4000/health/liveliness}"
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT

prompt() {
  local label="$1" current="$2" answer
  printf '%s (%s): ' "$label" "$current" > /dev/tty
  read -r answer < /dev/tty || true
  printf '%s' "${answer:-$current}"
}

prompt_secret() {
  local label="$1" answer
  printf '%s: ' "$label" > /dev/tty
  IFS= read -r -s answer < /dev/tty || true
  printf '\n' > /dev/tty
  printf '%s' "$answer"
}

toml_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '%s' "$value"
}

if [[ "${LLM_GLUE_NONINTERACTIVE:-0}" != 1 && -r /dev/tty ]]; then
  mode="$(prompt 'Installation mode [remote-client/standalone-ollama/central-llama]' "$mode")"
  case "$mode" in
    remote-client)
      server_url="$(prompt 'LLM Glue server URL' "$server_url")"
      server_token="$(prompt_secret 'Server read token (leave blank to use LLM_GLUE_READ_TOKEN at runtime)')"
      opencode_path="$(prompt 'OpenCode system config path' "$opencode_path")"
      ;;
    standalone-ollama)
      ollama_url="$(prompt 'Ollama URL' "$ollama_url")"
      opencode_path="$(prompt 'OpenCode system config path' "$opencode_path")"
      ;;
    central-llama)
      llama_url="$(prompt 'llama.cpp URL' "$llama_url")"
      model_directory="$(prompt 'GGUF model directory' "$model_directory")"
      listen_url="$(prompt 'LLM Glue listen URL' "$listen_url")"
      read_token="$(prompt_secret 'Remote client read token')"
      write_token="$(prompt_secret 'Remote agent write token')"
      litellm_primary="$(prompt 'LiteLLM primary config path' "$litellm_primary")"
      litellm_generated="$(prompt 'LLM Glue LiteLLM include path' "$litellm_generated")"
      litellm_service="$(prompt 'LiteLLM systemd service' "$litellm_service")"
      litellm_url="$(prompt 'Externally reachable LiteLLM URL for OpenCode clients' "$litellm_url")"
      litellm_health="$(prompt 'LiteLLM health URL' "$litellm_health")"
      ;;
    *) echo 'invalid installation mode' >&2; exit 2 ;;
  esac
  printf 'Register the systemd service? [y/N]: ' > /dev/tty
  read -r answer < /dev/tty || true
  [[ "$answer" =~ ^[Yy]$ ]] && install_service=1
fi

[[ "$mode" == remote-client || "$mode" == standalone-ollama || "$mode" == central-llama ]] || { echo 'LLM_GLUE_MODE must be remote-client, standalone-ollama, or central-llama' >&2; exit 2; }
if [[ "${LLM_GLUE_NONINTERACTIVE:-0}" == 1 && "$mode" == remote-client && "$server_url" == https://replace-with-your-server ]]; then
  echo 'Noninteractive remote-client installation requires LLM_GLUE_SERVER_URL.' >&2
  exit 2
fi
if [[ "$install_service" == 1 && "$mode" == remote-client && -z "$server_token" ]]; then
  echo 'Service-mode remote-client requires LLM_GLUE_SERVER_TOKEN (or the interactive secret prompt); systemd does not inherit the installer environment.' >&2
  exit 2
fi
if [[ "$mode" == central-llama && ( -z "$read_token" || -z "$write_token" ) ]]; then
  echo 'central-llama requires LLM_GLUE_READ_TOKEN and LLM_GLUE_WRITE_TOKEN values (or interactive secret prompts).' >&2
  exit 2
fi
if [[ "$mode" == central-llama && "$litellm_url" == http://replace-with-your-litellm:4000 ]]; then
  echo 'central-llama requires LLM_GLUE_LITELLM_URL reachable by remote OpenCode clients.' >&2
  exit 2
fi

if [[ -n "${LLM_GLUE_INSTALL_DIR:-}" ]]; then install_dir="$LLM_GLUE_INSTALL_DIR"; elif [[ "$install_service" == 1 ]]; then install_dir="/opt/llm-glue"; else install_dir="$HOME/.local/lib/llm-glue"; fi
if [[ -n "${LLM_GLUE_CONFIG_PATH:-}" ]]; then config="$LLM_GLUE_CONFIG_PATH"; elif [[ "$install_service" == 1 ]]; then config="/etc/llm-glue.conf"; else config="$install_dir/llm-glue.conf"; fi

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
staged="$tmp/stage/llm-glue"
chmod 0755 "$staged"
"$staged" --version

candidate="$tmp/llm-glue.conf"
if [[ "$mode" == remote-client ]]; then
  {
    printf '%s\n' '[roles]' 'agent = false' 'server = false' 'client = true' '' '[client]' "source = \"$(toml_escape "$server_url")\""
    if [[ -n "$server_token" ]]; then printf 'token = "%s"\n' "$(toml_escape "$server_token")"; else printf '%s\n' 'token_environment = "LLM_GLUE_READ_TOKEN"'; fi
    printf '%s\n' '' '[[client.targets]]' 'type = "opencode"' "path = \"$(toml_escape "$opencode_path")\""
  } > "$candidate"
elif [[ "$mode" == standalone-ollama ]]; then
  state_path="state"; log_path="logs/llm-glue.log"
  if [[ "$install_service" == 1 ]]; then state_path="/var/lib/llm-glue"; log_path="/var/log/llm-glue/llm-glue.log"; fi
  printf '%s\n' '[roles]' 'agent = true' 'server = true' 'client = true' '' '[[agent.providers]]' 'name = "local-ollama"' 'type = "ollama"' "url = \"$(toml_escape "$ollama_url")\"" '' '[server]' 'listen = "http://127.0.0.1:8765"' "state_directory = \"$state_path\"" '' '[client]' 'source = "http://127.0.0.1:8765"' '' '[[client.targets]]' 'type = "opencode"' "path = \"$(toml_escape "$opencode_path")\"" '' '[logging]' "file = \"$log_path\"" > "$candidate"
else
  [[ "$listen_url" =~ :([0-9]+)(/.*)?$ ]] || { echo 'LLM_GLUE_LISTEN_URL must include an explicit port' >&2; exit 2; }
  local_source="http://127.0.0.1:${BASH_REMATCH[1]}"
  printf '%s\n' '[roles]' 'agent = true' 'server = true' 'client = true' '' '[[agent.providers]]' 'name = "local-llama"' 'type = "llama.cpp"' "url = \"$(toml_escape "$llama_url")\"" "model_directories = [\"$(toml_escape "$model_directory")\"]" 'inspect_gguf = true' 'hash_model_files = false' 'probe_mode = "loaded-only"' '' '[server]' "listen = \"$(toml_escape "$listen_url")\"" "opencode_inference_url = \"$(toml_escape "$litellm_url")\"" "read_token = \"$(toml_escape "$read_token")\"" "write_token = \"$(toml_escape "$write_token")\"" 'acknowledge_insecure_remote_http = true' 'state_directory = "/var/lib/llm-glue"' '' '[client]' "source = \"$local_source\"" "token = \"$(toml_escape "$read_token")\"" '' '[[client.targets]]' 'type = "litellm"' "path = \"$(toml_escape "$litellm_generated")\"" "primary_config_path = \"$(toml_escape "$litellm_primary")\"" "service_name = \"$(toml_escape "$litellm_service")\"" "health_url = \"$(toml_escape "$litellm_health")\"" '' '[logging]' 'file = "/var/log/llm-glue/llm-glue.log"' > "$candidate"
fi
chmod 0600 "$candidate"
"$staged" config validate --config "$candidate"

if [[ -e "$config" ]]; then
  if [[ -r "$config" ]]; then "$staged" config validate --config "$config"; else sudo -- "$staged" config validate --config "$config"; fi
  echo "preserving existing configuration $config" >&2
fi

if [[ "$install_service" == 1 ]]; then
  sudo -- install -d -m 0755 "$install_dir"
  if sudo -- test -f "$install_dir/llm-glue"; then sudo -- cp -p -- "$install_dir/llm-glue" "$install_dir/llm-glue.rollback"; fi
  sudo -- install -m 0755 "$staged" "$install_dir/llm-glue"
  if ! sudo -- test -e "$config"; then sudo -- install -m 0600 "$candidate" "$config"; fi
  sudo -- "$install_dir/llm-glue" service install --config "$config"
else
  mkdir -p "$install_dir"
  if [[ -f "$install_dir/llm-glue" ]]; then cp -p -- "$install_dir/llm-glue" "$install_dir/llm-glue.rollback"; fi
  install -m 0755 "$staged" "$install_dir/llm-glue"
  if [[ ! -e "$config" ]]; then install -m 0600 "$candidate" "$config"; fi
fi
echo "installed $tag to $install_dir with config $config"
