#!/usr/bin/env bash
set -euo pipefail

repo="bobapplemac/llm-glue"
install_service="${LLM_GLUE_INSTALL_SERVICE:-0}"
mode="${LLM_GLUE_MODE:-remote-client}"
roles="${LLM_GLUE_ROLES:-}"
agent_providers="${LLM_GLUE_AGENT_PROVIDERS:-${LLM_GLUE_AGENT_PROVIDER:-}}"
client_targets="${LLM_GLUE_CLIENT_TARGETS:-${LLM_GLUE_CLIENT_TARGET:-}}"
server_url="${LLM_GLUE_SERVER_URL:-https://replace-with-your-server:8765}"
server_token="${LLM_GLUE_SERVER_TOKEN:-}"
inference_api_key="${LLM_GLUE_INFERENCE_API_KEY:-}"
embed_inference_api_key="${LLM_GLUE_EMBED_INFERENCE_API_KEY:-1}"
read_token="${LLM_GLUE_READ_TOKEN:-}"
write_token="${LLM_GLUE_WRITE_TOKEN:-}"
listen_url="${LLM_GLUE_LISTEN_URL:-https://0.0.0.0:8765}"
generate_api_keys="${LLM_GLUE_GENERATE_API_KEYS:-0}"
allow_invalid_server_certificate="${LLM_GLUE_ALLOW_INVALID_SERVER_CERTIFICATE:-1}"
trusted_server_public_key_sha256="${LLM_GLUE_TRUSTED_SERVER_PUBLIC_KEY_SHA256:-}"
tls_server_names="${LLM_GLUE_TLS_SERVER_NAMES:-localhost,$(hostname -f 2>/dev/null || hostname)}"
ollama_url="${LLM_GLUE_OLLAMA_URL:-http://127.0.0.1:11434}"
llama_url="${LLM_GLUE_LLAMA_URL:-http://127.0.0.1:8081}"
model_directory="${LLM_GLUE_MODEL_DIRECTORY:-/mnt/models/gguf}"
opencode_path="${LLM_GLUE_OPENCODE_PATH:-/etc/opencode/opencode.jsonc}"
litellm_generated="${LLM_GLUE_LITELLM_GENERATED:-/opt/litellm/llm-glue-models.yaml}"
litellm_primary="${LLM_GLUE_LITELLM_PRIMARY:-/opt/litellm/config.yaml}"
litellm_service="${LLM_GLUE_LITELLM_SERVICE:-litellm.service}"
litellm_url="${LLM_GLUE_LITELLM_URL:-http://replace-with-your-litellm:4000}"
litellm_health="${LLM_GLUE_LITELLM_HEALTH:-http://127.0.0.1:4000/health/liveliness}"
tmp="$(mktemp -d)"
service_was_active=0
prior_binary=0
binary_replaced=0
installed_binary=''

cleanup() {
  local status=$?
  if [[ $status -ne 0 && "$install_service" == 1 ]]; then
    if [[ $binary_replaced == 1 && $prior_binary == 1 && -n "$installed_binary" ]]; then
      sudo -- install -m 0755 "$installed_binary.rollback" "$installed_binary" || true
    fi
    if [[ $service_was_active == 1 ]]; then sudo -- systemctl start llm-glue.service || true; fi
  fi
  rm -rf -- "$tmp"
}
trap cleanup EXIT

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

has_role() {
  [[ ",$roles," == *",$1,"* ]]
}

has_csv_value() {
  [[ ",$1," == *",$2,"* ]]
}

detect_agent_providers() {
  local detected=''
  if command -v ollama >/dev/null 2>&1 || curl --silent --fail --max-time 1 http://127.0.0.1:11434/api/version >/dev/null 2>&1; then detected=ollama; fi
  if [[ -f /opt/llama-cpp/router/models.ini ]] || curl --silent --fail --max-time 1 http://127.0.0.1:8081/health >/dev/null 2>&1; then
    [[ -z "$detected" ]] || detected+=,
    detected+=llama.cpp
  fi
  printf '%s' "${detected:-ollama}"
}

detect_client_targets() {
  local detected=''
  if command -v opencode >/dev/null 2>&1 || [[ -d /etc/opencode ]]; then detected=opencode; fi
  if [[ -f /opt/litellm/config.yaml ]] || systemctl list-unit-files 'litellm*.service' --no-legend 2>/dev/null | grep -q .; then
    [[ -z "$detected" ]] || detected+=,
    detected+=litellm
  fi
  printf '%s' "${detected:-opencode}"
}

