"""JSON contracts shared with .NET and the Angular client (see agent-js)."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass
class RoomMetadata:
    """Parsed from LiveKit room.metadata JSON (set when the room is created).

    The exception is eleven_labs_api_key, which comes from the job metadata: LiveKit
    delivers room.metadata to every participant, so secrets cannot travel there.
    """

    bot_id: int
    conversation_id: int
    voice_id: str
    eleven_labs_api_key: str
    no_speech_timeout_ms: int
    max_utterance_ms: int
    enable_barge_in: bool
    silence_end_ms: int
    vad_activation_threshold: float
    vad_min_speech_duration_ms: int
    interruption_min_duration_ms: int
    interruption_min_words: int
    greeting_message: str | None = None
    is_resume: bool = False
    # Words the speech recogniser should favour for this bot (see keyterms.py).
    transcription_vocabulary: list[str] = field(default_factory=list)


@dataclass
class AgentConfig:
    """Process-level settings from environment variables."""

    livekit_url: str
    livekit_api_key: str
    livekit_api_secret: str
    backend_url: str
    agent_name: str
    # Shared secret proving agent identity on /livekit/* callbacks (matches
    # .NET AgentCallback:SharedSecret). Empty = gate open (dev only).
    backend_shared_secret: str


@dataclass(frozen=True)
class WorkerServerConfig:
    """AgentServer tuning for 2 GB / 1 vCPU ACI (overridable via AGENT_* env vars)."""

    num_idle_processes: int
    job_memory_warn_mb: float
    job_memory_limit_mb: float
    load_threshold: float
    initialize_process_timeout: float


@dataclass
class AgentEventPayload:
    """Same shape as agent-js `AgentEvent` (camelCase in JSON)."""

    event_type: str
    bot_id: int
    conversation_id: int
    timestamp: str
    data: dict[str, Any] | None = None
    # The LiveKit room this agent is in. Bot Factory uses it to tell the events of the conversation's
    # current call from those of one that was replaced (its agent may still be leaving).
    room_name: str | None = None

    def to_json_dict(self) -> dict[str, Any]:
        # Omit `data` when absent so payloads match agent-js / JSON.stringify(undefined) behavior.
        d: dict[str, Any] = {
            "eventType": self.event_type,
            "botId": self.bot_id,
            "conversationId": self.conversation_id,
            "timestamp": self.timestamp,
        }
        if self.data is not None:
            d["data"] = self.data
        if self.room_name is not None:
            d["roomName"] = self.room_name
        return d
