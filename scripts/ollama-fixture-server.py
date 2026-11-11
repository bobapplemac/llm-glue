#!/usr/bin/env python3
"""Loopback-only Ollama discovery fixture for fixed-identity service tests."""

import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class Handler(BaseHTTPRequestHandler):
    def log_message(self, _format, *_args):
        return

    def reply(self, value):
        body = json.dumps(value, separators=(",", ":")).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/api/version":
            self.reply({"version": "0.40.16"})
        elif self.path == "/api/tags":
            self.reply({"models": [{"name": "service-fixture", "details": {"family": "fixture", "quantization_level": "Q4"}}]})
        elif self.path == "/api/ps":
            self.reply({"models": []})
        else:
            self.send_error(404)

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length:
            self.rfile.read(length)
        if self.path == "/api/show":
            self.reply({
                "model_info": {"fixture.context_length": 8192},
                "capabilities": ["completion", "tools", "thinking"],
                "thinking": {"values": [False, True], "default": True},
                "details": {"family": "fixture", "quantization_level": "Q4"},
            })
        else:
            self.send_error(404)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port-file", required=True)
    args = parser.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    Path(args.port_file).write_text(str(server.server_port), encoding="ascii")
    server.serve_forever()


if __name__ == "__main__":
    main()
