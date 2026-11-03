# Bot Factory — Service Responsibilities

Three services, clear boundaries. The agent is a thin audio bridge. All business logic lives in .NET.

---

## LiveKit Server

WebRTC infrastructure. Knows nothing about the app.

- WebRTC transport (NAT traversal, codec negotiation, jitter buffers, reconnection)
- Room management (create, destroy, track participants)
- Audio/video routing between participants (SFU — Selective Forwarding Unit)
- Agent dispatch (when a user joins a room, assigns an available agent worker)
- UDP/TCP signaling and media relay

**Does not:** run any application logic, call any APIs, know what a "bot" is.

**Runtime:** Docker container (`livekit/livekit-server`), requires Redis for state.

---

## Node.js Agent

Thin audio bridge (~50 LOC). Runs per-session, dispatched by LiveKit Server when a user joins a room. Handles real-time audio coordination that can't live in an HTTP backend.

### Responsibilities

- **Join room** — connects as a participant when dispatched by LiveKit Server
- **Subscribe to user audio** — listens to user's microphone track
- **VAD** — detects when the user starts/stops speaking (turn detection, silence thresholds from room metadata)
- **STT** — feeds user audio to STT plugin (Deepgram Nova-3 default), produces transcribed text
- **Forward text to .NET** — sends transcribed text to `POST /api/chat/turn`, reads SSE response stream
- **Sentence buffering** — splits streamed response into natural chunks for TTS (doesn't wait for full response)
- **TTS** — feeds text chunks to TTS plugin (ElevenLabs default), produces audio
- **Publish audio** — sends TTS audio back to room (user hears it)
- **Barge-in** — if user speaks mid-response, cancels current TTS playback and starts a new turn
- **Data channel** — sends transcriptions + agent responses to browser for display in chat panel
- **Read config** — reads bot params from room metadata (voice_id, STT/TTS provider, VAD thresholds)

### Does NOT handle

- LLM calls (Gemini)
- RAG (embedding, vector search, context injection)
- Tool execution (KB retrieval, future integrations)
- Conversation context or history
- Authentication or authorization
- Bot configuration or management
- Persistence of any kind

### Configuration

All config comes from LiveKit room metadata (set by .NET backend when creating the room):

```json
{
  "conversationId": "uuid",
  "voiceId": "elevenlabs-voice-id",
  "sttProvider": "deepgram",
  "ttsProvider": "elevenlabs",
  "silenceEndMs": 700,
  "enableBargeIn": true
}
```

The agent never sees master_prompt, top_k, grounded_only_message, or any LLM/RAG params. Those stay in .NET.

### Interface with .NET

Single HTTP call:

```
POST /api/chat/turn
Content-Type: application/json

{ "conversationId": "uuid", "text": "transcribed user speech" }

Response: text/event-stream (SSE)
data: "Hello, "
data: "how can "
data: "I help you?"
data: [DONE]
```

Agent yields each SSE chunk to TTS. That's it.

**Runtime:** Docker container (Node.js), ~50 LOC of config + one custom LLM adapter class.

---

## .NET Backend

The brain. All orchestration, all business logic, all persistence.

### Voice Pipeline

- **Chat turn endpoint** (`POST /api/chat/turn`):
  1. Receive `{ conversationId, text }` from agent (or from SignalR for text chat)
  2. Load conversation history from DB
  3. Load bot runtime params (master_prompt, top_k, grounded_only_message)
  4. Run RAG: embed query → SQL Server vector search → top-k retrieval
  5. Build full prompt: master_prompt + RAG context + conversation history + user text
  6. Call Gemini via IChatClient (streaming)
  7. Execute tool calls internally (KB retrieval, future integrations)
  8. Stream plain text response back via SSE
  9. Persist user message + bot response
  10. Emit telemetry events (llm_started, llm_first_token, llm_completed, retrieval_hit/miss)

One pipeline, two entry points: voice agent calls it via HTTP, SignalR text chat calls it internally.

### LiveKit Integration

- **Token generation** — issue LiveKit access tokens for authenticated users (scoped to conversation room)
- **Room creation** — create LiveKit room, set metadata with bot params (voice_id, STT/TTS provider, VAD settings)
- **Room lifecycle** — handle room events via LiveKit webhooks (agent_connected, participant_left, etc.)

### Everything Else

- Authentication (ASP.NET Identity, OAuth Google/Microsoft, GOB-ID)
- RBAC (roles, permissions, tenant-scoped bindings)
- Bot management (CRUD, runtime params, publish/unpublish, url_slug)
- Knowledge base (document CRUD, S3 storage, chunking, embedding, indexing via Hangfire)
- Conversation persistence (conversations, messages, execution metadata)
- Telemetry (event hub, latency metrics, event persistence)
- SignalR hub (text chat — calls same chat turn pipeline, streams tokens to Angular)
- Admin features (debug panel data, prompt visibility, retrieved chunks)

**Runtime:** ASP.NET 10, connects to SQL Server, Redis (optional — for SignalR backplane), S3-compatible storage.

---

## Communication Flow

```
Browser (Angular + livekit-client)
    │
    ├─ WebRTC audio ──────────► LiveKit Server ──────────► Node.js Agent
    │                                                         │
    │                                                    STT (Deepgram)
    │                                                         │
    │                                                    transcribed text
    │                                                         │
    │                                              POST /api/chat/turn
    │                                                         │
    │                                                         ▼
    │                                                   .NET Backend
    │                                                   (RAG + Gemini + tools)
    │                                                         │
    │                                                    SSE text stream
    │                                                         │
    │                                                         ▼
    │                                                   Node.js Agent
    │                                                   (sentence buffer + TTS)
    │                                                         │
    │  ◄──────── WebRTC audio ◄──────── LiveKit Server ◄──────┘
    │
    ├─ Data channel (transcriptions + agent text) ◄─── Node.js Agent
    │
    ├─ HTTP (token, bot config, CRUD) ──────────► .NET Backend
    │
    └─ SignalR (text chat) ──────────► .NET Backend
                                       (same chat turn pipeline, no STT/TTS)
```

---

## Why This Split

- **All business logic in one place** — debug, test, and extend in .NET only
- **Agent is disposable** — crash it, redeploy it, scale it independently, almost never touch the code
- **No OpenAI format shoehorning** — the API contract is yours (`{ conversationId, text }`)
- **One pipeline** — voice and text chat share the same `POST /api/chat/turn` path
- **Agent params vs backend params** — clean separation: agent only knows audio config (voice, STT/TTS provider, VAD), backend owns everything else (prompt, RAG, tools)
