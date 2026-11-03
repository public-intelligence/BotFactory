# Bot Factory Agent (Python) — Implementation Tour

This document explains **what `apps/agent` does** and **how the Python package is organized**. For historical Node.js code, see [`../agent-js/IMPLEMENTATION_TOUR.md`](../agent-js/IMPLEMENTATION_TOUR.md).

## Role

The agent is a **dumb pipe**:

1. Joins a LiveKit room as an **Agents** worker (`.NET` creates the room and sets **metadata**).
2. Runs **audio**: **Silero VAD** (per-bot) + **multilingual turn detector** → **ElevenLabs STT** (`spa`, ISO 639-3) → **.NET** (`/livekit/chat/turn`, SSE; Gemini with Latin American Spanish system suffix) → **ElevenLabs TTS** (`es`).
3. Forwards **raw session events** as JSON (`AgentEvent` shape) to **.NET** and the **browser** — no business mapping here.

**Not used:** LiveKit Cloud **Inference** (`inference.*`). STT/TTS are **direct ElevenLabs** plugins with the **same** `elevenLabsApiKey` from job metadata.

## End-to-end flow

1. Angular calls .NET to start a conversation; .NET creates the LiveKit room (metadata + agent dispatch), returns URL/token to the client.
2. LiveKit dispatches a job to this worker; the worker parses **`room.metadata`** and **`job.metadata`** JSON.
3. User speaks → VAD + STT + turn detector commit turn → last user text POSTed to .NET (pooled `httpx` per job) → SSE deltas → TTS → audio to the room. Brief disconnects do not end the session (`close_on_disconnect=False`).
4. Session hooks emit **`AgentEvent`** payloads (`eventType`, `botId`, `conversationId`, `timestamp`, optional `data`) to HTTP and/or the data channel.

## Job metadata (JSON string)

Secrets for the agent. Set by .NET on the agent dispatch (`RoomAgentDispatch.Metadata`); LiveKit hands it only to the dispatched worker as `ctx.job.metadata`.

| Field | Type | Notes |
|-------|------|--------|
| `elevenLabsApiKey` | string | Used for **both** STT and TTS |

## Room metadata (JSON string)

LiveKit delivers `room.metadata` to **every participant**, anonymous visitors included, so it must never carry secrets. Same contract as **agent-js** (`parse_room_metadata` / `config.py`):

| Field | Type | Notes |
|-------|------|--------|
| `botId` | number | |
| `conversationId` | number | Sent to .NET on chat turns |
| `voiceId` | string | ElevenLabs voice |

**Locale (fixed, not in metadata):** STT uses ISO 639-3 `spa`; TTS uses `es` (`ELEVENLABS_STT_LANGUAGE_CODE` / `ELEVENLABS_TTS_LANGUAGE` in `agent.py`). LLM response language is set in .NET via `SpeechLanguagePrompt.BuildSystemInstruction` on each `/livekit/chat/turn` call.
| `noSpeechTimeoutMs` | number | Optional; default 30000 |
| `maxUtteranceMs` | number | Optional; default 60000 |
| `enableBargeIn` | boolean | Optional; default true |

## Package layout

| Path | Purpose |
|------|---------|
| [`src/bot_factory_agent/__main__.py`](src/bot_factory_agent/__main__.py) | `python -m bot_factory_agent` → health server + `cli.run_app` |
| [`src/bot_factory_agent/agent.py`](src/bot_factory_agent/agent.py) | `AgentServer` (memory/load tuning for 2 GB ACI), prewarm turn-detector weights, per-bot VAD cache, `MultilingualModel` per session, `RoomInputOptions`, `rtc_session`, event wiring, timers, barge-in |
| [`src/bot_factory_agent/config.py`](src/bot_factory_agent/config.py) | Env + `load_worker_server_config` (`AGENT_*`) + `parse_room_metadata` / `dev_room_metadata` |
| [`src/bot_factory_agent/types.py`](src/bot_factory_agent/types.py) | `RoomMetadata`, `AgentConfig`, `WorkerServerConfig`, `AgentEventPayload` (camelCase JSON) |
| [`src/bot_factory_agent/health.py`](src/bot_factory_agent/health.py) | Threaded `GET /healthz` |
| [`src/bot_factory_agent/relays.py`](src/bot_factory_agent/relays.py) | `BackendRelay` (fire-and-forget `httpx` async POST), `ClientRelay` (`publish_data`, topic `agent`) |
| [`src/bot_factory_agent/logging_config.py`](src/bot_factory_agent/logging_config.py) | JSON lines to stdout by default; `LOG_FORMAT=text` for human-readable |
| [`src/bot_factory_agent/llm_backend.py`](src/bot_factory_agent/llm_backend.py) | Subclass `llm.LLM` / `LLMStream`; one `httpx.AsyncClient` per job (`aclose` on shutdown); SSE from `/livekit/chat/turn`; Spanish fallbacks on HTTP/SSE failure |

## .NET endpoints

- `POST {BACKEND_URL}/livekit/chat/turn` — body `{ conversationId, text }`, response `text/event-stream`, lines `data: ...`
- `POST {BACKEND_URL}/livekit/telemetry/events` — JSON `AgentEvent`

## Docker

[`Dockerfile`](Dockerfile): `python:3.12-slim-trixie`, `uv sync`, `bot_factory_agent download-files` (turn-detector weights), `CMD uv run python -m bot_factory_agent start`.
