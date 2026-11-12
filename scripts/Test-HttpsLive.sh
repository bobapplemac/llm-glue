#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
binary="${LLM_GLUE_TEST_BINARY:-$root/artifacts/service-check/linux-x64/llm-glue}"
[[ -x "$binary" ]] || { echo "publish a Linux test binary first: $binary" >&2; exit 2; }
temporary="$(mktemp -d)"
pid=""
cleanup() {
  if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  rm -rf -- "$temporary"
}
trap cleanup EXIT
port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')"
server="$temporary/server.conf"
printf '%s\n' '[roles]' 'agent=false' 'server=true' 'client=false' '[server]' \
  "listen=\"https://localhost:$port\"" 'tls_server_names=["localhost"]' 'state_directory="state"' > "$server"
pin="$($binary doctor --config "$server" | sed -n 's/^Server TLS public-key SHA-256: //p')"
[[ "$pin" =~ ^[0-9a-f]{64}$ ]] || { echo 'doctor did not return a public-key pin' >&2; exit 1; }
"$binary" run --config "$server" > "$temporary/server.log" 2>&1 &
pid=$!
for _ in $(seq 1 30); do
  if curl --fail --silent --show-error --insecure "https://localhost:$port/api/v1/health" >/dev/null 2>&1; then break; fi
  sleep 0.25
done
curl --fail --silent --show-error --insecure "https://localhost:$port/api/v1/health" >/dev/null
client="$temporary/client.conf"
printf '%s\n' '[roles]' 'agent=false' 'server=false' 'client=true' '[client]' \
  "source=\"https://localhost:$port\"" "trusted_server_public_key_sha256=\"$pin\"" > "$client"
"$binary" status --config "$client" >/dev/null
"$binary" tls validate --config "$server" >/dev/null
"$binary" tls reload --config "$server" >/dev/null
"$binary" tls renew --config "$server" >/dev/null
renewed_pin="$($binary doctor --config "$server" | sed -n 's/^Server TLS public-key SHA-256: //p')"
[[ "$renewed_pin" == "$pin" ]] || { echo 'TLS renewal changed the public-key pin' >&2; exit 1; }
"$binary" tls regenerate --config "$server" >/dev/null
replacement_pin="$($binary doctor --config "$server" | sed -n 's/^Server TLS public-key SHA-256: //p')"
[[ "$replacement_pin" =~ ^[0-9a-f]{64}$ && "$replacement_pin" != "$pin" ]] || { echo 'TLS regeneration did not rotate the public key' >&2; exit 1; }
printf '%s\n' '[roles]' 'agent=false' 'server=false' 'client=true' '[client]' \
  "source=\"https://localhost:$port\"" "trusted_server_public_key_sha256=\"$replacement_pin\"" > "$client"
"$binary" status --config "$client" >/dev/null
echo "Linux HTTPS live test passed; rotated public-key SHA-256 $pin -> $replacement_pin"
