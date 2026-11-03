"""Per-turn metric aggregation for the agent pipeline.

LiveKit emits a `metrics_collected` event per pipeline stage (STT, EOU, LLM,
TTS, VAD) — several per turn, some many times per second. Forwarding each one
to the feed is too noisy (see plan §metrics_collected). Instead we accumulate
the turn-defining stages into one `turn_metrics` event flushed when the agent
finishes responding.

The turn id is shared with the backend LLM call (`llm_backend.BackendLLM`): the
first turn-defining signal (STT/EOU metric, final transcript, or the chat-turn
request) mints it, and every later stage of the same turn reuses it. That lets
the feed stitch this pipeline waterfall together with the backend's
`turn_completed` waterfall under one turn id.
"""

from __future__ import annotations

import uuid
from typing import Any


def _metric_kind(metrics: object) -> str | None:
    """Classify a LiveKit metrics object by its type name.

    Robust to version differences (snake_case dict vs typed object): we match on
    the class name rather than a serialized `type` field. Order matters so
    `eou`/`stt`/`tts`/`llm`/`vad` don't collide.
    """
    name = type(metrics).__name__.lower()
    for kind in ("eou", "stt", "tts", "llm", "vad"):
        if kind in name:
            return kind
    return None


def _ms(metrics: object, *attrs: str) -> int | None:
    """First present attr (seconds, float) converted to integer milliseconds."""
    for attr in attrs:
        val = getattr(metrics, attr, None)
        if val is None:
            continue
        try:
            return round(float(val) * 1000)
        except (TypeError, ValueError):
            continue
    return None


class TurnMetricsTracker:
    """Accumulates pipeline-stage latencies for the in-flight turn.

    A turn starts on the first turn-defining signal (STT/EOU metric, final
    transcript, or the backend chat-turn call) and ends when the agent returns
    to `listening`. LLM/TTS metrics only attach to an already-open turn so the
    startup greeting (TTS-only, no user turn) doesn't spawn a spurious event.
    """

    def __init__(self) -> None:
        self._turn_id: str | None = None
        self._acc: dict[str, int] = {}

    @property
    def turn_id(self) -> str | None:
        return self._turn_id

    def ensure_turn(self) -> str:
        """Return the current turn id, minting one if a turn isn't open yet."""
        if self._turn_id is None:
            self._turn_id = str(uuid.uuid4())
            self._acc = {}
        return self._turn_id

    def record(self, metrics: object) -> None:
        """Fold one `metrics_collected` payload into the current turn."""
        kind = _metric_kind(metrics)
        if kind is None or kind == "vad":
            return

        # STT/EOU are user-driven and open a turn; LLM/TTS only extend an open
        # one (the greeting plays TTS with no preceding user turn).
        if kind in ("stt", "eou"):
            self.ensure_turn()
        if self._turn_id is None:
            return

        if kind == "stt":
            self._put("sttMs", _ms(metrics, "duration", "audio_duration"))
        elif kind == "eou":
            self._put("eouMs", _ms(metrics, "end_of_utterance_delay"))
            self._put("transcriptionDelayMs", _ms(metrics, "transcription_delay"))
        elif kind == "llm":
            self._put("llmTtftMs", _ms(metrics, "ttft"))
            self._put("llmMs", _ms(metrics, "duration"))
        elif kind == "tts":
            self._put("ttsTtfbMs", _ms(metrics, "ttfb"))
            self._put("ttsMs", _ms(metrics, "duration", "audio_duration"))

    def _put(self, key: str, value: int | None) -> None:
        if value is not None:
            self._acc[key] = value

    def flush(self) -> dict[str, Any] | None:
        """End the current turn and return its `turn_metrics` data, or None.

        Always clears turn state so the next turn starts fresh. Returns None
        when no turn is open or nothing was recorded (e.g. a bare
        `listening` transition at session start, or the greeting).
        """
        turn_id = self._turn_id
        acc = self._acc
        self._turn_id = None
        self._acc = {}
        if turn_id is None or not acc:
            return None
        return {"turnId": turn_id, **acc}
