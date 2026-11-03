"""Unit tests for BackendRelay / ClientRelay / agent_auth_headers."""

from __future__ import annotations

import asyncio
from typing import Any
from unittest.mock import AsyncMock, MagicMock, patch

import httpx

from bot_factory_agent.relays import BackendRelay, ClientRelay, agent_auth_headers
from bot_factory_agent.types import AgentEventPayload


def test_agent_auth_headers() -> None:
    assert agent_auth_headers("") == {}
    assert agent_auth_headers("secret") == {"X-Agent-Secret": "secret"}


def test_backend_relay_posts_json() -> None:
    posted: dict[str, Any] = {}

    async def fake_post(self, url, json=None, headers=None):  # type: ignore[no-untyped-def]
        posted["url"] = url
        posted["json"] = json
        posted["headers"] = headers
        return httpx.Response(204)

    async def _run() -> None:
        event = AgentEventPayload("TurnStarted", 1, 2, "2026-01-01T00:00:00Z", {"turnId": "t1"})
        relay = BackendRelay(backend_url="http://backend:5000/", shared_secret="sec")

        with patch.object(httpx.AsyncClient, "post", fake_post):
            relay.send(event)
            await asyncio.sleep(0.05)

    asyncio.run(_run())
    assert posted["url"] == "http://backend:5000/livekit/telemetry/events"
    assert posted["json"]["eventType"] == "TurnStarted"
    assert posted["headers"] == {"X-Agent-Secret": "sec"}


def test_backend_relay_swallows_http_errors() -> None:
    async def boom(self, url, json=None, headers=None):  # type: ignore[no-untyped-def]
        raise httpx.ConnectError("down")

    async def _run() -> None:
        relay = BackendRelay(backend_url="http://backend", shared_secret="")
        event = AgentEventPayload("Error", 1, 2, "2026-01-01T00:00:00Z")
        with patch.object(httpx.AsyncClient, "post", boom):
            relay.send(event)
            await asyncio.sleep(0.05)

    asyncio.run(_run())


def test_client_relay_publishes_on_agent_topic() -> None:
    async def _run() -> MagicMock:
        lp = MagicMock()
        lp.publish_data = AsyncMock()
        room = MagicMock()
        room.local_participant = lp

        relay = ClientRelay(room=room)
        event = AgentEventPayload("Close", 9, 8, "2026-01-01T00:00:00Z", {"reason": "user_left"})
        relay.send(event)
        await asyncio.sleep(0.05)
        return lp

    lp = asyncio.run(_run())
    lp.publish_data.assert_awaited()
    args, kwargs = lp.publish_data.await_args
    assert kwargs["topic"] == "agent"
    assert kwargs["reliable"] is True
    assert b"Close" in args[0]


def test_client_relay_drops_when_no_local_participant() -> None:
    async def _run() -> None:
        room = MagicMock()
        room.local_participant = None
        relay = ClientRelay(room=room)
        relay.send(AgentEventPayload("Error", 1, 2, "2026-01-01T00:00:00Z"))
        await asyncio.sleep(0.05)

    asyncio.run(_run())
