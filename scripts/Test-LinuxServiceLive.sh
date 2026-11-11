#!/usr/bin/env bash
set -euo pipefail

[[ "$(id -u)" == 0 ]] || { echo 'Run as root.' >&2; exit 2; }
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_binary="${1:-$root/artifacts/final-qualification/publish/linux-x64/llm-glue}"
[[ -x "$source_binary" ]] || { echo "missing executable: $source_binary" >&2; exit 2; }
systemctl is-system-running >/dev/null
! systemctl cat llm-glue.service >/dev/null 2>&1 || { echo 'Refusing to modify an existing llm-glue.service.' >&2; exit 2; }
! getent passwd llm-glue >/dev/null || { echo 'Refusing to modify an existing llm-glue account.' >&2; exit 2; }
[[ ! -e /var/lib/llm-glue && ! -e /var/log/llm-glue ]] || { echo 'Refusing to modify existing LLM Glue service directories.' >&2; exit 2; }

id="$(date +%s)-$$"
test_root="/opt/llm-glue-qualification-$id"
evidence="$root/artifacts/live-systemd-qualification/$id"
executable="$test_root/llm-glue"
moved="$test_root/moved/llm-glue"
config="$test_root/llm-glue.conf"
installed=0
fixture_pid=

cleanup() {
  [[ -z "$fixture_pid" ]] || kill "$fixture_pid" >/dev/null 2>&1 || true
  if ((installed)); then "$moved" service uninstall --config "$config" >/dev/null 2>&1 || "$executable" service uninstall --config "$config" >/dev/null 2>&1 || true; fi
  systemctl disable --now llm-glue.service >/dev/null 2>&1 || true
  rm -f -- /etc/systemd/system/llm-glue.service
  systemctl daemon-reload >/dev/null 2>&1 || true
  userdel llm-glue >/dev/null 2>&1 || true
  rm -rf -- "$test_root" /var/lib/llm-glue /var/log/llm-glue
}
trap cleanup EXIT

install -d -m 0755 "$test_root" "$evidence"
install -m 0755 "$source_binary" "$executable"
port_file="$test_root/ollama-port"
python3 "$root/scripts/ollama-fixture-server.py" --port-file "$port_file" &
fixture_pid=$!
for _ in $(seq 1 50); do [[ -s "$port_file" ]] && break; sleep 0.1; done
[[ -s "$port_file" ]] || { echo 'fixture server did not publish its port' >&2; exit 1; }
fixture_port="$(cat "$port_file")"
target="$test_root/managed/opencode.json"
printf '%s\n' \
  '[roles]' 'agent = true' 'server = true' 'client = true' '' \
  '[agent]' 'discovery_interval_seconds = 1' '' \
  '[[agent.providers]]' 'type = "ollama"' 'name = "service-fixture"' "url = \"http://127.0.0.1:$fixture_port\"" 'probe_mode = "off"' '' \
  '[server]' 'listen = "http://127.0.0.1:8765"' "state_directory = \"$test_root/state\"" '' \
  '[client]' 'source = "http://127.0.0.1:8765"' 'poll_interval_seconds = 1' '' \
  '[[client.targets]]' 'type = "opencode"' "path = \"$target\"" '' \
  '[logging]' "file = \"$test_root/logs/llm-glue.log\"" > "$config"
chmod 0600 "$config"

wait_healthy() {
  for _ in $(seq 1 45); do curl --fail --silent http://127.0.0.1:8765/api/v1/health >/dev/null 2>&1 && return; sleep 1; done
  echo 'service did not become healthy' >&2
  return 1
}

"$executable" config validate --config "$config"
"$executable" service install --config "$config"
installed=1
"$executable" service start --config "$config"
wait_healthy
[[ "$(systemctl show llm-glue.service -p User --value)" == llm-glue ]]
systemctl show llm-glue.service -p ExecStart --value | grep -Fq "$executable"
for _ in $(seq 1 45); do [[ -f "$target.llm-glue-ownership.json" ]] && break; sleep 1; done
python3 - "$target" "$target.llm-glue-ownership.json" <<'PY'
import json, pathlib, sys
catalog = json.loads(pathlib.Path(sys.argv[1]).read_text())
ownership = json.loads(pathlib.Path(sys.argv[2]).read_text())
assert "ollama" in catalog["disabled_providers"]
assert "ollama" in ownership["disabledProviders"]
assert "service-fixture" in catalog["providers"]["llm-glue-ollama-service-fixture"]["models"]
PY

"$executable" service stop --config "$config"
install -d -m 0755 "$(dirname "$moved")"
install -m 0755 "$executable" "$moved"
"$moved" service repair --config "$config"
systemctl show llm-glue.service -p ExecStart --value | grep -Fq "$moved"
"$moved" service start --config "$config"
wait_healthy
[[ -f "$target.llm-glue-ownership.json" ]]

cp -p -- "$moved" "$moved.rollback"
"$moved" update rollback --service --config "$config"
wait_healthy
"$moved" service uninstall --config "$config"
installed=0
! systemctl cat llm-glue.service >/dev/null 2>&1
getent passwd llm-glue >/dev/null
[[ -f "$config" && -d "$test_root/state" && -f "$test_root/logs/llm-glue.log" && -f "$target.llm-glue-ownership.json" ]]
printf 'passed %s identity=llm-glue executable=%s rollback=success preservation=success ownership-sidecar=verified\n' "$(date --iso-8601=seconds)" "$moved" | tee "$evidence/result.txt"
