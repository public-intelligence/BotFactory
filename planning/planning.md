# Bot Factory — Planning (Explicit Module Responsibilities)

## Stack

- ASP.NET 10 (Minimal APIs with MapGroup, EF Core + SQL Server 2025)
- Angular 21 (standalone components, signals, Angular Router)
- Tailwind CSS 4 (via Core-UI component library)
- LiveKit (self-hosted WebRTC SFU + Node.js voice agent)
- STT: Deepgram Nova-3 (default, swappable per bot)
- TTS: ElevenLabs (default, swappable per bot — Cartesia, Deepgram also available)
- Gemini LLM (via Microsoft.Extensions.AI / IChatClient)
- GOB-ID (authentication + authorization, external OIDC)
- Hangfire (background jobs)
- S3-compatible blob storage (Garage docker image for dev)
- Redis (LiveKit Server state)
- SignalR (text chat streaming + admin events)
- Docker Compose (local dev: SQL Server + Redis + LiveKit Server + Node.js Agent + Garage S3)

## Voice Pipeline Architecture

Uses LiveKit with a Node.js voice agent sidecar (see responsibilities.md for full architecture).

Three processes: LiveKit Server (WebRTC SFU, Docker), Node.js Agent (thin audio bridge — STT + TTS only), and .NET Backend (all orchestration and business logic).

### Agent as Audio Bridge

The Node.js agent is intentionally thin (~50 LOC). It handles ONLY audio I/O:

1. Subscribes to user's microphone track
2. Runs STT (Deepgram) → produces transcribed text
3. Sends transcribed text to .NET backend via `POST /api/chat/turn`
4. Receives streamed text response from .NET backend (SSE)
5. Pipes response text through TTS (ElevenLabs) → publishes audio back to room
6. Sends transcriptions + metadata to browser via LiveKit data channel

The agent does NOT handle LLM calls, RAG, tool execution, or any business logic. It is purely an audio-to-text and text-to-audio bridge.

### Chat Turn Endpoint

The .NET backend exposes `POST /api/chat/turn` (NOT an OpenAI-compatible endpoint):

```
Request:  { conversationId, text }
Response: SSE stream of plain text chunks
```

The backend receives transcribed text, loads conversation context + bot params, runs RAG (embed query → vector search → top-k), builds the full prompt, calls Gemini (streaming), executes any tool calls internally, and streams back the final response text. All orchestration lives in .NET.

This same endpoint is used by both the voice agent and the SignalR text chat hub — one pipeline, two entry points.

### Flow

Browser connects to LiveKit room via `livekit-client` JS SDK. User publishes microphone track. LiveKit Server dispatches the Node.js agent. Agent runs STT on user audio, sends text to .NET backend, receives streamed response, runs TTS, publishes audio back. Transcriptions sent to browser via data channel.

Bot runtime params (voice_id, STT/TTS provider, VAD settings) are passed to the agent via LiveKit room metadata. LLM/RAG params (master_prompt, top_k, grounded_only_message) stay in the backend — the agent never sees them.

For text chat: Angular connects directly to SignalR hub, which calls the same `POST /api/chat/turn` pipeline internally. No STT/TTS involved.

When in voice mode: transcriptions appear in the chat panel in real-time (via data channel). When users type in chat: bot responds with text only (no TTS).

---

### Bot Management

Bot lifecycle and runtime configuration.

Responsibilities:

- Bot CRUD.
- Bot admin UI (Angular).
- Draft / Published state management.
- Bot publication (generates public URL via url_slug).
- Runtime parameter configuration:
  - master_prompt
  - voice_id (voice catalog per TTS provider)
  - stt_provider (Deepgram default, swappable)
  - tts_provider (ElevenLabs default, swappable)
  - top_k
  - silence_end_ms
  - no_speech_timeout_ms
  - max_utterance_ms
  - enable_barge_in
  - enable_tts
  - enable_typing_sound
  - enable_text_only_fallback
  - grounded_only_message (configurable "no sé" text)
- Bot testing configuration ("Probar" mode — pre-publication).
- Bot publish / unpublish events.
- Bot runtime settings persistence.
- Bot params:
  - bot_name
  - voice_id
  - url_slug
  - published_state
  - stt_provider (Deepgram, Google, OpenAI, etc.)
  - tts_provider (ElevenLabs, Cartesia, Deepgram, etc.)
- Bot params passed to LiveKit agent via room metadata on session start.

---

### Chat

Voice and chat interaction runtime.

Responsibilities:

- Persistent chat storage:
  - conversations
  - messages (user transcription/text + bot responses)
  - execution metadata (provider, latency, states)
- Chat turn endpoint (`POST /api/chat/turn`):
  - Receives `{ conversationId, text }` from agent or SignalR
  - Loads conversation context + bot runtime params
  - Runs RAG pipeline (embed query → vector search → top-k)
  - Builds full prompt (master_prompt + RAG context + conversation history + user text)
  - Calls Gemini via IChatClient (streaming)
  - Executes tool calls internally (KB retrieval, future integrations)
  - Returns SSE stream of plain text chunks
  - Single pipeline used by both voice agent and text chat
- SignalR hub for text chat:
  - Direct text input from chat panel
  - Calls same chat turn pipeline internally
  - Stream tokens back to client
  - No STT/TTS involved
- LiveKit integration (frontend):
  - `livekit-client` JS SDK room connection (WebRTC)
  - LiveKit token generation from backend (authenticated users only)
  - Voice UI: visualizer, mic controls, "Cerrar" button
  - Subscribe to agent audio track (auto-play)
  - Receive transcriptions + metadata via LiveKit data channel
  - Partial transcripts display in chat panel
- Conversation lifecycle management:
  - conversation_started
  - conversation_ended
- Event emission for all conversation stages.
- Admin test mode ("Probar"):
  - Events streaming (left panel)
  - Active params display
  - Prompt visibility
  - Show tool responses
  - Show stage latencies
  - Retrieved chunks display

---

### RAG

Knowledge base ingestion and retrieval.

Responsibilities:

- Document CRUD:
  - upload .txt only
  - S3-compatible blob storage (Garage in dev)
  - replace content
  - delete
- Chunking strategy.
- Embedding generation (Gemini embeddings via Microsoft.Extensions.AI / IEmbeddingGenerator).
- SQL Server vector storage (SqlVector).
- Vector index management.
- Background Jobs (Hangfire):
  - job queue
  - job runner
  - retry policy
  - job scheduling
  - job logging
- Reindexing pipeline.
- Retrieval system:
  - top-k search (cosine distance)
  - threshold filtering (optional)
- Grounded-only response policy: if no retrieval hit, return configurable grounded_only_message.
- Knowledge base validation.
- KB ingestion events:
  - kb_index_started
  - kb_index_completed
  - kb_index_failed

### Telemetry

Centralized event tracking.

Responsibilities:

- Telemetry event hub (backend service).
- Event emitter library (injectable ITelemetryService).
- Angular telemetry service:
  - `TelemetryService` (injectable)
- Backend telemetry service:
  - `ITelemetryService.EmitEvent()`
- Event ingestion pipeline.
- Event schema validation.
- Computed metrics generation:
  - time_to_first_token
  - time_to_first_audio
  - end_to_end_turn_time
- Event persistence (SQL Server, events table).
- Required events: conversation_started, stt_completed, retrieval_hit, retrieval_miss, llm_error, tts_first_audio, barge_in, network_degraded, kb_index_completed, bot_published, prompt_updated, api_key_updated.
- Recommended events: user_authenticated, conversation_ended, stt_failed, tts_failed, llm_started, llm_first_token, llm_completed, tts_started, tts_completed, fallback_text_only, kb_index_started, kb_index_failed, bot_unpublished.

### UI

User interfaces (Angular + Core-UI + Tailwind).

Responsibilities:

- Public landing page (per bot via url_slug).
- Public bot conversation UI:
  - Voice-first dark full-screen layout (like Taina reference).
  - Logo/bot name top-left.
  - Central audio visualizer indicator.
  - Bottom bar: mic (start/stop), options dropdown, chat button, "Cerrar" button.
  - Chat panel (right side): shows transcriptions + text input.
  - Admin debug panel (left side, admin-only): events, params, latencies.
- Admin portal:
  - Bot configuration screens.
  - Knowledge base management screens.
  - Role and user management screens.
  - Telemetry dashboards.
  - Conversation viewer (transcription + events).
- Mobile-friendly layouts.

## Resolved Questions

- API Keys: configured per provider in App Settings (tenant-scoped), global defaults with per-bot override.
- Users can exist in multiple organizations.
- Voice pipeline: LiveKit with Node.js voice agent sidecar (see responsibilities.md).
- No multiple language support (Spanish only).
- Users must authenticate via GOB-ID to interact; visitors can see landing but not send messages.
- Blob storage: S3-compatible (Garage docker in dev, likely Azure Blob in prod).