toml_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '%s' "$value"
}

random_hex_24() {
  if command -v openssl >/dev/null 2>&1; then openssl rand -hex 24; else od -An -N24 -tx1 /dev/urandom | tr -d ' \n'; fi
}

toml_string_array() {
  local value="$1" item first=1
  printf '['
  IFS=',' read -ra items <<< "$value"
  for item in "${items[@]}"; do
    item="${item#${item%%[![:space:]]*}}"; item="${item%${item##*[![:space:]]}}"
    [[ -n "$item" ]] || continue
    ((first)) || printf ', '
    printf '"%s"' "$(toml_escape "$item")"
    first=0
  done
  printf ']'
}

if [[ "${LLM_GLUE_NONINTERACTIVE:-0}" != 1 && -r /dev/tty ]]; then
  mode="$(prompt 'Installation profile [remote-client/standalone-ollama/central-llama/custom]' "$mode")"
  case "$mode" in
    remote-client)
      server_url="$(prompt 'LLM Glue server URL' "$server_url")"
      server_token="$(prompt_secret 'Server read token (leave blank to use LLM_GLUE_READ_TOKEN at runtime)')"
      trusted_server_public_key_sha256="$(prompt 'Pinned Server public-key SHA-256 (blank accepts any self-signed certificate)' "$trusted_server_public_key_sha256")"
      [[ -z "$trusted_server_public_key_sha256" ]] || allow_invalid_server_certificate=0
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
      tls_server_names="$(prompt 'TLS/SNI server names, comma separated' "$tls_server_names")"
      printf 'Auto-generate 24-byte read/write API keys? [Y/n]: ' > /dev/tty
      read -r answer < /dev/tty || true
      [[ "$answer" =~ ^[Nn]$ ]] || generate_api_keys=1
      if [[ "$generate_api_keys" != 1 ]]; then
        read_token="$(prompt_secret 'Remote client read token')"
        write_token="$(prompt_secret 'Remote agent write token')"
      fi
      litellm_primary="$(prompt 'LiteLLM primary config path' "$litellm_primary")"
      litellm_generated="$(prompt 'LLM Glue LiteLLM include path' "$litellm_generated")"
      litellm_service="$(prompt 'LiteLLM systemd service' "$litellm_service")"
      litellm_url="$(prompt 'Externally reachable LiteLLM URL for OpenCode clients' "$litellm_url")"
      if [[ "$embed_inference_api_key" == 1 ]]; then inference_api_key="$(prompt_secret 'LiteLLM API key to embed in authenticated OpenCode artifacts')"; fi
      litellm_health="$(prompt 'LiteLLM health URL' "$litellm_health")"
      ;;
    custom)
      roles="$(prompt 'Enabled roles, comma separated [agent/server/client]' "${roles:-agent,server,client}")"
      roles="${roles//[[:space:]]/}"
      if has_role agent; then
        [[ -n "$agent_providers" ]] || agent_providers="$(detect_agent_providers)"
        agent_providers="$(prompt 'Agent providers, comma separated [ollama/llama.cpp]' "$agent_providers")"
        agent_providers="${agent_providers//[[:space:]]/}"
        if has_csv_value "$agent_providers" ollama; then
          ollama_url="$(prompt 'Ollama URL' "$ollama_url")"
        fi
        if has_csv_value "$agent_providers" llama.cpp; then
          llama_url="$(prompt 'llama.cpp URL' "$llama_url")"
          model_directory="$(prompt 'GGUF model directory' "$model_directory")"
        fi
      fi
      if ! has_role server && { has_role agent || has_role client; }; then
        server_url="$(prompt 'Remote LLM Glue server URL' "$server_url")"
        if has_role agent; then write_token="$(prompt_secret 'Remote Server write token')"; fi
        if has_role client; then server_token="$(prompt_secret 'Remote Server read token')"; fi
        trusted_server_public_key_sha256="$(prompt 'Pinned Server public-key SHA-256 (blank accepts any self-signed certificate)' "$trusted_server_public_key_sha256")"
        [[ -z "$trusted_server_public_key_sha256" ]] || allow_invalid_server_certificate=0
      fi
      if has_role server; then
        listen_url="$(prompt 'LLM Glue listen URL' "$listen_url")"
        tls_server_names="$(prompt 'TLS/SNI server names, comma separated' "$tls_server_names")"
        printf 'Auto-generate 24-byte read/write API keys? [Y/n]: ' > /dev/tty
        read -r answer < /dev/tty || true
        [[ "$answer" =~ ^[Nn]$ ]] || generate_api_keys=1
        if [[ "$generate_api_keys" != 1 ]]; then
          read_token="$(prompt_secret 'Remote client read token')"
          write_token="$(prompt_secret 'Remote Agent write token')"
        fi
        litellm_url="$(prompt 'Externally reachable LiteLLM URL for generated OpenCode routes' "$litellm_url")"
        if [[ "$embed_inference_api_key" == 1 ]]; then inference_api_key="$(prompt_secret 'LiteLLM API key to embed in authenticated OpenCode artifacts')"; fi
      fi
      if has_role client; then
        [[ -n "$client_targets" ]] || client_targets="$(detect_client_targets)"
        client_targets="$(prompt 'Client targets, comma separated [opencode/litellm]' "$client_targets")"
        client_targets="${client_targets//[[:space:]]/}"
        if has_csv_value "$client_targets" opencode; then
          opencode_path="$(prompt 'OpenCode system config path' "$opencode_path")"
        fi
        if has_csv_value "$client_targets" litellm; then
          litellm_primary="$(prompt 'LiteLLM primary config path' "$litellm_primary")"
          litellm_generated="$(prompt 'LLM Glue LiteLLM include path' "$litellm_generated")"
          litellm_service="$(prompt 'LiteLLM systemd service' "$litellm_service")"
          litellm_health="$(prompt 'LiteLLM health URL' "$litellm_health")"
        fi
      fi
      ;;
    *) echo 'invalid installation mode' >&2; exit 2 ;;
  esac
  printf 'Register the systemd service? [y/N]: ' > /dev/tty
  read -r answer < /dev/tty || true
  [[ "$answer" =~ ^[Yy]$ ]] && install_service=1
