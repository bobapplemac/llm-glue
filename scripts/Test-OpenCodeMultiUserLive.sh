#!/usr/bin/env bash
set -euo pipefail

[[ $(id -u) -eq 0 ]] || { echo 'run as root on an isolated Linux qualification host' >&2; exit 2; }
[[ $# -eq 1 ]] || { echo 'usage: Test-OpenCodeMultiUserLive.sh /path/to/opencode-2.2.7' >&2; exit 2; }

opencode="$(readlink -f -- "$1")"
[[ -x "$opencode" ]] || { echo "OpenCode executable not found: $opencode" >&2; exit 2; }
version="$($opencode --version)"
[[ "$version" == 'opencode v2.2.7' ]] || { echo "expected OpenCode 2.2.7, found: $version" >&2; exit 2; }

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
system_dir=/etc/opencode
system_config="$system_dir/opencode.json"
marker="$system_dir/.llm-glue-multi-user-test"
users=(llmgluetest1 llmgluetest2)
tmp="$(mktemp -d /tmp/llm-glue-opencode-users.XXXXXX)"
chmod 0755 "$tmp"
install -m 0755 "$opencode" "$tmp/opencode"
opencode="$tmp/opencode"
system_created=0
created_users=()

cleanup() {
  local user
  for user in "${created_users[@]}"; do
    if getent passwd "$user" >/dev/null; then userdel --remove "$user" >/dev/null 2>&1 || true; fi
  done
  if [[ $system_created -eq 1 && -f "$marker" ]]; then
    rm -f -- "$system_config" "$marker"
    rmdir -- "$system_dir" 2>/dev/null || true
  fi
  rm -rf -- "$tmp"
}
trap cleanup EXIT

[[ ! -e "$system_dir" ]] || { echo "$system_dir already exists; refusing to touch an existing installation" >&2; exit 2; }
for user in "${users[@]}"; do
  getent passwd "$user" >/dev/null && { echo "account $user already exists; refusing to reuse it" >&2; exit 2; }
done

install -d -m 0755 "$system_dir"
system_created=1
printf '%s\n' 'owned by scripts/Test-OpenCodeMultiUserLive.sh' > "$marker"
install -m 0644 "$root/tests/fixtures/golden/opencode.json" "$system_config"

for user in "${users[@]}"; do
  useradd --create-home --shell /bin/bash "$user"
  created_users+=("$user")
  home="$(getent passwd "$user" | cut -d: -f6)"
  install -d -o "$user" -g "$user" -m 0700 "$home/.config" "$home/.cache" "$home/.local"
  install -d -o "$user" -g "$user" -m 0700 "$home/.config/opencode" "$home/.local/share"
  unique="user-$user"
  printf '%s\n' \
    '{' \
    '  "$schema": "https://opencode.ai/config.json",' \
    '  "providers": {' \
    '    "llm-glue-llama-cpp-lab": {' \
    '      "name": "User override must lose",' \
    '      "package": "@opencode/ai/providers/openai-compatible",' \
    '      "settings": { "baseURL": "http://127.0.0.1:19999/v1" },' \
    '      "models": {' \
    '        "qwen-model": {' \
    '          "modelID": "user-override-must-lose",' \
    '          "name": "User override must lose",' \
    '          "capabilities": {' \
    '            "tools": false,' \
    '            "input": ["text"],' \
    '            "output": ["text"],' \
    '            "temperature": false,' \
    '            "reasoning": { "supported": false }' \
    '          }' \
    '        }' \
    '      }' \
    '    },' \
    "    \"$unique\": {" \
    '      "name": "User-owned provider",' \
    '      "package": "@opencode/ai/providers/openai-compatible",' \
    '      "settings": { "baseURL": "http://127.0.0.1:18888/v1" },' \
    '      "models": {' \
    '        "user-model": {' \
    '          "modelID": "user-model",' \
    '          "name": "User model",' \
    '          "capabilities": {' \
    '            "tools": false,' \
    '            "input": ["text"],' \
    '            "output": ["text"],' \
    '            "temperature": true,' \
    '            "reasoning": { "supported": false }' \
    '          }' \
    '        }' \
    '      }' \
    '    }' \
    '  }' \
    '}' > "$home/.config/opencode/opencode.json"
  chown "$user:$user" "$home/.config/opencode/opencode.json"

  runuser -u "$user" -- env \
    HOME="$home" \
    XDG_CONFIG_HOME="$home/.config" \
    XDG_DATA_HOME="$home/.local/share" \
    XDG_CACHE_HOME="$home/.cache" \
    "$opencode" --standalone api GET /api/model > "$tmp/$user-models.json"
  runuser -u "$user" -- env \
    HOME="$home" \
    XDG_CONFIG_HOME="$home/.config" \
    XDG_DATA_HOME="$home/.local/share" \
    XDG_CACHE_HOME="$home/.cache" \
    "$opencode" --standalone debug config > "$tmp/$user-sources.json"

  python3 - "$tmp/$user-models.json" "$tmp/$user-sources.json" "$user" "$home" <<'PY'
import json
import pathlib
import sys

models_path, sources_path, user, home = sys.argv[1:]
models = json.loads(pathlib.Path(models_path).read_text())["data"]
managed = [m for m in models if m["providerID"] == "llm-glue-llama-cpp-lab" and m["id"] == "qwen-model"]
assert len(managed) == 1, f"{user}: managed model missing or duplicated"
model = managed[0]
assert model["modelID"] == "qwen-model", f"{user}: user modelID overrode system config"
assert model["name"] == "Qwen Test", f"{user}: user name overrode system config"
assert model["settings"]["baseURL"] == "http://127.0.0.1:8081/v1", f"{user}: user endpoint overrode system config"
assert model["variants"] == [{"id": "thinking-on", "body": {"enable_thinking": True, "temperature": 0.6}}]
unique = [m for m in models if m["providerID"] == f"user-{user}" and m["id"] == "user-model"]
assert len(unique) == 1, f"{user}: unrelated user model was not preserved"

sources = json.loads(pathlib.Path(sources_path).read_text())
paths = {entry["path"] for entry in sources}
assert "/etc/opencode/opencode.json" in paths, f"{user}: system source absent"
assert f"{home}/.config/opencode/opencode.json" in paths, f"{user}: user source absent"
PY
done

printf 'PASS OpenCode %s: system precedence and unrelated settings verified for %s and %s\n' \
  "${version#opencode v}" "${users[0]}" "${users[1]}"
