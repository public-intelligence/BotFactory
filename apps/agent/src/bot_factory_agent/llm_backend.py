"""Custom LLM: POST /livekit/chat/turn, stream SSE deltas (parity with agent-js/llm-adapter.ts)."""

from __future__ import annotations

import json
import logging
import uuid
from collections.abc import Callable
from typing import Any

import httpx
from livekit.agents import llm
from livekit.agents.types import DEFAULT_API_CONNECT_OPTIONS, NOT_GIVEN, APIConnectOptions

from bot_factory_agent.relays import agent_auth_headers

logger = logging.getLogger("backend-llm")

# Pushed into the voice pipeline on HTTP/SSE failure so TTS still speaks instead of silence.
# Matches .NET GeminiChatOptions.FallbackMessage.
FALLBACK_MESSAGE = "Lo siento, no pude procesar eso."

EMPTY_USER_TEXT = "No te escuché."

_CHAT_TURN_TIMEOUT = httpx.Timeout(30.0, connect=15.0)

ProviderErrorCallback = Callable[[dict[str, Any]], None]


def _chat_turn_url(backend_url: str) -> str:
    return f"{backend_url.rstrip('/')}/livekit/chat/turn"


def _parse_sse_line(line: str) -> tuple[str | None, str | None]:
    # .NET emits standard SSE: optional comments (:), `event:` and `data:` lines.
    trimmed = line.rstrip("\r")
    if not trimmed or trimmed.startswith(":"):
        return None, None
    if trimmed.startswith("event:"):
        return trimmed[6:].lstrip(), None
    if trimmed.startswith("data:"):
        return None, trimmed[5:].lstrip()
    return None, None


def _last_user_text(chat_ctx: llm.ChatContext) -> str:
    # Conversation history is authoritative in .NET; we only forward the latest user utterance.
    for item in reversed(chat_ctx.items):
        if isinstance(item, llm.ChatMessage) and item.role == "user":
            return item.text_content or EMPTY_USER_TEXT
    return EMPTY_USER_TEXT


class BackendLLM(llm.LLM):
    def __init__(
        self,
        *,
        backend_url: str,
        conversation_id: int,
        turn_id_provider: Callable[[], str] | None = None,
        on_provider_error: ProviderErrorCallback | None = None,
        shared_secret: str = "",
    ) -> None:
        super().__init__()
        self._backend_url = backend_url
        self._shared_secret = shared_secret
        self._conversation_id = conversation_id
        # Shared with the turn tracker so agent pipeline metrics and the backend
        # turn land under the same id. Falls back to a fresh id per call.
        self._turn_id_provider = turn_id_provider or (lambda: str(uuid.uuid4()))
        self._on_provider_error = on_provider_error
        self._client = httpx.AsyncClient(timeout=_CHAT_TURN_TIMEOUT)
        self._closed = False

    @property
    def model(self) -> str:
        return "backend-sse"

    @property
    def provider(self) -> str:
        return "dotnet"

    async def aclose(self) -> None:
        if self._closed:
            return
        self._closed = True
        await self._client.aclose()

    def chat(
        self,
        *,
        chat_ctx: llm.ChatContext,
        tools: list[llm.Tool] | None = None,
        conn_options: APIConnectOptions = DEFAULT_API_CONNECT_OPTIONS,
        parallel_tool_calls: Any = NOT_GIVEN,
        tool_choice: Any = NOT_GIVEN,
        extra_kwargs: Any = NOT_GIVEN,
    ) -> llm.LLMStream:
        # NOT_GIVEN / unused kwargs exist to satisfy the livekit-agents LLM.chat signature.
        return BackendLLMStream(
            self,
            chat_ctx=chat_ctx,
            tools=tools or [],
            conn_options=conn_options,
        )


class BackendLLMStream(llm.LLMStream):
    def __init__(
        self,
        backend: BackendLLM,
        *,
        chat_ctx: llm.ChatContext,
        tools: list[llm.Tool],
        conn_options: APIConnectOptions,
    ) -> None:
        super().__init__(backend, chat_ctx=chat_ctx, tools=tools, conn_options=conn_options)
        self._backend_llm = backend

    def _notify_provider_error(self, payload: dict[str, Any]) -> None:
        cb = self._backend_llm._on_provider_error
        if cb is None:
            return
        try:
            cb(payload)
        except Exception as e:
            logger.warning("provider_error callback failed: %s", e)

    async def _run(self) -> None:
        text = _last_user_text(self._chat_ctx)
        url = _chat_turn_url(self._backend_llm._backend_url)
        conv_id = self._backend_llm._conversation_id
        # One id per turn (this _run is exactly one user→agent turn). The backend
        # stamps every telemetry event it emits for this turn with the same id so
        # the feed can group them into a per-turn waterfall. The provider shares
        # the id with the agent's pipeline-metrics tracker.
        turn_id = self._backend_llm._turn_id_provider()

        logger.info(
            "backend chat turn request url=%s conversation_id=%s turn_id=%s text_len=%s",
            url,
            conv_id,
            turn_id,
            len(text),
        )

        def _fallback_chunk() -> None:
            self._event_ch.send_nowait(
                llm.ChatChunk(
                    id=str(uuid.uuid4()),
                    delta=llm.ChoiceDelta(role="assistant", content=FALLBACK_MESSAGE),
                )
            )

        try:
            async with self._backend_llm._client.stream(
                "POST",
                url,
                headers={
                    "Content-Type": "application/json",
                    **agent_auth_headers(self._backend_llm._shared_secret),
                },
                json={"conversationId": conv_id, "text": text, "turnId": turn_id},
            ) as response:
                if response.status_code != 200:
                    logger.error(
                        "chat turn HTTP error status=%s url=%s",
                        response.status_code,
                        url,
                    )
                    _fallback_chunk()
                    return

                buffer = ""
                current_event: str | None = None
                async for chunk in response.aiter_bytes():
                    buffer += chunk.decode(errors="replace")
                    lines = buffer.split("\n")
                    # Last segment may be an incomplete line; keep it until the next chunk.
                    buffer = lines.pop() if lines else ""
                    for line in lines:
                        event_name, payload = _parse_sse_line(line)
                        if event_name is not None:
                            current_event = event_name
                            continue
                        if payload is None:
                            continue

                        if current_event == "provider_error":
                            try:
                                data = json.loads(payload)
                                if isinstance(data, dict):
                                    logger.error(
                                        "backend provider_error code=%s operator=%s",
                                        data.get("errorCode"),
                                        data.get("operatorMessage"),
                                    )
                                    self._notify_provider_error(data)
                            except json.JSONDecodeError:
                                logger.warning("malformed provider_error SSE payload")
                            current_event = None
                            continue

                        current_event = None
                        self._event_ch.send_nowait(
                            llm.ChatChunk(
                                id=str(uuid.uuid4()),
                                delta=llm.ChoiceDelta(role="assistant", content=payload),
                            )
                        )

                if buffer:
                    event_name, payload = _parse_sse_line(buffer)
                    if payload is not None and event_name != "provider_error":
                        self._event_ch.send_nowait(
                            llm.ChatChunk(
                                id=str(uuid.uuid4()),
                                delta=llm.ChoiceDelta(role="assistant", content=payload),
                            )
                        )
        except Exception as e:
            logger.error("chat turn failed url=%s error=%s", url, e)
            _fallback_chunk()