fi

if [[ "$mode" == remote-client ]]; then roles=client; fi
if [[ "$mode" == standalone-ollama || "$mode" == central-llama ]]; then roles=agent,server,client; fi
roles="${roles//[[:space:]]/}"
[[ -n "$roles" ]] || { echo 'at least one role must be enabled' >&2; exit 2; }
IFS=',' read -ra selected_roles <<< "$roles"
for selected_role in "${selected_roles[@]}"; do
  [[ "$selected_role" == agent || "$selected_role" == server || "$selected_role" == client ]] || { echo "invalid role: $selected_role" >&2; exit 2; }
done
for required_role in agent server client; do
  count=0
  for selected_role in "${selected_roles[@]}"; do [[ "$selected_role" == "$required_role" ]] && count=$((count+1)); done
  ((count <= 1)) || { echo "duplicate role: $required_role" >&2; exit 2; }
done

if { [[ "$mode" == central-llama ]] || [[ "$mode" == custom ]] && has_role server; } && [[ "$generate_api_keys" == 1 ]]; then
  [[ -n "$read_token" ]] || read_token="$(random_hex_24)"
  [[ -n "$write_token" ]] || write_token="$(random_hex_24)"
fi

[[ "$mode" == remote-client || "$mode" == standalone-ollama || "$mode" == central-llama || "$mode" == custom ]] || { echo 'LLM_GLUE_MODE must be remote-client, standalone-ollama, central-llama, or custom' >&2; exit 2; }
if [[ "${LLM_GLUE_NONINTERACTIVE:-0}" == 1 && "$mode" == remote-client && "$server_url" == https://replace-with-your-server:8765 ]]; then
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
[[ "$embed_inference_api_key" == 0 || "$embed_inference_api_key" == 1 ]] || { echo 'LLM_GLUE_EMBED_INFERENCE_API_KEY must be 0 or 1.' >&2; exit 2; }
[[ "$generate_api_keys" == 0 || "$generate_api_keys" == 1 ]] || { echo 'LLM_GLUE_GENERATE_API_KEYS must be 0 or 1.' >&2; exit 2; }
[[ "$allow_invalid_server_certificate" == 0 || "$allow_invalid_server_certificate" == 1 ]] || { echo 'LLM_GLUE_ALLOW_INVALID_SERVER_CERTIFICATE must be 0 or 1.' >&2; exit 2; }
if [[ "$mode" == central-llama && "$embed_inference_api_key" == 1 && -z "$inference_api_key" ]]; then
  echo 'central-llama requires LLM_GLUE_INFERENCE_API_KEY by default. Set LLM_GLUE_EMBED_INFERENCE_API_KEY=0 only when every OpenCode process receives that environment variable separately.' >&2
  exit 2
fi
if [[ "$mode" == custom ]]; then
  if has_role agent; then
    agent_providers="${agent_providers//[[:space:]]/}"
    [[ -n "$agent_providers" ]] || { echo 'custom Agent requires LLM_GLUE_AGENT_PROVIDERS.' >&2; exit 2; }
    IFS=',' read -ra selected_providers <<< "$agent_providers"
    for agent_provider in "${selected_providers[@]}"; do [[ "$agent_provider" == ollama || "$agent_provider" == llama.cpp ]] || { echo "invalid Agent provider: $agent_provider" >&2; exit 2; }; done
    for supported_provider in ollama llama.cpp; do
      count=0
      for agent_provider in "${selected_providers[@]}"; do [[ "$agent_provider" == "$supported_provider" ]] && count=$((count+1)); done
      ((count <= 1)) || { echo "duplicate Agent provider: $supported_provider" >&2; exit 2; }
    done
  fi
  if has_role client; then
    client_targets="${client_targets//[[:space:]]/}"
    [[ -n "$client_targets" ]] || { echo 'custom Client requires LLM_GLUE_CLIENT_TARGETS.' >&2; exit 2; }
    IFS=',' read -ra selected_targets <<< "$client_targets"
    for client_target in "${selected_targets[@]}"; do [[ "$client_target" == opencode || "$client_target" == litellm ]] || { echo "invalid Client target: $client_target" >&2; exit 2; }; done
    for supported_target in opencode litellm; do
      count=0
      for client_target in "${selected_targets[@]}"; do [[ "$client_target" == "$supported_target" ]] && count=$((count+1)); done
      ((count <= 1)) || { echo "duplicate Client target: $supported_target" >&2; exit 2; }
    done
  fi
  if ! has_role server && { has_role agent || has_role client; } && [[ "$server_url" == https://replace-with-your-server:8765 ]]; then
    echo 'custom remote Agent/Client roles require LLM_GLUE_SERVER_URL.' >&2; exit 2
  fi
  if ! has_role server && has_role agent && [[ -z "$write_token" ]]; then echo 'custom remote Agent requires LLM_GLUE_WRITE_TOKEN.' >&2; exit 2; fi
  if ! has_role server && has_role client && [[ -z "$server_token" ]]; then echo 'custom remote Client requires LLM_GLUE_SERVER_TOKEN.' >&2; exit 2; fi
  if has_role server && [[ -z "$read_token" || -z "$write_token" ]]; then echo 'custom Server requires read/write tokens or LLM_GLUE_GENERATE_API_KEYS=1.' >&2; exit 2; fi
  if has_role server && [[ "$litellm_url" == http://replace-with-your-litellm:4000 ]]; then echo 'custom Server requires LLM_GLUE_LITELLM_URL.' >&2; exit 2; fi
  if has_role server && [[ "$embed_inference_api_key" == 1 && -z "$inference_api_key" ]]; then echo 'custom Server requires LLM_GLUE_INFERENCE_API_KEY by default.' >&2; exit 2; fi
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
tr -d '\r' < "$tmp/SHA256SUMS" > "$tmp/SHA256SUMS.normalized"
(cd "$tmp" && grep -E "[[:space:]]${asset}$" SHA256SUMS.normalized | sha256sum --check --strict -)
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
    if [[ -n "$trusted_server_public_key_sha256" ]]; then printf 'trusted_server_public_key_sha256 = "%s"\n' "$(toml_escape "$trusted_server_public_key_sha256")"; else printf 'allow_invalid_server_certificate = %s\n' "$([[ "$allow_invalid_server_certificate" == 1 ]] && echo true || echo false)"; fi
    printf '%s\n' '' '[[client.targets]]' 'type = "opencode"' "path = \"$(toml_escape "$opencode_path")\""
  } > "$candidate"
elif [[ "$mode" == standalone-ollama ]]; then
  state_path="state"; log_path="logs/llm-glue.log"
  if [[ "$install_service" == 1 ]]; then state_path="/var/lib/llm-glue"; log_path="/var/log/llm-glue/llm-glue.log"; fi
  printf '%s\n' '[roles]' 'agent = true' 'server = true' 'client = true' '' '[[agent.providers]]' 'name = "local-ollama"' 'type = "ollama"' "url = \"$(toml_escape "$ollama_url")\"" '' '[server]' 'listen = "https://127.0.0.1:8765"' 'tls_auto_generate = true' 'tls_server_names = ["localhost"]' "state_directory = \"$state_path\"" '' '[client]' '' '[[client.targets]]' 'type = "opencode"' "path = \"$(toml_escape "$opencode_path")\"" '' '[logging]' "file = \"$log_path\"" > "$candidate"
elif [[ "$mode" == central-llama ]]; then
  [[ "$listen_url" =~ :([0-9]+)(/.*)?$ ]] || { echo 'LLM_GLUE_LISTEN_URL must include an explicit port' >&2; exit 2; }
  {
    printf '%s\n' '[roles]' 'agent = true' 'server = true' 'client = true' '' '[[agent.providers]]' 'name = "local-llama"' 'type = "llama.cpp"' "url = \"$(toml_escape "$llama_url")\"" "model_directories = [\"$(toml_escape "$model_directory")\"]" 'inspect_gguf = true' 'hash_model_files = false' 'probe_mode = "loaded-only"' '' '[server]' "listen = \"$(toml_escape "$listen_url")\"" "opencode_inference_url = \"$(toml_escape "$litellm_url")\""
    if [[ "$embed_inference_api_key" == 1 ]]; then printf 'embed_opencode_inference_api_key = true\nopencode_inference_api_key = "%s"\n' "$(toml_escape "$inference_api_key")"; else printf '%s\n' 'embed_opencode_inference_api_key = false'; fi
    printf '%s\n' 'tls_auto_generate = true'
    printf 'tls_server_names = '; toml_string_array "$tls_server_names"; printf '\n'
    printf '%s\n' "read_token = \"$(toml_escape "$read_token")\"" "write_token = \"$(toml_escape "$write_token")\"" 'state_directory = "/var/lib/llm-glue"' '' '[client]' "token = \"$(toml_escape "$read_token")\"" '' '[[client.targets]]' 'type = "litellm"' "path = \"$(toml_escape "$litellm_generated")\"" "primary_config_path = \"$(toml_escape "$litellm_primary")\"" "service_name = \"$(toml_escape "$litellm_service")\"" "health_url = \"$(toml_escape "$litellm_health")\"" '' '[logging]' 'file = "/var/log/llm-glue/llm-glue.log"'
  } > "$candidate"
else
  agent_enabled=false; server_enabled=false; client_enabled=false
  has_role agent && agent_enabled=true
  has_role server && server_enabled=true
  has_role client && client_enabled=true
  state_path="state"; log_path="logs/llm-glue.log"
  if [[ "$install_service" == 1 ]]; then state_path="/var/lib/llm-glue"; log_path="/var/log/llm-glue/llm-glue.log"; fi
  {
    printf '%s\n' '[roles]' "agent = $agent_enabled" "server = $server_enabled" "client = $client_enabled"
    if has_role agent; then
      if ! has_role server; then
        printf '%s\n' '' '[agent]' "submit_to = \"$(toml_escape "$server_url")\"" "write_token = \"$(toml_escape "$write_token")\""
        if [[ -n "$trusted_server_public_key_sha256" ]]; then printf 'trusted_server_public_key_sha256 = "%s"\n' "$(toml_escape "$trusted_server_public_key_sha256")"; else printf 'allow_invalid_server_certificate = %s\n' "$([[ "$allow_invalid_server_certificate" == 1 ]] && echo true || echo false)"; fi
      fi
      IFS=',' read -ra selected_providers <<< "$agent_providers"
      for agent_provider in "${selected_providers[@]}"; do
        printf '%s\n' '' '[[agent.providers]]' "name = \"local-$([[ "$agent_provider" == ollama ]] && echo ollama || echo llama)\"" "type = \"$agent_provider\""
        if [[ "$agent_provider" == ollama ]]; then
          printf 'url = "%s"\n' "$(toml_escape "$ollama_url")"
        else
          printf 'url = "%s"\nmodel_directories = ["%s"]\n' "$(toml_escape "$llama_url")" "$(toml_escape "$model_directory")"
          printf '%s\n' 'inspect_gguf = true' 'hash_model_files = false' 'probe_mode = "loaded-only"'
        fi
      done
    fi
    if has_role server; then
      printf '%s\n' '' '[server]' "listen = \"$(toml_escape "$listen_url")\"" "opencode_inference_url = \"$(toml_escape "$litellm_url")\""
      if [[ "$embed_inference_api_key" == 1 ]]; then printf 'embed_opencode_inference_api_key = true\nopencode_inference_api_key = "%s"\n' "$(toml_escape "$inference_api_key")"; else printf '%s\n' 'embed_opencode_inference_api_key = false'; fi
      printf '%s\n' 'tls_auto_generate = true'
      printf 'tls_server_names = '; toml_string_array "$tls_server_names"; printf '\n'
      printf '%s\n' "read_token = \"$(toml_escape "$read_token")\"" "write_token = \"$(toml_escape "$write_token")\"" "state_directory = \"$(toml_escape "$state_path")\""
    fi
    if has_role client; then
      printf '%s\n' '' '[client]'
      if has_role server; then
        printf 'token = "%s"\n' "$(toml_escape "$read_token")"
      else
        printf 'source = "%s"\ntoken = "%s"\n' "$(toml_escape "$server_url")" "$(toml_escape "$server_token")"
        if [[ -n "$trusted_server_public_key_sha256" ]]; then printf 'trusted_server_public_key_sha256 = "%s"\n' "$(toml_escape "$trusted_server_public_key_sha256")"; else printf 'allow_invalid_server_certificate = %s\n' "$([[ "$allow_invalid_server_certificate" == 1 ]] && echo true || echo false)"; fi
      fi
      IFS=',' read -ra selected_targets <<< "$client_targets"
      for client_target in "${selected_targets[@]}"; do
        printf '%s\n' '' '[[client.targets]]' "type = \"$client_target\""
        if [[ "$client_target" == opencode ]]; then
          printf 'path = "%s"\n' "$(toml_escape "$opencode_path")"
        else
          printf 'path = "%s"\nprimary_config_path = "%s"\nservice_name = "%s"\nhealth_url = "%s"\n' "$(toml_escape "$litellm_generated")" "$(toml_escape "$litellm_primary")" "$(toml_escape "$litellm_service")" "$(toml_escape "$litellm_health")"
        fi
      done
    fi
    printf '%s\n' '' '[logging]' "file = \"$(toml_escape "$log_path")\""
  } > "$candidate"
fi
chmod 0600 "$candidate"
"$staged" config validate --config "$candidate"

if [[ -e "$config" ]]; then
  if [[ -r "$config" ]]; then "$staged" config validate --config "$config"; else sudo -- "$staged" config validate --config "$config"; fi
  echo "preserving existing configuration $config" >&2
fi

if [[ "$install_service" == 1 ]]; then
  sudo -- install -d -m 0755 "$install_dir"
  installed_binary="$install_dir/llm-glue"
  if sudo -- test -f "$installed_binary"; then
    prior_binary=1
    sudo -- cp -p -- "$installed_binary" "$installed_binary.rollback"
  fi
  if sudo -- systemctl is-active --quiet llm-glue.service; then
    service_was_active=1
    sudo -- systemctl stop llm-glue.service
  fi
  sudo -- install -m 0755 "$staged" "$installed_binary"
  binary_replaced=1
  if ! sudo -- test -e "$config"; then sudo -- install -m 0600 "$candidate" "$config"; fi
  sudo -- "$installed_binary" service install --config "$config"
  sudo -- "$installed_binary" service start --config "$config"
else
  mkdir -p "$install_dir"
  if [[ -f "$install_dir/llm-glue" ]]; then cp -p -- "$install_dir/llm-glue" "$install_dir/llm-glue.rollback"; fi
  install -m 0755 "$staged" "$install_dir/llm-glue"
  if [[ ! -e "$config" ]]; then install -m 0600 "$candidate" "$config"; fi
fi
echo "installed $tag to $install_dir with config $config"
