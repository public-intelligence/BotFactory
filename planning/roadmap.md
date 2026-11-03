# Bot Factory — Implementation Roadmap

Stack: ASP.NET 10 + Angular 21 + SQL Server + LiveKit + Gemini.

**Status (repo):** `🤖` on a line means that the agent checked that there is a real implementation in `apps/` (backend, frontend, or `apps/agent`) that matches the intent, sometimes partial — see _italics_. Sections **4–12** and most of **6–9** are still open unless noted.

`✅` on a line means that a human tested it and marked it as complete / production ready.

---

## 0 — Project Planning

Define architecture and implementation strategy.

Tasks:

- 🤖 ✅ Review PRD
- 🤖 ✅ Define modules
- 🤖 ✅ Define database schema outline
- 🤖 ✅ Define deployment architecture
- 🤖 ✅ Define voice pipeline approach (see responsibilities.md)

Dependencies: None

---

## 1 — App Boilerplate

Create the core application infrastructure.

Tasks:

- 🤖 ✅ Create ASP.NET 10 project (Minimal APIs)
- 🤖 ✅ Create Angular 21 project (standalone components)
- 🤖 ✅ Configure Core-UI + Tailwind CSS 4 (`@public-intelligence/ui-core` + Tailwind in `apps/frontend`)
- 🤖 ✅ Configure EF Core + SQL Server 2025
- 🤖 ✅ Setup Docker Compose (SQL Server + Redis + LiveKit Server + Garage S3)
- 🤖 ✅ Setup initial EF Core migrations
- 🤖 ✅ Configure environment/appsettings (dev/staging/prod)
- 🤖 ✅ Implement multi-tenancy base (tenant_id column, global query filters)
- 🤖 ✅ Create super admin seed
- 🤖 ✅ Configure CORS (Angular dev server <> .NET API)
- 🤖 ✅ Setup solution structure (modular: BotFactory.Api, BotFactory.Core, BotFactory.Infrastructure, etc.)

Dependencies: None

---

## 2 — Gob-ID Authentication & Authorization

Implement GOB-ID integration for authentication and authorization.

Tasks:

