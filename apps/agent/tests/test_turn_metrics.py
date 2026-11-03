"""Unit tests for TurnMetricsTracker (no livekit import required)."""

from __future__ import annotations

from bot_factory_agent.turn_metrics import TurnMetricsTracker


# Fakes named to match livekit metric classes; _metric_kind classifies by name.
class STTMetrics:
    duration = 0.25


class EOUMetrics:
    end_of_utterance_delay = 0.08
    transcription_delay = 0.30


class LLMMetrics:
    ttft = 0.21
    duration = 1.38


class TTSMetrics:
    ttfb = 0.18
    duration = 0.54


class VADMetrics:
    inference_duration_total = 0.001


def test_greeting_tts_only_does_not_open_or_flush_a_turn() -> None:
    t = TurnMetricsTracker()
    t.record(TTSMetrics())
    t.record(VADMetrics())
    assert t.turn_id is None
    assert t.flush() is None


def test_full_turn_aggregates_and_shares_id() -> None:
    t = TurnMetricsTracker()
    t.record(EOUMetrics())
    tid = t.turn_id
    assert tid is not None
    # The backend chat-turn call reuses the open id.
    assert t.ensure_turn() == tid
    t.record(STTMetrics())
    t.record(LLMMetrics())
    t.record(TTSMetrics())

    data = t.flush()
    assert data == {
        "turnId": tid,
        "eouMs": 80,
        "transcriptionDelayMs": 300,
        "sttMs": 250,
        "llmTtftMs": 210,
        "llmMs": 1380,
        "ttsTtfbMs": 180,
        "ttsMs": 540,
    }
    assert t.turn_id is None  # flush resets


def test_next_turn_mints_a_fresh_id() -> None:
    t = TurnMetricsTracker()
    t.record(STTMetrics())
    first = t.turn_id
    t.flush()
    t.record(STTMetrics())
    assert t.turn_id is not None
    assert t.turn_id != first


def test_vad_is_ignored() -> None:
    t = TurnMetricsTracker()
    t.record(VADMetrics())
    assert t.turn_id is None
