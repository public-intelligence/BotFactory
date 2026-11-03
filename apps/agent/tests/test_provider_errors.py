"""Tests for provider error classification (parity with C# ProviderErrorClassifier)."""

from bot_factory_agent.provider_errors import (
    CODE_ELEVENLABS_QUOTA_EXCEEDED,
    CODE_GEMINI_QUOTA_EXCEEDED,
    USER_MESSAGE_UNAVAILABLE,
    classify,
    classify_gemini,
)


def test_classify_gemini_quota_429() -> None:
    info = classify_gemini(
        '{"error":{"status":"RESOURCE_EXHAUSTED"}}',
        http_status=429,
    )
    assert info.error_code == CODE_GEMINI_QUOTA_EXCEEDED
    assert info.provider == "gemini"
    assert "cuota" in info.operator_message.lower()
    assert info.user_message == USER_MESSAGE_UNAVAILABLE


def test_classify_elevenlabs_tts_quota() -> None:
    info = classify(
        {"label": "tts-elevenlabs"},
        "You have insufficient credits",
        http_status=402,
    )
    assert info.error_code == CODE_ELEVENLABS_QUOTA_EXCEEDED
    assert info.stage == "tts"
    assert "ElevenLabs" in info.operator_message


def test_classify_stt_source() -> None:
    info = classify("stt-elevenlabs", "quota exceeded for this month")
    assert info.stage == "stt"
    assert info.error_code == CODE_ELEVENLABS_QUOTA_EXCEEDED
