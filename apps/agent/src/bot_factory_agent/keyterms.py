"""The words the speech recogniser should favour for a bot (its transcription vocabulary).

ElevenLabs Scribe v2 realtime takes them as repeated ``keyterms`` query parameters of its
WebSocket URL (up to 50 terms of up to 20 characters, per its changelog of 2026-04-27) and
echoes them back in ``session_started``. ``livekit-plugins-elevenlabs`` 1.5.1 has a
``keyterms`` option, but its own docs say it is for batch recognition only: the realtime
socket is opened without them. The plugin accepts the HTTP session it opens that socket
with, so this module wraps that session and adds the vocabulary to the realtime URL,
without copying or patching the plugin.
"""

from __future__ import annotations

from typing import Any
from urllib.parse import quote

# Provider limits for realtime transcription.
MAX_KEYTERMS = 50
MAX_KEYTERM_CHARS = 20

# The path of the realtime endpoint: the only URL the vocabulary is added to.
REALTIME_PATH = "/speech-to-text/realtime"


def clean_keyterms(raw: Any) -> list[str]:
    """Terms the provider will take, from whatever the room metadata carried.

    A term it would refuse is dropped here, because the provider refuses the whole
    connection, and with it every call of the bot, for one bad word. Keeps strings with
    text, trimmed, within the length limit, without repeats (case aside), at most 50.
    The API validates the same limits when the vocabulary is saved; this is the last line.
    """
    if not isinstance(raw, list):
        return []

    terms: list[str] = []
    seen: set[str] = set()
    for item in raw:
        if not isinstance(item, str):
            continue
        term = " ".join(item.split())
        if not term or len(term) > MAX_KEYTERM_CHARS or not term.isprintable():
            continue
        key = term.casefold()
        if key in seen:
            continue
        seen.add(key)
        terms.append(term)
        if len(terms) == MAX_KEYTERMS:
            break
    return terms


def with_keyterms(url: str, terms: list[str]) -> str:
    """``url`` with one ``keyterms=`` parameter per term, if it is the realtime endpoint."""
    if not terms or REALTIME_PATH not in url:
        return url
    separator = "&" if "?" in url else "?"
    return url + separator + "&".join(f"keyterms={quote(term, safe='')}" for term in terms)


class KeytermsSession:
    """An aiohttp session that adds the bot's vocabulary to the realtime transcription socket.

    Only ``ws_connect`` is changed, and only for the realtime endpoint; every other
    attribute is the wrapped session's own. The plugin calls ``ws_connect`` again on each
    reconnect, so the vocabulary survives them.
    """

    def __init__(self, session: Any, keyterms: list[str]) -> None:
        self._session = session
        self._keyterms = keyterms

    def ws_connect(self, url: Any, *args: Any, **kwargs: Any) -> Any:
        return self._session.ws_connect(with_keyterms(str(url), self._keyterms), *args, **kwargs)

    def __getattr__(self, name: str) -> Any:
        return getattr(self._session, name)
