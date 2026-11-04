"""The bot's transcription vocabulary: what is sent to the provider, and how."""

from __future__ import annotations

from typing import Any
from urllib.parse import parse_qs, urlsplit

from bot_factory_agent.keyterms import (
    MAX_KEYTERM_CHARS,
    MAX_KEYTERMS,
    KeytermsSession,
    clean_keyterms,
    with_keyterms,
)

REALTIME = (
    "wss://api.elevenlabs.io/v1/speech-to-text/realtime"
    "?model_id=scribe_v2_realtime&encoding=pcm_16000&commit_strategy=manual&language_code=spa"
)


def test_clean_keyterms_keeps_typical_terms() -> None:
    terms = ["Aurora", "ABCD", "XYZ", "DEFG", "Avanza", "Punto Ciudadano", "Quisqueya", "ZETA"]

    assert clean_keyterms(terms) == terms


def test_clean_keyterms_drops_what_the_provider_would_refuse() -> None:
    raw = [
        "Aurora",
        "",
        "   ",
        5,
        None,
        "x" * (MAX_KEYTERM_CHARS + 1),
        "con\x00control",
        "  Punto   Ciudadano  ",
        "punto ciudadano",
        "AURORA",
    ]

    # Blanks, non-strings, over-long terms and control characters go; spaces are collapsed;
    # repeats are dropped whatever their case, keeping the first spelling.
    assert clean_keyterms(raw) == ["Aurora", "Punto Ciudadano"]


def test_clean_keyterms_takes_the_longest_term_allowed_and_no_more_than_the_limit() -> None:
    longest = "x" * MAX_KEYTERM_CHARS
    many = [f"término{i}" for i in range(MAX_KEYTERMS + 10)]

    assert clean_keyterms([longest]) == [longest]
    assert len(clean_keyterms(many)) == MAX_KEYTERMS
    assert clean_keyterms(many)[0] == "término0"


def test_clean_keyterms_of_anything_but_a_list_is_empty() -> None:
    for raw in (None, "Aurora", {"a": 1}, 3, ()):
        assert clean_keyterms(raw) == []


def test_with_keyterms_adds_one_parameter_per_term_encoded() -> None:
    url = with_keyterms(REALTIME, ["Canción", "Punto Ciudadano", "ABCD"])

    query = parse_qs(urlsplit(url).query)
    assert query["keyterms"] == ["Canción", "Punto Ciudadano", "ABCD"]
    # What was already there stays.
    assert query["model_id"] == ["scribe_v2_realtime"]
    assert query["language_code"] == ["spa"]
    assert "Punto%20Ciudadano" in url
    assert "Canci%C3%B3n" in url
    assert " " not in url


def test_with_keyterms_does_not_touch_other_endpoints_or_an_empty_vocabulary() -> None:
    batch = "https://api.elevenlabs.io/v1/speech-to-text"

    assert with_keyterms(batch, ["Aurora"]) == batch
    assert with_keyterms(REALTIME, []) == REALTIME


def test_with_keyterms_starts_the_query_when_there_is_none() -> None:
    assert with_keyterms("wss://x.test/v1/speech-to-text/realtime", ["XYZ"]).endswith("/realtime?keyterms=XYZ")


class _FakeSession:
    """What the plugin needs of an aiohttp session: ws_connect, closed, and the rest."""

    closed = False

    def __init__(self) -> None:
        self.calls: list[tuple[str, dict[str, Any]]] = []

    def ws_connect(self, url: str, **kwargs: Any) -> str:
        self.calls.append((url, kwargs))
        return "socket"

    def post(self) -> str:
        return "posted"


def test_the_wrapped_session_adds_the_vocabulary_to_the_realtime_socket_only() -> None:
    inner = _FakeSession()
    session = KeytermsSession(inner, ["Aurora", "XYZ"])

    result = session.ws_connect(REALTIME, headers={"xi-api-key": "k"})

    assert result == "socket"
    (url, kwargs), = inner.calls
    assert parse_qs(urlsplit(url).query)["keyterms"] == ["Aurora", "XYZ"]
    assert kwargs == {"headers": {"xi-api-key": "k"}}

    session.ws_connect("wss://elsewhere.test/other")
    assert inner.calls[1][0] == "wss://elsewhere.test/other"


def test_the_wrapped_session_is_the_wrapped_one_for_everything_else() -> None:
    inner = _FakeSession()
    session = KeytermsSession(inner, ["XYZ"])

    assert session.closed is False
    assert session.post() == "posted"
    inner.closed = True
    assert session.closed is True


def test_every_reconnect_carries_the_vocabulary() -> None:
    inner = _FakeSession()
    session = KeytermsSession(inner, ["Quisqueya"])

    session.ws_connect(REALTIME)
    session.ws_connect(REALTIME)

    assert [parse_qs(urlsplit(u).query)["keyterms"] for u, _ in inner.calls] == [["Quisqueya"], ["Quisqueya"]]
