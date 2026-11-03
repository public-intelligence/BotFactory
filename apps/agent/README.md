# Bot Factory voice agent (Python)

Production LiveKit **Agents** worker: **Silero VAD** (per-bot, local), **multilingual turn detector** (semantic end-of-turn), **ElevenLabs STT + TTS** (same API key from job metadata), custom **HTTP+SSE "LLM"** to .NET (`POST /livekit/chat/turn`), and **raw `AgentEvent` JSON** to .NET (`POST /livekit/telemetry/events`) and the browser (data channel topic `agent`).

## Run locally

Requires **Python 3.11+**.

```bash
cd apps/agent
uv sync
uv run python -m bot_factory_agent download-files   # once: turn-detector model weights
cp .env.example .env   # fill LIVEKIT_* and BACKEND_URL
uv run python -m bot_factory_agent dev
```

Use `start` instead of `dev` for production-style worker registration.

## Docker

See [`Dockerfile`](Dockerfile). Compose: root [`docker-compose.dev.yml`](../../docker-compose.dev.yml) service `agent`.

## Docs

- [`IMPLEMENTATION_TOUR.md`](IMPLEMENTATION_TOUR.md) — architecture and file map
