"""Unit tests for BackendLLM helpers and SSE streaming (mocked httpx)."""

from __future__ import annotations

import asyncio
import json
from typing import Any

from livekit.agents import llm

from bot_factory_agent.llm_backend import (
    EMPTY_USER_TEXT,
    FALLBACK_MESSAGE,
    BackendLLM,
    _chat_turn_url,
    _last_user_text,
    _parse_sse_line,
)


def test_chat_turn_url_strips_trailing_slash() -> None:
    assert _chat_turn_url("http://backend:5000/") == "http://backend:5000/livekit/chat/turn"
    assert _chat_turn_url("http://backend:5000") == "http://backend:5000/livekit/chat/turn"


def test_parse_sse_line() -> None:
    assert _parse_sse_line("") == (None, None)
    assert _parse_sse_line(": comment") == (None, None)
    assert _parse_sse_line("event: provider_error") == ("provider_error", None)
    assert _parse_sse_line("data: hello") == (None, "hello")
    assert _parse_sse_line("data:  spaced") == (None, "spaced")
    assert _parse_sse_line("other") == (None, None)


def test_last_user_text() -> None:
    ctx = llm.ChatContext()
    assert _last_user_text(ctx) == EMPTY_USER_TEXT

    ctx.add_message(role="assistant", content="hi")
    ctx.add_message(role="user", content="pregunta")
    assert _last_user_text(ctx) == "pregunta"


class _StreamResponse:
    def __init__(self, status_code: int, chunks: list[bytes]) -> None:
        self.status_code = status_code
        self._chunks = chunks

    async def aiter_bytes(self):
        for c in self._chunks:
            yield c

    async def __aenter__(self):
        return self

    async def __aexit__(self, *args):
        return False


def test_backend_llm_stream_emits_deltas() -> None:
    async def _run() -> list[str]:
        backend = BackendLLM(
            backend_url="http://backend",
            conversation_id=42,
            turn_id_provider=lambda: "turn-fixed",
            shared_secret="sec",
        )

        sse = b"data: Hola\n\ndata:  mundo\n\n"
        stream_resp = _StreamResponse(200, [sse])

        def fake_stream(*args, **kwargs):  # type: ignore[no-untyped-def]
            assert kwargs["json"]["conversationId"] == 42
            assert kwargs["json"]["turnId"] == "turn-fixed"
            assert kwargs["headers"]["X-Agent-Secret"] == "sec"
            return stream_resp

        backend._client.stream = fake_stream  # type: ignore[method-assign]

        ctx = llm.ChatContext()
        ctx.add_message(role="user", content="hola")
        stream = backend.chat(chat_ctx=ctx)

        chunks: list[str] = []
        async for event in stream:
            if isinstance(event, llm.ChatChunk) and event.delta and event.delta.content:
                chunks.append(event.delta.content)

        await backend.aclose()
        return chunks

    assert asyncio.run(_run()) == ["Hola", "mundo"]


def test_backend_llm_http_error_uses_fallback() -> None:
    async def _run() -> list[str]:
        backend = BackendLLM(backend_url="http://backend", conversation_id=1)
        stream_resp = _StreamResponse(500, [b""])
        backend._client.stream = lambda *a, **k: stream_resp  # type: ignore[method-assign]

        ctx = llm.ChatContext()
        ctx.add_message(role="user", content="x")
        stream = backend.chat(chat_ctx=ctx)

        texts = []
        async for event in stream:
            if isinstance(event, llm.ChatChunk) and event.delta and event.delta.content:
                texts.append(event.delta.content)

        await backend.aclose()
        return texts

    assert asyncio.run(_run()) == [FALLBACK_MESSAGE]


def test_backend_llm_provider_error_callback() -> None:
    async def _run() -> list[dict[str, Any]]:
        seen: list[dict[str, Any]] = []
        backend = BackendLLM(
            backend_url="http://backend",
            conversation_id=1,
            on_provider_error=seen.append,
        )
        payload = json.dumps(
            {
                "errorCode": "gemini_quota_exceeded",
                "userMessage": "unavailable",
                "operatorMessage": "quota",
            }
        )
        sse = f"event: provider_error\ndata: {payload}\n\n".encode()
        stream_resp = _StreamResponse(200, [sse])
        backend._client.stream = lambda *a, **k: stream_resp  # type: ignore[method-assign]

        ctx = llm.ChatContext()
        ctx.add_message(role="user", content="x")
        stream = backend.chat(chat_ctx=ctx)
        async for _ in stream:
            pass

        await backend.aclose()
        return seen

    seen = asyncio.run(_run())
    assert len(seen) == 1
    assert seen[0]["errorCode"] == "gemini_quota_exceeded"