- 🤖 Define static list of permissions we'll use on the app
- 🤖 ✅ Implement middleware in dotnet to validate GOB-ID JWT
- 🤖 ✅ Implement services in angular to show/hide elements depending on permissions
- 🤖 ✅ Angular auth interceptor (attach GOB-ID JWT to API calls)
- 🤖 ✅ Angular login redirect to GOB-ID
- 🤖 ✅ Auth guards for routes (visitors see UI but can't interact)
- 🤖 ✅ Session handling (JWT validation on every protected endpoint)
- 🤖 ✅ LiveKit token generation for authenticated users only (room access scoped to conversation)

Gob-ID example JWT payload:

```
{
    "sub": "342e38e6-4ff5-48d4-9401-fafc8fac4e36",
    "personnel_id": "cf200421-2d04-4857-8c61-eed070c1692f",
    "application_id": "e07ff72a-5321-f111-b7f2-6045bdb142ad",
    "organization_application_id": "110",
    "organization_id": "10",
    "organization_name": "Instituto Nacional de Recursos Hídraulicos",
    "permissions": "[\"admin_local\",\"delete_applications\",\"delete_org_app\",\"delete_organizations\",\"delete_user\",\"delete_user_app\",\"edit_applications\",\"edit_org_app\",\"edit_organizations\",\"edit_user\",\"edit_user_app\",\"list_applications\",\"list_org\",\"list_org_app\",\"list_user\",\"list_user_app\",\"super_admin\",\"view_app\",\"view_org\",\"view_user\",\"view_views\"]",
    "exp": 1773847035,
    "iss": "https://gobidapi-ech0bqe4escmabet.eastus-01.azurewebsites.net",
    "aud": "gobid-users"
}
```

Dependencies:

- App Boilerplate (#1)

---

## 3 — Chat MVP

Create minimal working conversational system with LiveKit.

Tasks:

- 🤖 ✅ LiveKit Server + Redis setup in Docker Compose (livekit.yaml config)
- 🤖 ✅ LiveKit voice worker (`apps/agent`, Python + `livekit-agents`)
  - 🤖 ✅ `livekit-agents` worker + Silero VAD
  - 🤖 ✅ ElevenLabs STT + TTS plugins
  - 🤖 ✅ Custom LLM adapter: `POST /livekit/chat/turn` (SSE) on .NET, streamed into TTS
  - 🤖 ✅ Agent handles ONLY audio I/O — no LLM, no RAG, no tool calls
- 🤖 ✅ .NET LiveKit integration:
  - 🤖 ✅ LiveKit token generation (`LiveKitService`, room create)
  - 🤖 ✅ Room management (create room, set metadata with bot params)
- ✅ Chat turn endpoint: `POST /livekit/chat/turn` ({ conversationId, text } → SSE text stream)
  - ✅ Loads conversation context + bot params
  - ✅ Calls Gemini via IChatClient (streaming)
  - ✅ Handles tool calls internally
  - ✅ Single pipeline for both voice agent and SignalR text chat
- ✅ Angular LiveKit integration:
  - 🤖 ✅ `livekit-client` JS SDK (connect to room, publish mic, subscribe to agent audio)
  - 🤖 ✅ Data channel listener (transcriptions + metadata from agent)
- ✅ Voice UI scaffolding (chat screen: connect, status, transcripts)
- SignalR hub for text chat (streaming tokens to Angular, make llm <> browser streaming work)
- ✅ Chat panel UI (right side panel, text input + message display)
- 🤖 ✅ Partial transcript display (from agent data channel → chat panel)
- ✅ Audio visualizer component
- ✅ Mic controls (start/stop/mute)
- 🤖 ✅ "Cerrar" (end session) button
- ✅ Barge-in support (if user speaks during TTS, cut audio and take user turn; emit barge_in event)
- ✅ Loading/thinking feedback (visual indicator + status message while processing; optional typing/keyboard sound via enable_typing_sound toggle)

Constraints:

- No authentication
- No persistence
- No bots (hardcoded config, passed via room metadata)
- No RAG (direct LLM responses)
- Agent uses hardcoded STT/TTS providers

Dependencies: None

---

## 4 — Telemetry Service

Lightweight event ingestion layer — no dashboards, no UI.

Tasks:

- Events table schema (EF Core migration)
- Event fields: event_id, event_name, timestamp, tenant_id, bot_id, conversation_id, user_id, auth_provider, stage, status, latency_ms, provider, error_code, error_message, payload_json
- ITelemetryService (backend injectable, `EmitEvent()`)
- TelemetryService (Angular injectable)
- Event schema validation
- Event persistence (SQL Server)
- Computed metrics: time_to_first_partial_transcript, time_to_final_transcript, time_to_first_token, time_to_first_audio, end_to_end_turn_time
- Required events (PRD RF-23):
  - conversation_started
  - stt_completed
  - retrieval_hit
  - retrieval_miss
  - llm_error
  - tts_first_audio
  - barge_in
  - network_degraded
  - kb_index_completed
  - bot_published
  - prompt_updated
  - api_key_updated
- Recommended events:
  - user_authenticated
  - conversation_ended
  - stt_failed
  - tts_failed
  - llm_started
  - llm_first_token
  - llm_completed
  - tts_started
  - tts_completed
  - fallback_text_only
  - kb_index_started
  - kb_index_failed
  - bot_unpublished

Dependencies:

- App Boilerplate (#1)

---

## 5 — Bot Management & App Settings

Allow admins to create and configure bots and organization-scoped provider settings.

Tasks:

- 🤖 ✅ Bots schema (EF Core migration)
- 🤖 ✅ Bot CRUD API endpoints
- 🤖 ✅ Bot admin UI (Angular)
- 🤖 ✅ Runtime parameters editor (master_prompt, top_k, silence params, toggles)
- 🤖 ✅ Bot publishing state (draft/published)
- 🤖 ✅ Public bot URL generation (url_slug)
- 🤖 ✅ Bot runtime parameter persistence
- ✅ Bot publish / unpublish events (audit log)
- ✅ Bot config dispatch to LiveKit agent via room metadata — _ElevenLabs key + voice still partly hardcoded in `BotEndpoints` create-chat_
- ✅ App Settings schema (EF Core migration)
- ✅ API keys storage per provider (encrypted at rest)
- ✅ App Settings CRUD API (tenant-admin only)
- ✅ App Settings UI (Angular) — _static / placeholder UI in `settings.ts`, no backend persistence_
- ✅ Global defaults with per-bot override
- Audit logging for API key changes
- 🤖 ✅ Configurable grounded_only_message per bot (default response when retrieval misses)

Dependencies:

- Gob-ID Authentication & Authorization (#2)

---

## 6 — Chat Boilerplate

Implement persistent chat system.

Tasks:

- ✅ Conversations schema (EF Core)
- ✅ Messages schema (user/assistant, text, timestamps, latency)
- ✅ Execution metadata storage (provider, latency_ms, states)
- ✅ Chat session initialization (create conversation record)
- ✅ Load bot runtime params into chat turn endpoint
- ✅ Persist conversation messages (from chat turn calls + SignalR text chat)
- ✅ Persist execution metadata
- ✅ Chat API endpoints (list conversations, get messages, etc.)
- Connect SignalR text chat hub to persistence layer

Dependencies:

- Bot Management & App Settings (#5)

---

## 7 — Knowledge Base Module

Implement document management.

Tasks:

- ✅ KB documents schema (EF Core)
- ✅ S3-compatible blob storage integration (Garage in dev)
- ✅ Storage configuration management (connection strings/keys in App Settings)
- ✅ Document CRUD API endpoints (.txt only)
- ✅ Document management UI (Angular — upload, list, edit, delete)
- ✅ Link documents to bots (many-to-many)
- ✅ Manual reindex trigger (API + UI button)

Dependencies:

- Bot Management & App Settings (#5)

---

## 8 — Background Jobs Infrastructure

Support asynchronous pipelines.

Tasks:

- ✅ Hangfire setup (NuGet, SQL Server storage)
- ✅ Hangfire dashboard (admin-only, behind auth)
- ✅ Job scheduling service
- Retry policy configuration
- ✅ Job logging (integrate with telemetry events)
- ✅ Indexing job type (process KB document → chunks → embeddings)

Dependencies:

- Bot Management & App Settings (#5)

---

## 9 — Embeddings and Chunking

Implement document ingestion pipeline.

Tasks:

- ✅ Text chunking logic (split .txt into chunks)
- ✅ Embedding generation (Gemini embeddings via IEmbeddingGenerator)
- ✅ SQL Server vector storage (SqlVector<float>, vector columns)
- ✅ kb_chunks table (EF Core)
- ✅ Indexing jobs (Hangfire: document → chunks → embeddings → store)
- ✅ Reindex pipeline (delete old chunks, re-process)
- KB ingestion events (kb_index_started, kb_index_completed, kb_index_failed)

Dependencies:

- Knowledge Base Module (#7)
- Background Jobs Infrastructure (#8)

---

## 10 — Merge Chat MVP

Integrate chat MVP into full chat system.

Tasks:

- ✅ Replace hardcoded config with bot runtime params in chat turn endpoint
- ✅ Connect voice UI to bot-specific LiveKit room (bot params via room metadata → agent)
- ✅ Connect chat UI to conversation persistence layer
- ✅ Apply bot runtime parameters (master_prompt, voice_id, silence params, toggles)
- ✅ Route public bot URL (/:url_slug) to correct bot config
- ✅ Auth gate: require GOB-ID login to start conversation, visitors see landing only
- ✅ Network handling (reconnection states, jitter handling, network_degraded event)
- Text-only fallback mode (enable_text_only_fallback toggle; fall back to text when audio/network fails, emit fallback_text_only event)

Dependencies:

- Chat MVP (#3)
- Chat Boilerplate (#6)

---

## 11 — Knowledge Retrieval Integration

Connect chat to RAG pipeline.

Tasks:

- ✅ Embed user query (in chat turn endpoint, before calling Gemini)
- ✅ Top-k vector search (SQL Server cosine distance)
- ✅ Inject retrieved context into Gemini prompt (master_prompt + RAG context + question)
- ✅ Grounded-only enforcement (if retrieval_miss → return configurable grounded_only_message)
- ✅ Handle retrieval miss gracefully
- ✅ KB retrieval as tool call (executed internally in chat turn endpoint)
- retrieval_hit / retrieval_miss events

Dependencies:

- Embeddings and Chunking (#9)
- Merge Chat MVP (#10)

---

## 12 — Admin Chat Mode

Provide internal debugging tools ("Probar" mode). This is the only UI for viewing telemetry events — no standalone dashboard.

Tasks:

- ✅ Admin debug panel (left side of chat UI, admin-only)
- Event streaming in chat (real-time via SignalR, consuming ITelemetryService)
- Prompt visibility (show constructed prompt with RAG context)
- Retrieved chunks display (show top-k results with distances)
- ✅ Runtime parameters display (current bot config)
- Tool response inspection (show tool call inputs/outputs)
- Stage latency visualization (STT → LLM → TTS timing, from telemetry events)

Dependencies:

- Merge Chat MVP (#10)
- Telemetry Service (#4)

---

## 13 — Homepage

Implement public landing page.

Tasks:

- ✅ Public bot landing page (per url_slug)
- ✅ Bot name/logo display
- ✅ Login CTA (GOB-ID redirect)
- ✅ Mobile responsive layout
- ✅ Visitor state (see landing, can't interact)

Dependencies:

- App Boilerplate (#1)

---

## 14 — Final Testing & Documentation

User acceptance testing, feedback collection, and deployment documentation.

Tasks:

- End-to-end user acceptance testing (admin creates bot → publishes → end user converses)
- Collect and address user feedback
- Backup/restore documentation for standalone deployment (SQL Server + vector data + S3 blobs)
- Deployment guide (SaaS and Standalone modes)

Dependencies:

- All previous milestones
