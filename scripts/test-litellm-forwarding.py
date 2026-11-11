#!/usr/bin/env python3
"""Exercise a generated LiteLLM route against a request-capturing upstream.

This is an opt-in integration test.  It requires the pinned LiteLLM package to
be installed in the Python environment used to launch the script.  No external
inference service or credential is required.
"""

from __future__ import annotations

import argparse
import http.server
import json
import os
import pathlib
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request


EXPECTED_VERSION = "1.109.0"


def free_port() -> int:
    with socket.socket() as candidate:
        candidate.bind(("127.0.0.1", 0))
        return int(candidate.getsockname()[1])


class CaptureHandler(http.server.BaseHTTPRequestHandler):
    captured: dict[str, object] | None = None
    captured_event = threading.Event()

    def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        length = int(self.headers.get("content-length", "0"))
        body = json.loads(self.rfile.read(length))
        type(self).captured = {
            "path": self.path,
            "authorization": self.headers.get("authorization"),
            "body": body,
        }
        type(self).captured_event.set()
        payload = json.dumps(
            {
                "id": "chatcmpl-llm-glue-test",
                "object": "chat.completion",
                "created": 1,
                "model": body.get("model", "upstream-model"),
                "choices": [
                    {
                        "index": 0,
                        "message": {"role": "assistant", "content": "FORWARDED"},
                        "finish_reason": "stop",
                    }
                ],
                "usage": {"prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2},
            }
        ).encode()
        self.send_response(200)
        self.send_header("content-type", "application/json")
        self.send_header("content-length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, _format: str, *_args: object) -> None:
        pass


def request_json(
    url: str, payload: dict[str, object] | None = None, bearer_token: str | None = None
) -> dict[str, object]:
    data = None if payload is None else json.dumps(payload).encode()
    request = urllib.request.Request(url, data=data)
    if data is not None:
        request.add_header("content-type", "application/json")
    if bearer_token is not None:
        request.add_header("authorization", f"Bearer {bearer_token}")
    with urllib.request.urlopen(request, timeout=5) as response:
        return json.load(response)


def wait_ready(base_url: str, process: subprocess.Popen[str], master_key: str) -> None:
    deadline = time.monotonic() + 45
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"LiteLLM exited before readiness with code {process.returncode}")
        try:
            request_json(f"{base_url}/v1/models", bearer_token=master_key)
            return
        except (OSError, urllib.error.URLError, json.JSONDecodeError) as error:
            last_error = error
            time.sleep(0.25)
    raise RuntimeError(f"LiteLLM did not become ready: {last_error}")


def litellm_command() -> list[str]:
    executable = shutil.which("litellm")
    if executable:
        return [executable]
    scripts = pathlib.Path(sys.executable).parent
    candidate = scripts / ("litellm.exe" if os.name == "nt" else "litellm")
    if candidate.exists():
        return [str(candidate)]
    raise RuntimeError("litellm executable not found; install litellm[proxy]==1.109.0")


def installed_version() -> str:
    from importlib.metadata import version

    return version("litellm")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--keep-logs", action="store_true")
    args = parser.parse_args()

    version = installed_version()
    if version != EXPECTED_VERSION:
        raise RuntimeError(f"expected LiteLLM {EXPECTED_VERSION}, found {version}")

    upstream_port = free_port()
    proxy_port = free_port()
    upstream = http.server.ThreadingHTTPServer(("127.0.0.1", upstream_port), CaptureHandler)
    thread = threading.Thread(target=upstream.serve_forever, daemon=True)
    thread.start()

    with tempfile.TemporaryDirectory(prefix="llm-glue-litellm-") as temporary:
        root = pathlib.Path(temporary)
        config = root / "config.yaml"
        log_path = root / "litellm.log"
        golden_path = pathlib.Path(__file__).resolve().parents[1] / "tests" / "fixtures" / "golden" / "litellm.yaml"
        generated = golden_path.read_text(encoding="utf-8")
        generated = generated.replace("llama-cpp-lab-qwen-model", "llm-glue-forwarding")
        generated = generated.replace("openai/qwen-model", "openai/upstream-model")
        generated = generated.replace("http://127.0.0.1:8081/v1", f"http://127.0.0.1:{upstream_port}/v1")
        if "llm-glue-forwarding" not in generated or "openai/upstream-model" not in generated:
            raise RuntimeError("generated LiteLLM golden fixture no longer has the expected route shape")
        config.write_text(generated, encoding="utf-8")
        environment = os.environ.copy()
        environment["LLM_GLUE_PROVIDER_API_KEY"] = "integration-secret"
        master_key = "sk-llm-glue-integration-master-key-00000001"
        environment["LITELLM_MASTER_KEY"] = master_key
        log = log_path.open("w+", encoding="utf-8")
        process = subprocess.Popen(
            litellm_command() + ["--config", str(config), "--host", "127.0.0.1", "--port", str(proxy_port)],
            stdout=log,
            stderr=subprocess.STDOUT,
            env=environment,
            text=True,
        )
        try:
            base_url = f"http://127.0.0.1:{proxy_port}"
            wait_ready(base_url, process, master_key)
            response = request_json(
                f"{base_url}/v1/chat/completions",
                {
                    "model": "llm-glue-forwarding",
                    "messages": [{"role": "user", "content": "reply FORWARDED"}],
                    "temperature": 0.25,
                    "top_p": 0.8,
                    "max_tokens": 7,
                    "enable_thinking": True,
                },
                bearer_token=master_key,
            )
            if response["choices"][0]["message"]["content"] != "FORWARDED":
                raise AssertionError(f"unexpected proxy response: {response}")
            if not CaptureHandler.captured_event.wait(5) or CaptureHandler.captured is None:
                raise AssertionError("upstream did not receive a request")
            captured = CaptureHandler.captured
            body = captured["body"]
            expected = {
                "model": "upstream-model",
                "temperature": 0.25,
                "top_p": 0.8,
                "max_tokens": 7,
                "enable_thinking": True,
            }
            for key, value in expected.items():
                if body.get(key) != value:
                    raise AssertionError(f"forwarded {key!r} was {body.get(key)!r}, expected {value!r}")
            if captured["path"] != "/v1/chat/completions":
                raise AssertionError(f"unexpected upstream path: {captured['path']}")
            if captured["authorization"] != "Bearer integration-secret":
                raise AssertionError("environment-backed provider credential was not forwarded")
            print(f"PASS LiteLLM {version}: generated route forwarded response and inference parameters")
        except Exception:
            log.flush()
            log.seek(0)
            print(log.read(), file=sys.stderr)
            raise
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
            log.close()
            upstream.shutdown()
            upstream.server_close()
            if args.keep_logs:
                saved = pathlib.Path.cwd() / "artifacts" / "litellm-forwarding.log"
                saved.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(log_path, saved)
                print(f"log: {saved}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
