"""Fire-and-forget relays: HTTP to .NET, data channel to browser (parity with agent-js).

Both relays schedule asyncio tasks on the running loop (the LiveKit job thread); no queues or retries.
"""

from __future__ import annotations

import asyncio
import json
import logging

import httpx
from livekit import rtc

from bot_factory_agent.types import AgentEventPayload

logger = logging.getLogger("relays")


def _telemetry_url(backend_url: str) -> str:
    # All agent→.NET telemetry uses the /livekit prefix (not /api).
    return f"{backend_url.rstrip('/')}/livekit/telemetry/events"


def agent_auth_headers(shared_secret: str) -> dict[str, str]:
    # Matches the .NET AgentCallbackOptions.HeaderName gate on /livekit/*.
    return {"X-Agent-Secret": shared_secret} if shared_secret else {}


class BackendRelay:
    def __init__(self, *, backend_url: str, shared_secret: str = "") -> None:
        self._url = _telemetry_url(backend_url)
        self._headers = agent_auth_headers(shared_secret)

    def send(self, event: AgentEventPayload) -> None:
        payload = event.to_json_dict()
        event_type = event.event_type

        async def _post() -> None:
            # Short-lived client per call keeps shutdown simple; failures are logged only.
            try:
                timeout = httpx.Timeout(5.0, connect=5.0)
                async with httpx.AsyncClient(timeout=timeout) as client:
                    await client.post(self._url, json=payload, headers=self._headers)
            except Exception as e:
                logger.warning(
                    "telemetry send failed",
                    extra={"eventType": event_type, "error": str(e)},
                )

        try:
            loop = asyncio.get_running_loop()
        except RuntimeError:
            logger.warning(
                "no running loop, telemetry dropped",
                extra={"eventType": event_type},
            )
            return
        loop.create_task(_post())


class ClientRelay:
    def __init__(self, *, room: rtc.Room) -> None:
        self._room = room

    def send(self, event: AgentEventPayload) -> None:
        data = json.dumps(event.to_json_dict()).encode("utf-8")

        async def _publish() -> None:
            lp = self._room.local_participant
            if lp is None:
                logger.warning(
                    "no local participant, event dropped",
                    extra={"eventType": event.event_type},
                )
                return
            try:
                # Angular subscribes to topic "agent" on the LiveKit data channel.
                await lp.publish_data(data, reliable=True, topic="agent")
            except Exception as e:
                logger.warning(
                    "publish failed",
                    extra={"eventType": event.event_type, "error": str(e)},
                )

        try:
            loop = asyncio.get_running_loop()
        except RuntimeError:
            logger.warning(
                "no running loop, client event dropped",
                extra={"eventType": event.event_type},
            )
            return
        loop.create_task(_publish())
