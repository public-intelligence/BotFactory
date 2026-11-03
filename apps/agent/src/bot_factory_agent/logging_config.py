"""JSON line logging for stdout (aggregators, filters); optional plain text via LOG_FORMAT=text."""

from __future__ import annotations

import json
import logging
import os
from datetime import datetime, timezone
from typing import Any

# LogRecord has many internal attributes; we only merge user-supplied `extra=...` keys into JSON.
_LOG_RECORD_SKIP = frozenset(
    {
        "name",
        "msg",
        "args",
        "created",
        "filename",
        "funcName",
        "levelname",
        "levelno",
        "lineno",
        "module",
        "msecs",
        "message",
        "pathname",
        "process",
        "processName",
        "relativeCreated",
        "thread",
        "threadName",
        "exc_info",
        "exc_text",
        "stack_info",
        "taskName",
    }
)


class JsonLineFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        ts = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"
        payload: dict[str, Any] = {
            "ts": ts,
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
        }
        for k, v in record.__dict__.items():
            if k in _LOG_RECORD_SKIP or k.startswith("_"):
                continue
            try:
                json.dumps(v)
                payload[k] = v
            except (TypeError, ValueError):
                payload[k] = str(v)
        if record.exc_info:
            payload["exception"] = self.formatException(record.exc_info)
        return json.dumps(payload, default=str)


def configure_logging() -> None:
    level_name = (os.getenv("LOG_LEVEL") or "INFO").upper()
    level = getattr(logging, level_name, logging.INFO)
    fmt = (os.getenv("LOG_FORMAT") or "json").strip().lower()

    root = logging.getLogger()
    # Replace any prior basicConfig handlers so JSON format is consistent across the process.
    root.handlers.clear()
    root.setLevel(level)

    handler = logging.StreamHandler()
    handler.setLevel(level)
    if fmt == "text":
        handler.setFormatter(
            logging.Formatter("%(levelname)s %(name)s %(message)s"),
        )
    else:
        handler.setFormatter(JsonLineFormatter())

    root.addHandler(handler)
