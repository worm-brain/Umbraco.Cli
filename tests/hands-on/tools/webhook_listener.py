#!/usr/bin/env python3
"""Tiny webhook listener: appends each request (headers + JSON body) to a JSONL file and returns 200.

Usage (from tests/hands-on): python3 tools/webhook_listener.py [port] &   -> writes webhook-requests.jsonl
(override with WEBHOOK_LOG=<file>). Point a webhook at http://127.0.0.1:<port>/hook.
"""
import json, os, sys, time
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
OUT = Path(os.environ.get("WEBHOOK_LOG") or Path(__file__).resolve().parent.parent / "webhook-requests.jsonl")

class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)) or 0).decode()
        try:
            body = json.loads(body)
        except ValueError:
            pass
        with OUT.open("a") as f:
            f.write(json.dumps({"at": time.time(), "path": self.path, "headers": dict(self.headers), "body": body}) + "\n")
        self.send_response(200); self.end_headers(); self.wfile.write(b"ok")
    def log_message(self, *a):
        pass

HTTPServer(("127.0.0.1", int(sys.argv[1]) if len(sys.argv) > 1 else 8765), Handler).serve_forever()
