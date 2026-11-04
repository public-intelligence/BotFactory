"""Room and job metadata parsing: the ElevenLabs key must not depend on room.metadata."""

from __future__ import annotations

import json

import pytest

from bot_factory_agent.config import parse_room_metadata

ROOM = json.dumps({"botId": 1, "conversationId": 2, "voiceId": "voice-1", "isResume": True})
LEGACY_ROOM = json.dumps({"botId": 1, "conversationId": 2, "voiceId": "voice-1", "elevenLabsApiKey": "sk-room"})


def test_key_comes_from_job_metadata() -> None:
    meta = parse_room_metadata(ROOM, json.dumps({"elevenLabsApiKey": "  sk-job  "}))

    assert meta.eleven_labs_api_key == "sk-job"
    assert (meta.bot_id, meta.conversation_id, meta.voice_id, meta.is_resume) == (1, 2, "voice-1", True)


def test_job_key_wins_over_room_key() -> None:
    meta = parse_room_metadata(LEGACY_ROOM, json.dumps({"elevenLabsApiKey": "sk-job"}))

    assert meta.eleven_labs_api_key == "sk-job"


@pytest.mark.parametrize("job", [None, "", "   ", "not json", "[]", "{}", '{"elevenLabsApiKey": 5}'])
def test_rooms_from_an_older_api_keep_working(job: str | None) -> None:
    assert parse_room_metadata(LEGACY_ROOM, job).eleven_labs_api_key == "sk-room"


def test_missing_key_fails_as_before() -> None:
    with pytest.raises(ValueError, match="elevenLabsApiKey"):
        parse_room_metadata(ROOM, "")


def test_malformed_job_metadata_is_never_echoed() -> None:
    with pytest.raises(ValueError) as e:
        parse_room_metadata(ROOM, '{"elevenLabsApiKey": "sk-secret')

    assert "sk-secret" not in str(e.value)


def test_vocabulary_comes_from_room_metadata_cleaned() -> None:
    room = json.dumps(
        {
            "botId": 1,
            "conversationId": 2,
            "voiceId": "voice-1",
            "transcriptionVocabulary": ["Aurora", " Punto  Ciudadano ", "", 7, "aurora", "x" * 40],
        }
    )

    meta = parse_room_metadata(room, json.dumps({"elevenLabsApiKey": "sk-job"}))

    assert meta.transcription_vocabulary == ["Aurora", "Punto Ciudadano"]


def test_rooms_without_a_vocabulary_get_an_empty_one() -> None:
    meta = parse_room_metadata(ROOM, json.dumps({"elevenLabsApiKey": "sk-job"}))

    assert meta.transcription_vocabulary == []
