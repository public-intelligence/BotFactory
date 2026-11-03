# P0 Test Coverage — Implementation Plan

**Status:** Implemented  

**Depends on:** none  
**Blocks:** [P1](test-coverage-p1.md) (public-token + auth helpers)  
**Related:** [P2](test-coverage-p2.md), coverage analysis canvas

## Goals / out of scope

**Goals**
- Close five P0 gaps with deterministic, repo-local tests (no real GOB-ID, LiveKit cloud, Gemini, ElevenLabs).
- Integration tests for HTTP/SignalR; unit tests for providers/helpers/agent modules.
- Reuse existing factories and turn-pipeline fakes; do not re-test Gemini tool-loop persistence (`LiveKitChatTurnTests`).

**Out of scope**
- CI wiring for frontend/agent (`make test` expansion) — **P1**.
- Full LiveKit room/agent E2E, Playwright, real STT/TTS.
- Exhaustive admin CRUD, KB pipeline, Hangfire (covered elsewhere).
- Large refactors of `chat.ts` solely for testability (small pure-helper extracts only if required).

## Fixtures to reuse / extend

| Asset | Path | Use |
|---|---|---|
| `BotFactoryWebApplicationFactory` | `apps/backend/BotFactory.Tests/Infrastructure/` | Pin `PublicAccess:SigningKey` (≥32 chars) |
| `LiveKitWebApplicationFactory` + Gemini `SseQueueHandler` | same + `LiveKitChatTurnTests` | Hub `SendTurn` with fake Gemini |
| `TestAuthHelper` | `Infrastructure/TestAuthHelper.cs` | Cookie session via `POST /api/auth/dev/token` |
| New `PublicBotTestHelper` | create | Seed public bot; mint anon Bearer via `POST /api/public/bots/{publicId}/chat` |
| New `HubTestHelper` + SignalR.Client package | create | HubConnection against TestServer |
| Frontend pattern | `chat-session-messages.spec.ts` | Vitest pure-function style |
| Agent pattern | `test_provider_errors.py`, `test_turn_metrics.py` | pytest without LiveKit runtime |

## Workstreams

### WS1 — Public / embed HTTP (M)

**Files:** `PublicBotEndpointsTests.cs`, `EmbedEndpointsTests.cs`  
**Under test:** `PublicBotEndpoints.cs`, `PublicTokenScopeMiddleware.cs`, `EmbedEndpoints.cs`

Key cases: create public chat (token + anon conv), embedded surface, 404 for private/draft, mode mismatch 400, anon cap 429, resume/close owner vs wrong owner, closed 410, public token cannot `GET /api/bots` (403), embed HTML + CSP frame-ancestors, embed 404/503.

### WS2 — Auth / session (M)

**Files:** `AuthEndpointsTests.cs`, `FakeAuthProviderTests.cs`, `GobIdAuthProviderTests.cs`, `SessionAuthHandlerTests.cs`  
**Under test:** `AuthEndpoints.cs`, `FakeAuthProvider.cs`, `GobIdAuthProvider.cs`, `SessionAuthHandler.cs`

Key cases: providers list, login URL, fake token exchange + cookie + `/me`, logout, hub token, GobId with mocked HttpMessageHandler, cookie/Bearer/query access_token auth, public JWT on public API, expired session refresh.

### WS3 — ConversationHub (L)

**Files:** `ConversationHubTests.cs`, optional `ChatTurnRegistryTests.cs`  
**Under test:** `ConversationHub.cs`, `ChatTurnRegistry.cs`

Key cases: SendTurn happy path (turnStarted/delta/completed), empty text, closed, chat disabled, unauthorized, provider error → turnFailed, CancelTurn, public token owner, SubscribeEvents without telemetry permission.

### WS4 — Frontend chat (M)

**Specs:** `interaction-mode.storage.spec.ts`, `contact-extract.spec.ts`, `bot-theme.spec.ts`, `public-session.service.spec.ts`, `conversation-stream.service.spec.ts`, thin `chat.spec.ts`  
Do not reassert `chat-session-messages` mapping already covered.

### WS5 — Agent (M)

**Files:** `test_llm_backend.py`, `test_relays.py`, `test_agent_helpers.py`  
Skip: entrypoint, Silero VAD, real AgentSession. If `agent.py` import side effects block tests, extract pure helpers to a small module.

## PR / implementation order

1. Factory helpers + WS1 + WS2  
2. WS3 (needs public token helper from step 1)  
3. WS4 + WS5  

## Definition of done

- [x] All WS1–WS5 files/cases above (intent preserved if renamed)
- [x] `dotnet test apps/backend/BotFactory.Tests` green
- [x] `npm test` / `ng test` in `apps/frontend` green for new specs
- [x] `pytest` in `apps/agent` green for new modules
- [x] Public JWT cannot list `/api/bots`
- [x] Hub SendTurn happy + closed/empty/forbidden covered
- [x] `CHANGELOG.md` `[Unreleased]` → `Added` for P0 test coverage
- [x] No production behavior changes unless a clear bug is found (separate commit)

## Risks

- SignalR timing: wait on TaskCompletionSource, not Delay loops
- Pin PublicAccess signing key in factory
- Embed shell: local HTML only, never hit real SPA URL
- Agent: avoid importing entrypoint; mock httpx transports
