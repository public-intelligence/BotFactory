"""Environment and room metadata parsing (parity with agent-js/src/config.ts)."""

from __future__ import annotations

import json
import os
from typing import Any, Mapping

from bot_factory_agent.types import AgentConfig, RoomMetadata, WorkerServerConfig


def _env_int(name: str, default: int) -> int:
    raw = os.getenv(name)
    if raw is None or not raw.strip():
        return default
    return int(raw.strip())


def _env_float(name: str, default: float) -> float:
    raw = os.getenv(name)
    if raw is None or not raw.strip():
        return default
    return float(raw.strip())


def load_worker_server_config() -> WorkerServerConfig:
    return WorkerServerConfig(
        num_idle_processes=_env_int("AGENT_NUM_IDLE_PROCESSES", 1),
        job_memory_warn_mb=_env_float("AGENT_JOB_MEMORY_WARN_MB", 1600.0),
        job_memory_limit_mb=_env_float("AGENT_JOB_MEMORY_LIMIT_MB", 1900.0),
        load_threshold=_env_float("AGENT_LOAD_THRESHOLD", 0.8),
        initialize_process_timeout=_env_float("AGENT_INITIALIZE_PROCESS_TIMEOUT", 60.0),
    )


def load_env_config() -> AgentConfig:
    return AgentConfig(
        livekit_url=os.getenv("LIVEKIT_URL", "ws://localhost:7880"),
        livekit_api_key=os.getenv("LIVEKIT_API_KEY", ""),
        livekit_api_secret=os.getenv("LIVEKIT_API_SECRET", ""),
        backend_url=os.getenv("BACKEND_URL", "http://localhost:5000"),
        agent_name=(os.getenv("AGENT_NAME") or "bot-factory-agent").strip() or "bot-factory-agent",
        backend_shared_secret=os.getenv("BACKEND_SHARED_SECRET", ""),
    )


def dev_room_metadata() -> RoomMetadata:
    return RoomMetadata(
        bot_id=0,
        conversation_id=0,
        voice_id="EXAVITQu4vr4xnSDxMaL",
        eleven_labs_api_key="",
        no_speech_timeout_ms=15000,
        max_utterance_ms=20000,
        enable_barge_in=True,
        silence_end_ms=700,
        vad_activation_threshold=0.80,
        vad_min_speech_duration_ms=400,
        interruption_min_duration_ms=1000,
        interruption_min_words=1,
        greeting_message="¡Hola! Estoy para servirte.",
    )


def _require_number(obj: Mapping[str, Any], key: str) -> int | float:
    v = obj.get(key)
    # Reject bool: in Python, bool is a subclass of int (True == 1).
    if not isinstance(v, (int, float)) or isinstance(v, bool) or (isinstance(v, float) and v != v):
        raise TypeError(f'room metadata: "{key}" must be a number')
    return v


def _require_non_empty_str(obj: Mapping[str, Any], key: str) -> str:
    v = obj.get(key)
    if not isinstance(v, str) or not v.strip():
        raise ValueError(f'room metadata: "{key}" must be a non-empty string')
    return v.strip()


def _job_eleven_key(raw: str | None) -> str:
    # Never echo `raw` in errors or logs: it holds the secret.
    try:
        parsed: Any = json.loads(raw or "{}")
    except json.JSONDecodeError:
        return ""
    key = parsed.get("elevenLabsApiKey") if isinstance(parsed, dict) else None
    return key.strip() if isinstance(key, str) else ""


def parse_room_metadata(raw: str | None, job_metadata: str | None = None) -> RoomMetadata:
    if raw is None or not raw.strip():
        raise ValueError("room metadata: empty or undefined")
    try:
        parsed: Any = json.loads(raw)
    except json.JSONDecodeError as e:
        raise ValueError(f"room metadata: invalid JSON ({e})") from e
    if not isinstance(parsed, dict):
        raise ValueError("room metadata: root must be a JSON object")

    bot_id = int(_require_number(parsed, "botId"))
    conversation_id = int(_require_number(parsed, "conversationId"))
    voice_id = _require_non_empty_str(parsed, "voiceId")
    # The key arrives in job metadata, which only this worker receives. Room metadata reaches
    # every participant, so it is read only for rooms created by an API that predates the
    # move: that lets the API and the agent be updated in either order.
    eleven = _job_eleven_key(job_metadata) or _require_non_empty_str(parsed, "elevenLabsApiKey")

    nst = parsed.get("noSpeechTimeoutMs")
    # Optional fields: same office-profile defaults as the .NET Bot entity when missing or wrong type.
    no_speech = int(nst) if isinstance(nst, (int, float)) and not isinstance(nst, bool) else 15000

    mut = parsed.get("maxUtteranceMs")
    max_ut = int(mut) if isinstance(mut, (int, float)) and not isinstance(mut, bool) else 20000

    ebi = parsed.get("enableBargeIn")
    enable_barge = ebi if isinstance(ebi, bool) else True

    sem = parsed.get("silenceEndMs")
    silence_end = int(sem) if isinstance(sem, (int, float)) and not isinstance(sem, bool) else 700

    vat = parsed.get("vadActivationThreshold")
    vad_thresh = float(vat) if isinstance(vat, (int, float)) and not isinstance(vat, bool) else 0.80

    vmsd = parsed.get("vadMinSpeechDurationMs")
    vad_min_speech = int(vmsd) if isinstance(vmsd, (int, float)) and not isinstance(vmsd, bool) else 400

    imd = parsed.get("interruptionMinDurationMs")
    interruption_min_duration = int(imd) if isinstance(imd, (int, float)) and not isinstance(imd, bool) else 1500

    imw = parsed.get("interruptionMinWords")
    interruption_min_words = int(imw) if isinstance(imw, (int, float)) and not isinstance(imw, bool) else 1

    gm = parsed.get("greetingMessage")
    greeting_message = gm.strip() if isinstance(gm, str) and gm.strip() else None

    ir = parsed.get("isResume")
    is_resume = ir if isinstance(ir, bool) else False

    return RoomMetadata(
        bot_id=bot_id,
        conversation_id=conversation_id,
        voice_id=voice_id,
        eleven_labs_api_key=eleven,
        no_speech_timeout_ms=no_speech,
        max_utterance_ms=max_ut,
        enable_barge_in=enable_barge,
        silence_end_ms=silence_end,
        vad_activation_threshold=vad_thresh,
        vad_min_speech_duration_ms=vad_min_speech,
        interruption_min_duration_ms=interruption_min_duration,
        interruption_min_words=interruption_min_words,
        greeting_message=greeting_message,
        is_resume=is_resume,
    )
