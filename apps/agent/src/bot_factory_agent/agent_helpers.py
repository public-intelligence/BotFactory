"""Pure helpers extracted from agent.py for unit testing without LiveKit runtime."""

from __future__ import annotations

import json
from datetime import datetime, timezone
from typing import Any

from bot_factory_agent.provider_errors import classify
from bot_factory_agent.types import AgentEventPayload, RoomMetadata


def iso_ts() -> str:
    # ISO-8601 UTC with Z suffix (matches typical AgentEvent consumers).
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def agent_event(
    event_type: str,
    bot_id: int,
    conversation_id: int,
    data: dict[str, Any] | None = None,
    room_name: str | None = None,
) -> AgentEventPayload:
    return AgentEventPayload(
        event_type=event_type,
        bot_id=bot_id,
        conversation_id=conversation_id,
        timestamp=iso_ts(),
        data=data,
        room_name=room_name,
    )


def error_to_str(err: object) -> str:
    # Session errors are not always Exception instances across SDK versions.
    if isinstance(err, BaseException):
        return str(err)
    if isinstance(err, dict) and "message" in err:
        return str(err["message"])
    return str(err)


def source_to_data(source: object) -> object:
    # Shrink opaque SDK objects to JSON-friendly payloads for AgentEvent.data.
    if source is None:
        return None
    if isinstance(source, dict) and "label" in source:
        return {"label": str(source["label"])}
    lab = getattr(source, "label", None)
    if lab is not None and callable(lab):
        try:
            return {"label": str(lab())}
        except Exception:
            pass
    if lab is not None:
        return {"label": str(lab)}
    return str(source)


def enriched_error_data(
    err: object,
    source: object,
    *,
    http_status: int | None = None,
) -> dict[str, Any]:
    raw = error_to_str(err)
    info = classify(source, raw, http_status=http_status)
    data = info.to_event_data(raw)
    data["source"] = source_to_data(source)
    return data


def metrics_to_data(metrics: object) -> dict[str, Any]:
    # Metrics objects vary by livekit-agents version (Pydantic v1/v2 or ad-hoc types).
    md = getattr(metrics, "model_dump", None)
    if callable(md):
        try:
            return md(mode="json")
        except Exception:
            pass
    d = getattr(metrics, "dict", None)
    if callable(d):
        try:
            return d()
        except Exception:
            pass
    try:
        s = json.dumps(metrics, default=str)
        return json.loads(s) if s else {}
    except Exception:
        return {"_serializationFailed": True}


def require_eleven_key(meta: RoomMetadata) -> str:
    key = (meta.eleven_labs_api_key or "").strip()
    if not key:
        raise ValueError(
            "ElevenLabs API key missing; job metadata must include elevenLabsApiKey"
        )
    return key
