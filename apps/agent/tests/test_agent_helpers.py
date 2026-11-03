"""Unit tests for pure agent helpers (no LiveKit runtime / VAD / entrypoint)."""

from __future__ import annotations

import pytest

from bot_factory_agent.agent_helpers import (
    agent_event,
    enriched_error_data,
    error_to_str,
    metrics_to_data,
    require_eleven_key,
    source_to_data,
)
from bot_factory_agent.config import dev_room_metadata
from bot_factory_agent.provider_errors import CODE_ELEVENLABS_QUOTA_EXCEEDED


def test_error_to_str_handles_exception_dict_and_other() -> None:
    assert error_to_str(ValueError("boom")) == "boom"
    assert error_to_str({"message": "from-dict"}) == "from-dict"
    assert error_to_str(123) == "123"


def test_source_to_data_shrinks_labels() -> None:
    assert source_to_data(None) is None
    assert source_to_data({"label": "tts-elevenlabs"}) == {"label": "tts-elevenlabs"}

    class WithLabel:
        label = "stt-elevenlabs"

    assert source_to_data(WithLabel()) == {"label": "stt-elevenlabs"}

    class CallableLabel:
        def label(self) -> str:
            return "llm-gemini"

    assert source_to_data(CallableLabel()) == {"label": "llm-gemini"}


def test_enriched_error_data_classifies_quota() -> None:
    data = enriched_error_data(
        "insufficient credits",
        {"label": "tts-elevenlabs"},
        http_status=402,
    )
    assert data["errorCode"] == CODE_ELEVENLABS_QUOTA_EXCEEDED
    assert data["source"] == {"label": "tts-elevenlabs"}
    assert "error" in data


def test_metrics_to_data_model_dump_and_fallback() -> None:
    class Dumpable:
        def model_dump(self, mode: str = "python") -> dict:
            return {"ttft": 0.2, "mode": mode}

    assert metrics_to_data(Dumpable()) == {"ttft": 0.2, "mode": "json"}

    class Dictable:
        def dict(self) -> dict:
            return {"a": 1}

    assert metrics_to_data(Dictable()) == {"a": 1}
    assert metrics_to_data({"x": 1}) == {"x": 1}


def test_require_eleven_key() -> None:
    meta = dev_room_metadata()
    meta.eleven_labs_api_key = "  sk-test  "
    assert require_eleven_key(meta) == "sk-test"

    meta.eleven_labs_api_key = ""
    with pytest.raises(ValueError, match="ElevenLabs API key missing"):
        require_eleven_key(meta)


def test_agent_event_shape() -> None:
    ev = agent_event("Error", 1, 2, {"k": "v"})
    assert ev.event_type == "Error"
    assert ev.bot_id == 1
    assert ev.conversation_id == 2
    assert ev.data == {"k": "v"}
    assert ev.timestamp.endswith("Z")
    d = ev.to_json_dict()
    assert d["eventType"] == "Error"
    assert d["data"] == {"k": "v"}
