"""GET /healthz in a background thread (parity with agent-js/src/health.ts).

Separate from the LiveKit gRPC/worker port so Kubernetes/Compose can probe container liveness.
"""

from __future__ import annotations

import logging
import os
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

logger = logging.getLogger("health")


class _Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args: object) -> None:
        logger.debug("%s - %s", self.address_string(), fmt % args)

    def do_GET(self) -> None:  # noqa: N802
        # Strip query string so /healthz?foo still matches.
        path = (self.path or "/").split("?", 1)[0]
        if path == "/healthz":
            self.send_response(200)
            self.send_header("Content-Type", "text/plain")
            self.end_headers()
            self.wfile.write(b"ok")
            return
        self.send_response(404)
        self.end_headers()


def start_health_server() -> None:
    port = int(os.getenv("HEALTH_PORT", "8089"))

    def _run() -> None:
        server = HTTPServer(("0.0.0.0", port), _Handler)
        logger.info("health server listening on port %s", port)
        server.serve_forever()

    # Daemon: do not block process exit when the worker shuts down.
    thread = threading.Thread(target=_run, name="health", daemon=True)
    thread.start()
