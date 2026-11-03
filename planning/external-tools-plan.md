# Bot Factory — Configurable External Tools & Web Services: Implementation Plan

**Version:** 1.1
**Date:** 2026-07-18
**Status:** Phases 0–2 implemented 2026-07-18 (domain model, bot-aware registry with kb_search guardrails, execution gateway, REST connector, endpoints, Herramientas admin UI, tests). Phases 3–5 pending.
**v1.1 changes (2026-07-18 review):** decisions resolved (§15); built-in tool guardrails added (§4.5); minimal-change registry approach (§6.1); cost tracking removed from scope; voice latency handled by extending the existing thinking sound (§8); tool-results-in-history question answered (§3 item 6); versioning, environment pairing, and approval-state expansion dropped; consequential tools + confirmation moved to Phase 4; anonymous-access work (in flight, outside this plan) established as the start gate.

Evidence legend used throughout:
- **[Confirmed]** — read directly in the codebase (file references given).
- **[Inferred]** — architectural deduction from surrounding evidence.
- **[Missing]** — not present in the codebase; must be built or decided.
- **[Decision]** — see §15 (most were resolved in the 2026-07-18 review).

---

## 1. Executive Summary

**How the platform handles bot capabilities today.** Bot Factory is a multi-tenant (per-`OrganizationId`) ASP.NET 10 + Angular 21 platform. All conversational intelligence lives in the .NET backend: one pipeline, `ChatTurnService` (`apps/backend/BotFactory.Infrastructure/Services/ChatTurnService.cs`), serves both voice and text. Voice enters via the Python LiveKit agent (`apps/agent`) which is deliberately a "dumb pipe" — Silero VAD + ElevenLabs STT → `POST /livekit/chat/turn` (SSE) → ElevenLabs TTS. Text enters via SignalR `ConversationHub.SendTurn`. The LLM is Gemini called over raw HTTP with streaming function calling (`GeminiChatService`). Bot configuration is a flat row on the `bots` table edited from the Angular admin.

**Tool abstractions already exist — but only as a static seam.** [Confirmed] `BotFactory.Core/Tools/` defines a clean, provider-agnostic contract: `IToolHandler` (name + `FunctionDeclaration` with JSON-Schema parameters + `ExecuteAsync`), and `IToolRegistry`. `ChatTurnService` already runs a full function-calling loop: Gemini emits a `functionCall`, the handler executes, call and result are persisted as `Message(Role=Tool)` rows, telemetry is emitted with a per-turn `TurnId`, and the result is fed back to the model. Exactly one tool exists (`kb_search`, KB retrieval), registered statically in DI. There is **no** tool entity, no per-bot gating (every bot gets every registered tool), no external HTTP execution, no admin UI, and no schema validation of model-generated arguments.

**Recommended architecture.** Keep the existing `IToolHandler` seam and make it data-driven, under two overriding principles set in review: **minimal changes to the working pipeline** (the `IToolRegistry` interface, `GeminiChatService`, and the tool loop keep their current shapes — §6.1) and **built-in tools are inviolable** (`kb_search` stays code-defined, always present, untouchable by configuration — §4.5).

- **`ToolConnection`** (institution-scoped): reusable external-system configuration — base URL, connector type, auth method, encrypted credentials, timeouts, egress constraints.
- **`ToolDefinition`** (institution-scoped): one logical LLM-facing capability referencing a connection — AI-facing description/instructions, parameter JSON Schema, operation config (method/path/mappings), policy flags (read-only, confirmation, call caps), lifecycle status.
- **`BotTool`**: explicit bot↔tool association with enable flag and per-bot overrides.
- **Bot-aware `ToolRegistry`** (same `IToolRegistry` interface, now composed per turn for the turn's bot): built-in handlers (`kb_search`) always included first and unconditionally, plus `ConfiguredToolHandler` instances materialized from the bot's enabled definitions.
- **`ToolExecutionGateway`**: the platform-controlled pipeline every *configured* tool call passes through — argument schema validation → deterministic policy evaluation → credential resolution → protocol connector invocation (SSRF-guarded, size/time-capped) → response mapping/redaction → `ToolExecution` record + telemetry → structured result to the model. The model only ever *selects* a tool and *proposes* arguments; it never constructs requests. Built-ins bypass the gateway and keep today's direct path.
- **`IToolConnector`**: protocol adapters. REST first; SOAP second (pending the Costa Rica civil-registry meeting); others behind the same interface.

**Recommended first connectors.** (1) **REST/JSON** — highest general usefulness, lowest effort, maps directly onto existing `IHttpClientFactory`/`KbUrlValidator` patterns. (2) **SOAP 1.1/1.2** — high government-system compatibility in Latin America; build in Phase 4 unless the civil-registry meeting confirms SOAP earlier, in which case pull it forward. Defer GraphQL/gRPC/queues/DB-query connectors.

**Main risks.**
- *Security:* the agent-facing `/livekit/*` endpoints are unauthenticated today [Confirmed — `LiveKitEndpoints.cs:16-19`, TODO at line 87]; tool execution multiplies the blast radius of that gap and must be closed first. Credentials handling, SSRF, prompt injection from external responses, and cross-institution data leakage are the top threats.
- *Operational:* no outbound resilience layer (no Polly) and no rate limiting exist today — external calls need explicit timeouts and call caps for stability. (Cost is not a concern in this scope by review decision: external calls are assumed free.)
- *Product:* voice latency — a synchronous tool call must fit inside the agent's 30 s SSE read timeout and, practically, a few seconds of user patience; the existing thinking/loading sound extends naturally to cover tool execution (§8).

**Decision status.** The 2026-07-18 review resolved the major decisions (§15): read-only tools in v1; secret storage unchanged (existing AES pattern); no versioning or environment pairing for now; org admin approves via `tool:publish`; `JsonSchema.Net` for validation; call ceilings with conservative defaults; dashboards + Hangfire alerts without email. Remaining external dependencies: the civil registry's protocol (pending meeting), GOB-ID delivery of `tool:*` permission strings, and completion of the in-flight anonymous-access work, which gates the start of this feature.

---

## 2. Current-State Findings

All items below are **[Confirmed]** unless noted.

### 2.1 Backend projects and services

| Project | Role |
|---|---|
| `BotFactory.Api` | Host, minimal API endpoints, middleware, SignalR hubs, auth handlers (`apps/backend/BotFactory.Api`) |
| `BotFactory.Core` | Entities, DTOs, options, auth contracts, **tool contracts** (`BotFactory.Core/Tools/`) |
| `BotFactory.Infrastructure` | EF Core (SQL Server 2025), service implementations, jobs, HTTP clients, telemetry, **tool implementations** (`BotFactory.Infrastructure/Tools/`) |
| `BotFactory.Tests` | Integration + unit tests (in-memory host, mocked Gemini) |
| `apps/kb-scraper` | Separate Playwright render microservice + shared `BotFactory.KbScraper.Contracts` project referenced by Infrastructure — the existing cross-service contract pattern |
| `apps/agent` | Python LiveKit worker (`bot_factory_agent`) — audio only |

### 2.2 Bot configuration model

`Bot` (`BotFactory.Core/Entities/Bot.cs`) is a flat config row: identity (`BotName`, `UrlSlug`, `PublishedState`, `MasterPrompt`), RAG (`TopK`, `SimilarityThreshold`, `GroundedOnlyMessage`), interaction modes (`EnableChat`, `EnableVoice`), voice/VAD parameters, greeting, thinking sound. Historical schema evolution (23 migrations) always added **columns on `bots`**, never child config tables; the closest child-of-bot precedent is `KbDocument`. CRUD in `BotEndpoints.cs` under `/api/bots` with permission filters (`bot:update`, `bot:publish`, …).

### 2.3 The existing tool pipeline (the seam to extend)

- Contracts: `IToolHandler` { `Name`, `FunctionDeclaration Declaration`, `ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken)` }, `ToolContext(Bot, Conversation)`, `ToolInvocation(ResponsePayload, Telemetry, NoUsableResults, Display, Summary)` — `BotFactory.Core/Tools/IToolHandler.cs`.
- `FunctionDeclaration` / `JsonSchema` records are the Gemini `functionDeclarations` wire shape, documented as portable to OpenAI/Anthropic — `BotFactory.Core/Tools/FunctionDeclaration.cs`.
- `ToolRegistry` (`BotFactory.Infrastructure/Tools/ToolRegistry.cs`) is built **once** from DI (`Program.cs:177-179`, registered **scoped**); `BuildGeminiToolsPayload()` returns **all** handlers for **every** bot. One handler exists: `KbSearchToolHandler` (`kb_search`).
- Tool loop: `ChatTurnService.ExecuteAsync` (lines ~112–322) — stream Gemini → on `GeminiFunctionCall` persist a `Message(Role=Tool)` with `{name,args}`, execute handler, persist result as a second Tool message (`Provider="gemini-tool"`), emit the handler's telemetry, feed `functionResponse` back, loop. Hard cap `GeminiChatOptions.MaxToolIterations` = **2** (options-bound, changeable via configuration alone); on exceeding it, streams a fallback message. A `NoUsableResults` + `bot.GroundedOnlyMessage` combination short-circuits to the grounded-only message.
- Gemini integration: `GeminiChatService` — raw HTTP SSE against `generativelanguage.googleapis.com`, **not** `Microsoft.Extensions.AI`/`IChatClient` (the March architecture doc is outdated on this point); injects `tools` + `functionCallingConfig.mode="AUTO"` when the registry is non-empty; API key from `AppSettingService.GetDecryptedKeyAsync(ServiceType.Llm)`.
- Tests already spec the loop: `BotFactory.Tests/LiveKitChatTurnTests.cs` (function-call→tool-rows→final-answer, loop cap, grounded-only, 429 provider_error) and `GeminiChatServiceTests.cs` (tools payload serialization).

### 2.4 LiveKit integration and the voice path

- Backend: `LiveKitService` (Livekit.Server.Sdk.Dotnet) creates rooms with JSON metadata + explicit agent dispatch (`RoomAgentDispatch { AgentName = "bot-factory-agent" }`); mints 1-hour participant tokens. Metadata (`RoomMetadata`) carries botId, conversationId, voice/VAD params, greeting, **and the decrypted ElevenLabs API key** (see §9).
- Agent: `apps/agent/src/bot_factory_agent/agent.py` — per-bot cached Silero VAD, multilingual turn detector, ElevenLabs STT/TTS, `BackendLLM` (`llm_backend.py`) POSTs `{conversationId, text, turnId}` to `/livekit/chat/turn` and streams SSE `data:` deltas into TTS. The SSE parser understands named events (currently `provider_error`) and ignores `:` comment lines — an extensibility point. Client timeout: `httpx.Timeout(30.0, connect=15.0)` per turn. The agent's LiveKit `tools` parameter is accepted and **ignored** — tool calling never reaches the agent.
- The frontend plays a **thinking/loading sound** while the agent is in the "thinking" state (driven by `AgentStateChanged` events; «Sonido de escritura» toggle, on by default) — the existing latency-masking mechanism that §8 extends to tool execution.
- Voice and chat behavior are consistent by construction: both call `IChatTurnService`.

### 2.5 Knowledge Base integration

`KbDocument`/`KbChunk` (SQL Server 2025 `vector(1536)`), Gemini embeddings (`gemini-embedding-001`), Hangfire indexing jobs, `KbSearchService` raw-SQL cosine search. Retrieval is exposed to the model **as the `kb_search` tool**, not prompt stuffing.

### 2.6 External-service clients

All outbound HTTP via `IHttpClientFactory` typed clients (`Program.cs`): Gemini chat/embeddings, ElevenLabs voices, GOB-ID, KB scraper. **No Polly / no shared retry / no circuit breaker anywhere** [Confirmed by grep]. `KbScraperClient` hand-rolls busy-retries (`[2,5,10,20]s` on 503) and authenticates with a static bearer key; it consumes the shared `BotFactory.KbScraper.Contracts` project. `ProviderErrorClassifier` + `ProviderServiceException` map upstream failures to stable codes with Spanish user/operator messages. `KbUrlValidator.IsSafeToFetchAsync` provides SSRF protection (blocks loopback/private/link-local/cloud-metadata ranges, DNS-resolves and re-checks) for KB URL ingestion.

### 2.7 Secret management

`AppSetting` rows hold per-org provider keys, AES-256-CBC encrypted (`AesEncryptionService`, master key from `Encryption:MasterKey` config; random IV; **no auth tag, no rotation** — noted in code comments), `[AuditMask]`ed in audit logs, `KeyPrefix` for masked display, never returned to clients. No Azure Key Vault in app code (only the KB scraper's CI uses `KV-VOZ-VUC-DEV`). Session JWT/refresh tokens are stored **unencrypted** in the `sessions` table. Per review decision, tool credentials reuse this AES pattern unchanged.

### 2.8 Authorization and institution boundaries

- Auth: GOB-ID OIDC code exchange → server-side `Session` row → opaque `bf_session` HttpOnly cookie (`SessionAuthHandler`, transparent refresh). Dev `FakeAuthProvider` with seeded users.
- Permissions: flat strings in the JWT `permissions` claim (`Permissions.cs`: `superadmin`, `bot:admin`, `bot:update`, `bot:publish`, `kb:update`, `settings:update`, `conversation:read`, `telemetry:read`, `audit:read`, `bot:chat`, …). `PermissionEndpointFilter` grants if caller has `superadmin` OR `bot:admin` OR the specific permission. Roles live **only in GOB-ID** — adding new permission strings requires GOB-ID coordination (§15).
- Tenancy: every tenant entity derives from `BaseEntity { Id, OrganizationId, CreatedAt, UpdatedAt }`; `BotFactoryDbContext` reflectively applies a global query filter `OrganizationId == tenantContext.OrganizationId` to all `BaseEntity` subclasses and stamps org on insert. New entities inherit isolation automatically.
- Anonymous access at exploration time: **only** `GET /api/bots/by-slug/{slug}` is anonymous; creating a conversation (`POST /api/bots/{id}/chat`) requires authentication. [Confirmed] Anonymous access is now being implemented in a parallel effort outside this plan; it gates the start of this feature (§15).

### 2.9 Observability

- `TelemetryEvent` table (org-scoped): `EventName`, `Source (Agent|Backend)`, `BotId`, `ConversationId`, `UserId`, **`TurnId`** (per-turn correlation GUID minted by the agent and threaded through `/livekit/chat/turn`), `Stage`, `LatencyMs`, `Provider`, `ErrorMessage`, `Summary`, `Data` (JSON). Catalog in `docs/telemetry-events.md`.
- `ChatTurnService` emits `backend_completed` with a per-turn `segments` array — each Gemini span and **each tool call** with durations. The agent aggregates pipeline metrics into `turn_completed` sharing the same `TurnId`.
- Live feed: `TelemetryService.FeedAllowlist` → SignalR `ConversationHub` groups (`conv:{id}` messages for everyone; `conv:{id}:events` for `telemetry:read` admins). `kb_retrieved` is a "content event" routed to all viewers — the pattern for user-visible tool output.
- The Angular conversation feed already renders `tool_call · <name>` pills with expandable bodies and tool spans in the turn waterfall (`features/conversations/conversation-item.ts`, `conversation-feed.ts`).
- App-level logging is default `ILogger` console; **no Serilog/OpenTelemetry/App Insights SDK** in code. No correlation-ID middleware beyond `TurnId`.

### 2.10 Cost tracking, quotas, rate limiting

**None exist.** No token counting (Gemini `usageMetadata` is not parsed), no cost estimation, no per-tenant quotas, no ASP.NET rate limiter. [Confirmed by grep] Per review, cost is out of scope for this feature; call caps are planned for stability/abuse only.

### 2.11 Background jobs

Hangfire on SQL Server: global retry (3 attempts, 10/60/300 s), `JobLoggingFilter`, dashboard at `/api/tasks` gated to `superadmin`/`bot:admin`, recurring jobs (`close-stale-conversations` hourly; `refresh-url-kb-documents` Mondays 05:00), `IBackgroundJobService` enqueue abstraction. A natural home for async tool operations and monitoring/alert sweeps.

### 2.12 Frontend

Angular 21 standalone + zoneless + signals; ng-openapi-gen client under `src/app/api` regenerated with `npm run api:generate` (never hand-edited); `Api.invoke(fn, params)` promise convention; ngx-sonner toasts; permission checks are inline `permissions.includes(...)` computeds; hand-rolled reactive forms (no schema-driven forms exist); ui-core components in use (`PageShellComponent`, `PaginatedTableComponent`, `StatusPillComponent`, `FilterBarComponent`) with several available-but-unused (confirm dialogs, error summary, validation panel). Closest analogs for the new UI: **Settings** page (`features/settings/settings.ts` — masked org-wide secrets, bulk upsert, never-return-secret), **bot-edit tab shell** (`bot-edit.ts` + `BotEditStore`), and the **KB tab** (`bot-edit-kb.ts` — richest CRUD/table/modal surface). There is **no tool/connection UI** and no `/api/tools*` endpoint in the OpenAPI spec.

### 2.13 Deployment topology

Azure (`DEPLOYMENT.md`): API on App Service, frontend on Static Web Apps, agent + KB scraper + LiveKit on ACI, SQL Server 2025 on a VM, Azure Blob, Redis (LiveKit only). Services reach each other over **public FQDNs**; no VNet/private endpoints/IP allowlists documented [Inferred from config]. CI in `.azure/*.yml` (Azure DevOps).

---

## 3. Gaps and Constraints

1. **No dynamic/per-bot tool registration.** `ToolRegistry` is populated once from DI and global; `GeminiChatService.BuildStreamRequest` always attaches every handler. [Confirmed]
2. **No tool persistence at all** — no entity, migration, DTO, endpoint, or UI. The `tool_registration` table in the March architecture doc was never built. [Confirmed]
3. **No argument validation.** Model-proposed `args` go straight to the handler as `JsonElement`. Fine for `kb_search`; unacceptable for external calls. [Confirmed]
4. **KB-specific behavior baked into the generic loop.** The grounded-only short-circuit triggers on *any* handler's `NoUsableResults` — an external tool returning an empty result would wrongly emit the KB grounded-only message. Must be scoped to `kb_search` when generalizing (a one-line name guard — §6.1). [Confirmed — `ChatTurnService.cs` ~285-301]
5. **`MaxToolIterations = 2`** is tuned for a single KB lookup; multi-tool turns (KB + external lookup) will hit the cap and stream the fallback. No code change needed — the value is options-bound; raise via configuration when multi-tool bots appear. [Confirmed]
6. **Tool messages are excluded from history** (`ChatTurnService` history load filters `Role == Tool`). Review question: *can the model access prior results via stored chat messages?* Partially, already today: the assistant's final answer — which summarizes the tool result — **is** persisted as a normal Assistant message and is in history, so the model does see prior results in summarized form. What it lacks is the raw call/response payloads, which *are* stored (`Message(Role=Tool)` rows hold the `{name,args}` and response JSON [Confirmed]) but filtered out of the history query. Including them is feasible and is the correct mechanism when needed: replay stored Tool-message pairs as Gemini `functionCall`/`functionResponse` content parts (Gemini requires well-formed pairing — they cannot be replayed as plain text), bounded to a recent window (e.g. the last 2–3 tool exchanges) to contain token growth. Recommendation: ship v1 without it — summaries usually suffice, and re-calling a read-only lookup is harmless (the data may even be fresher) — and add windowed replay as a Phase 3 tuning item if re-call rates prove noisy. [Confirmed mechanics; recommendation inferred]
7. **Unauthenticated agent channel.** `/livekit/chat/turn` and `/livekit/telemetry/events` accept any caller that knows a conversation id; tenant context is derived from the row. An explicit TODO exists. This is the top prerequisite fix before tools ship. [Confirmed]
8. **No outbound resilience layer** (no Polly, no shared timeout/circuit-breaker policy), **no rate limiting, no quotas** — external tools introduce load and abuse surface with nothing to bound them. Cost, per review, is explicitly not a concern in this scope (external calls are assumed free), so caps exist for stability and abuse prevention, not budget. [Confirmed]
9. **Secrets constraints:** AES-CBC without rotation or authentication tag; master key in App Service settings; no Key Vault in app code; a hardcoded ElevenLabs-looking key sits in `DbSeeder.cs:35` [Confirmed — separate cleanup]; decrypted ElevenLabs key travels in LiveKit room metadata readable by room participants [Confirmed — existing pattern that must NOT be repeated for tool credentials]. Per review, storage stays on the AES pattern for tool credentials.
10. **Voice latency ceiling:** the agent's per-turn SSE read timeout is 30 s; Gemini's own HTTP timeout is 30 s. A synchronous tool call must fit well inside these, and SSE keepalives are needed for anything beyond a few seconds (the agent parser already tolerates `:` comment lines). The existing thinking sound covers the perceived-latency side (§8). [Confirmed]
11. **Permission strings are externally owned.** New `tool:*` permissions require GOB-ID configuration; until then only `bot:admin`/`superadmin` would pass the filter. [Confirmed mechanism; rollout per §15]
12. **Tenancy filter vs. platform-global resources.** The reflection-applied `OrganizationId == tenant` filter makes truly global (cross-org) rows awkward; per review, global tools ship as per-org **templates** (§12), avoiding filter surgery. [Confirmed mechanism]
13. **No schema-driven forms in the frontend** — the tool parameter editor and test console introduce that capability for the first time. [Confirmed]
14. **JSON Schema validation library** — resolved in review: **`JsonSchema.Net`** (json-everything), the most actively maintained draft-2020-12 implementation. [Resolved]
15. **Unknowns requiring validation:** the Costa Rica civil registry's actual protocol/auth (SOAP? mTLS? IP allowlist? VPN?); expected latencies of target government services; whether any institution requires on-prem/VPN-only egress that the current public-egress topology cannot reach. [Missing]

---

## 4. Proposed Target Architecture

### 4.1 Concept separation (mapped to the codebase)

| Concept (from the brief) | Recommendation | Rationale |
|---|---|---|
| Tool definition | **Own entity** (`ToolDefinition`) | The AI-facing contract and lifecycle live here; reused across bots |
| Connector | **Code, not data** (`IToolConnector` implementations) | Protocol logic must be platform-controlled; config selects a connector by type string |
| Connection / external service | **Own entity** (`ToolConnection`) | Reused by many tools; owns credentials/egress policy; institution-scoped |
| Tool operation | **Folded into `ToolDefinition` for v1** (one tool = one operation) | Avoids a fourth entity; a connection with 5 operations = 5 definitions sharing it. Revisit if OpenAPI import (Phase 4) makes bulk operations common |
| Bot-tool association | **Own entity** (`BotTool`) | Explicit allowlist per bot — the opposite of today's "all tools to all bots" |
| Tool execution | **Own entity** (`ToolExecution`) + telemetry events | Governance/audit needs a queryable record independent of the message log |

This is the "likely useful model" from the brief minus the separate operation entity — the codebase's flat-config style and the v1 scope don't justify it yet.

### 4.2 Component responsibilities

```
Angular admin ──CRUD──> Tool/Connection endpoints ──> EF entities (org-scoped, audited)
                                                        │
Voice (agent SSE) ─┐                                    ▼
                   ├─> ChatTurnService ──> bot-aware ToolRegistry ──> [kb_search — always, untouched]
Text (SignalR)  ───┘         │                                        + ConfiguredToolHandler*
                             │  functionCall(name, args)                    │
                             └────────────────────────────────> ToolExecutionGateway
                                                                  (configured tools only)
                                                                  1. validate args (JSON Schema)
                                                                  2. evaluate policy (deterministic)
                                                                  3. resolve credentials (decrypt)
                                                                  4. IToolConnector.InvokeAsync
                                                                     (REST now; SOAP later)
                                                                     · EgressGuard (SSRF/allowlist)
                                                                     · timeout / retry / size caps
                                                                  5. map + redact response
                                                                  6. write ToolExecution + telemetry
                                                                  7. return ToolInvocation to model
```

- **Frontend:** tool/connection management pages, bot-tool tab, test console, execution history. Never sees secrets.
- **Backend:** everything above; single place where credentials are decrypted and network egress happens.
- **Agent runtime / LiveKit:** unchanged data path (SSE text deltas). Additions limited to: authenticated backend channel, SSE keepalives during tool execution, optional `tool_status` event later.
- **Tool registry:** per-turn composition; built-ins first, then the per-bot allowlist; same `IToolRegistry` interface.
- **Secret store:** `ToolConnection.EncryptedSecretsJson` via existing `IEncryptionService` behind an `IToolCredentialProvider` seam. Storage stays on the AES pattern per review; the seam exists only so a different backend could be swapped in someday without touching the gateway.
- **Observability stack:** `ITelemetryService` events (feed-visible), `ToolExecution` rows (queryable history), audit logs (config changes, automatic via `IAuditable`).
- **External systems:** reached only from the backend, only through connectors, only at configured hosts.

### 4.3 Execution pipeline (validated against the current code)

The brief's 16-step pipeline maps cleanly onto `ChatTurnService`; steps 1–2 happen at turn start (registry composition), steps 3–16 inside the existing tool-loop iteration — for configured tools, `handler.ExecuteAsync` *is* the gateway call (`ConfiguredToolHandler`), so the loop code itself does not change. Confirmation (step 8) is deferred to Phase 4 and implemented as a deterministic pending-action state, not a model decision (§4.4).

### 4.4 Confirmation model for consequential tools (Phase 4 — deferred; v1 is read-only by review decision)

- `ToolDefinition.RequiresUserConfirmation` + `IsConsequential` flags (schema ships in v1; enforcement arrives with the first consequential tool).
- On a consequential call, the gateway does **not** execute. It stores `{toolId, validated args, hash}` as a pending action on the conversation (new nullable columns or `KeyValueEntry`), and returns a structured "confirmation required" result containing a human-readable parameter summary. The model relays the summary and asks the user to confirm.
- On the next turn, a deterministic check (not the model): if the pending action exists and the user's reply is affirmative (model calls a built-in `confirm_pending_action` tool — whose only effect is to release the stored action), the gateway executes **the stored arguments**, never re-generated ones, with an idempotency key. Any other tool call or a negative reply clears the pending action.
- Voice and chat behave identically because this is all inside the shared pipeline.

### 4.5 Built-in tool guardrails (`kb_search` and future system tools)

Review requirement: the existing KB tool must be **always present and impossible to modify** through the new configurable layer, with minimal disturbance to code that already works. Guardrails, in layers:

1. **Code-defined, no data surface.** Built-ins remain plain `IToolHandler` classes registered in DI (`Program.cs`) exactly as today. They have no `ToolDefinition` row, so there is nothing in the database, API, or UI that can edit, disable, or delete them. Their execution path is untouched — they do **not** pass through the `ToolExecutionGateway` (no policy layer, no schema-validation layer, no new execution rows; behavior is bit-for-bit today's).
2. **Always present by construction.** The bot-aware `ToolRegistry` (§6.1) seeds itself from the DI handler list first, unconditionally, before appending configured tools. A bot with zero configured tools yields exactly today's payload.
3. **Reserved names.** `ToolDefinition.Name` validation rejects (case-insensitive) any built-in handler name — taken live from the DI handler list at validation time, so future built-ins are automatically reserved too. Configured tools can never shadow a built-in. The registry merge is defensive anyway: on collision, the built-in wins.
4. **No association surface.** `BotTool` rows reference `ToolDefinition` only; built-ins cannot be toggled per bot. The bot "Herramientas" tab may show a read-only informational row («Base de conocimiento — herramienta del sistema») so admins understand it exists, but it renders no controls.
5. **Regression tests as a tripwire.** Tests assert: (a) every bot's Gemini payload contains the `kb_search` declaration with its current serialization; (b) creating a tool named `kb_search` (any casing) is rejected; (c) the grounded-only behavior still fires for `kb_search` and *only* for it; (d) the existing `LiveKitChatTurnTests` suite stays green untouched.

---

## 5. Data-Model Changes

Naming follows existing conventions: PascalCase entities extending `BaseEntity` (org scoping + stamping for free), snake_case plural tables, `IEntityTypeConfiguration<T>` per entity, string-stored enums, `IAuditable` for config entities.

### 5.1 `ToolConnection` → table `tool_connections` (`BaseEntity`, `IAuditable`)

| Field | Type | Notes |
|---|---|---|
| `Name` | string(200) | unique per org (filtered index like `bots.UrlSlug`) |
| `Description` | string(1000)? | |
| `ConnectorType` | string enum | `rest` (v1); `soap` (Phase 4) — extensible |
| `BaseUrl` | string(500) | https required in prod; host pinned for egress guard |
| `AuthType` | string enum | `none` \| `api_key_header` \| `api_key_query` \| `bearer` \| `basic` \| `oauth2_client_credentials` (Phase 3) |
| `AuthConfigJson` | nvarchar(max) | non-secret auth config: header name, token URL, scopes |
| `EncryptedSecretsJson` | nvarchar(max), `[AuditMask]` | AES via `IEncryptionService` (storage unchanged per review); JSON bundle `{apiKey?, clientSecret?, password?}` |
| `SecretPrefix` | string(10) | masked display, mirrors `AppSetting.KeyPrefix` |
| `DefaultHeadersJson` | nvarchar(max)? | static extra headers (non-secret) |
| `TimeoutMs` | int, default 8000 | per-call budget |
| `MaxResponseBytes` | int, default 262144 | response cap |
| `Status` | string enum | `active` \| `disabled` (emergency kill switch) |

Environment pairing (separate test vs. production connections) was reviewed and deferred along with versioning — a connection points at one system; admins can simply create a second connection against a test endpoint if they need one.

### 5.2 `ToolDefinition` → table `tool_definitions` (`BaseEntity`, `IAuditable`)

| Group | Fields |
|---|---|
| Identity | `ToolConnectionId` FK; `Name` (LLM function name — normalized `snake_case`, unique per org, validated `^[a-z][a-z0-9_]{2,63}$` **and not a reserved built-in name — §4.5**); `DisplayName`; `Category?`; `SupportContact?` |
| AI-facing | `AiDescription` (what it does + when to use — becomes `FunctionDeclaration.Description`); `AiUsageNotes?` (when NOT to use, prerequisites, how to present results — appended to the system instruction when the tool is active); `ResultHint?` (advisory vs. authoritative/real-time framing) |
| Operation (REST v1) | `HttpMethod`; `PathTemplate` (e.g. `/api/expedientes/{numero}`); `QueryParamsJson` (name → literal or `{param}` ref); `StaticHeadersJson?`; `BodyTemplateJson?` (POST bodies with `{param}` refs); `NormalizeArgDiacritics` (default false — strips diacritics from string arg values before templating, for APIs that geocode/search better unaccented, e.g. weatherstack) |
| Parameters | `ParametersSchemaJson` — JSON Schema (object, properties, required, enum, types, descriptions). Single source for: `FunctionDeclaration.Parameters`, server-side validation, and the test-console form |
| Response | `ResponseMappingJson?` (v1: list of JSON-path picks + rename; empty = pass-through subject to size cap); `ErrorMappingJson?` (HTTP status → Spanish user-facing meaning) |
| Policy | `IsConsequential` (default false); `RequiresUserConfirmation` (default false; must be true if consequential — enforced); `RequiresAuthenticatedUser` (default true — evaluated against the conversation's actual auth state once the in-flight anonymous-access work lands); `MaxCallsPerConversation?`; `TimeoutMsOverride?`; `DataClassification` (`public` \| `internal` \| `personal` \| `sensitive`) |
| Lifecycle | `Status`: `draft` → `published` → `disabled` / `archived`. **Final set per review — publishing *is* the approval:** the transition requires `tool:publish`, held by org admins under the permission model; no separate `pending_approval` state. `LastTestedAt?`, `LastTestOutcome?` |

### 5.3 `BotTool` → table `bot_tools` (`BaseEntity`, `IAuditable`)

`BotId` FK (cascade), `ToolDefinitionId` FK (restrict), unique `(BotId, ToolDefinitionId)`, `Enabled` (bool), `AdditionalInstructions?` (per-bot AI note), `MaxCallsPerConversationOverride?`. Auditable — enabling a tool on a bot is a sensitive config change. Built-in tools have no rows here and cannot be disabled (§4.5).

### 5.4 `ToolExecution` → table `tool_executions` (`BaseEntity`)

`BotId`, `ConversationId`, `TurnId` (Guid), `UserId?`, `ToolDefinitionId`, `ToolConnectionId`, `ToolName` (denormalized), `ConnectorType`, `ArgumentsJson` (validated + redacted), `Outcome` enum (`success` \| `validation_failed` \| `policy_blocked` \| `confirmation_pending` \| `timeout` \| `http_error` \| `connector_error` \| `cancelled`), `HttpStatus?`, `ErrorCategory?`, `ErrorMessage?` (truncated 2000, mirroring telemetry), `ResolvedRequestSummary` (method + redacted URL after templating), `RequestBytes?`/`ResponseBytes?`, `LatencyMs`, `RetryCount`, `ResultSnippetJson?` (redacted, truncated), `DefinitionSnapshotJson` (exact tool+connection config used, secrets stripped — preserves "which configuration ran"). Indexes: `(OrganizationId, ToolDefinitionId, CreatedAt)`, `(ConversationId)`.

Existing `Message(Role=Tool)` persistence stays as-is for model context; `ToolExecution` is the governance record.

### 5.5 Versioning — deferred (review decision)

No version entity. Traceability comes from two existing mechanisms: `tool_executions.DefinitionSnapshotJson` records the exact configuration each call ran with, and the automatic audit log captures field-level diffs of every definition/connection change [Confirmed mechanism]. If formal change control is ever demanded, a `ToolDefinitionVersion` snapshot table + per-bot pinning can be added later without schema disruption.

### 5.6 Test cases (Phase 3)

`ToolTestCase`: `ToolDefinitionId`, `Name`, `ArgumentsJson`, `ExpectedOutcome?`, `LastRunAt?`, `LastOutcome?` — powers saved tests/regression runs.

---

## 6. Backend Changes

### 6.1 New/modified components (by project)

**BotFactory.Core**
- `Tools/`: **no contract changes.** The grounded-only scoping fix (§3 item 4) is a one-line guard in `ChatTurnService` on the existing tool-name constant (`invocation.NoUsableResults && pendingCall.Name == KbSearchToolHandler.ToolName && …`) — `IToolHandler`, `ToolInvocation`, and `KbSearchToolHandler` stay untouched.
- New entities (§5) + enums; DTOs: `ToolConnectionDtos.cs`, `ToolDefinitionDtos.cs`, `BotToolDtos.cs`, `ToolExecutionDtos.cs`, `ToolTestDtos.cs` (responses never include secrets; include `SecretPrefix`/`hasSecret` like `AppSettingResponse`).
- `Auth/Permissions.cs`: add `tool:read`, `tool:update`, `tool:delete`, `tool:publish` (+ GOB-ID coordination; `bot:admin`/`superadmin` pass immediately via the existing filter override).

**BotFactory.Infrastructure**

*Registry — minimal-change approach (review requirement).* Keep the `IToolRegistry` interface and `GeminiChatService` exactly as they are. `ToolRegistry` is already registered **scoped** (`Program.cs:177-179`) and a chat turn is one DI scope, so the implementation becomes bot-aware the same way tenancy already does (the `ITenantContext`/`ITenantContextSetter` scoped-pair idiom [Confirmed pattern]):
- New scoped pair `IToolsetContext`/`IToolsetContextSetter` (implementation `ToolsetContext`); `ChatTurnService` calls `_toolsetSetter.SetBot(bot)` immediately after the existing `_tenantSetter.SetTenant(...)` — a one-line addition at the same place.
- `ToolRegistry` composes lazily on first use: **built-in handlers from DI first (always, unconditionally)**, then `ConfiguredToolHandler` instances for the context bot's enabled+published definitions (2 indexed queries). No bot set → built-ins only, identical to today's behavior. Name collisions are prevented by validation (§4.5), but the merge is defensive anyway: built-ins win.
- `GeminiChatService`, `BuildStreamRequest`, and the `ChatTurnService` tool loop keep their current signatures; `_toolRegistry.Get(name)` transparently returns either a built-in or a configured handler.
- `MaxToolIterations` needs **no code change** — `GeminiChatOptions` is options-bound [Confirmed]; raise the configured value (e.g. 3–4) when multi-tool bots appear.
- `Tools/ConfiguredToolHandler.cs`: `IToolHandler` façade over the gateway — the tool loop calls `ExecuteAsync` exactly as it does for `kb_search` today.

*Gateway and connectors:*
- `Tools/Execution/ToolExecutionGateway.cs` (`IToolExecutionGateway`): pipeline of §4.3 for configured tools only. Sub-components:
  - `ToolArgumentValidator` — `JsonSchema.Net` validation (resolved choice); on failure returns a structured error result to the model (`{error: "validation", missing: [...]}`) so it can re-ask the user, and records `validation_failed`.
  - `ToolPolicyEvaluator` — deterministic checks: tool enabled + published + connection active; bot association; per-conversation call count (query `tool_executions`); authenticated-user requirement (against the auth-state model the in-flight anonymous-access work defines); consequential/confirmation rules (Phase 4).
  - `ToolCredentialProvider` (`IToolCredentialProvider`) — decrypts `EncryptedSecretsJson` via the existing `IEncryptionService`. Secret storage unchanged per review; Key Vault migration explicitly out of scope. Phase 3 adds OAuth2 client-credentials token acquisition with cached tokens (`IKeyValueStore`).
  - `EgressGuard` — extract/generalize `KbUrlValidator` host+IP checks; additionally pin requests to the connection's `BaseUrl` host. Applied at execution time (DNS re-resolution), not just config time.
- `Tools/Connectors/IToolConnector.cs` + `RestToolConnector.cs`: named HttpClient `"tool-egress"` (no auto-redirects, decompression on, connection lifetime tuned); parameter substitution into path/query/body templates with strict URL-encoding (values only into declared slots — never model-built URLs); per-call CTS timeout; single retry for idempotent GETs on transient failure (Phase 3 makes retry policy configurable); response size cap enforced while streaming; JSON parse + `ResponseMappingJson` projection; `ErrorMappingJson` application; classification into `ProviderServiceException`-style categories (reuse the classifier pattern).

*`ChatTurnService` — reduced to three small edits:*
1. the `_toolsetSetter.SetBot(bot)` line (above);
2. the grounded-only name guard (Core bullet above);
3. appending active configured tools' `AiUsageNotes`/`AdditionalInstructions` to the system instruction (alongside the existing `SpeechLanguagePrompt` suffix).

SSE keepalives during tool execution live in the endpoint layer (§8), not in the service. `Services/GeminiChatService.cs`: **no changes.**

**BotFactory.Api**
- `Endpoints/ToolConnectionEndpoints.cs` — `/api/tool-connections` CRUD (`tool:read`/`tool:update`/`tool:delete`), `POST /{id}/test-connection` (connectivity + auth probe, redacted result).
- `Endpoints/ToolDefinitionEndpoints.cs` — `/api/tools` CRUD + `POST /{id}/publish|unpublish|disable` (`tool:publish` — this *is* the approval, per review), `POST /{id}/test` (execute with admin-supplied args against the gateway in **test mode**: no conversation, marked `IsTest`, full redacted request/response echo for the console), `GET /{id}/executions`.
- `Endpoints/BotToolEndpoints.cs` — `GET/PUT /api/bots/{botId}/tools` (association list + toggles; `bot:update`).
- `Endpoints/LiveKitEndpoints.cs` — generic SSE keepalive: write a `: keepalive` comment line whenever ~5 s pass without a data write (timer racing the turn; no `ChatTurnService` knowledge needed — §8).
- OpenAPI grows these routes → `npm run api:generate` for the frontend client (per AGENTS.md).
- **Security prerequisite:** shared-secret header on the `/livekit` group (`X-Agent-Key` validated against config; agent sends it from env) — closes gap §3.7. Small change; do it in Phase 0.

**Migrations** — `AddToolConnections`, `AddToolDefinitions`, `AddBotTools`, `AddToolExecutions` (+ later: `AddToolTestCases` in Phase 3; `AddConversationPendingAction` with the Phase 4 confirmation flow).

**Tests** — mirror the existing style: unit tests for validator/policy/connector templating/egress guard; the §4.5 built-in guardrail suite; integration tests driving `/livekit/chat/turn` with a stubbed external HTTP handler proving: tool exposed only to associated bots; validation failure path; policy block; timeout → structured error to model (no hallucinated success); execution row + telemetry written; kb_search regression intact.

### 6.2 Explicitly out of scope for the backend v1

Arbitrary header/URL construction by the model; SQL/database connectors; MCP; institution-specific code paths (everything is config); response streaming from tools; token/cost tracking (review decision); versioning entities; test/prod environment pairing.

---

## 7. Frontend Changes

Follow the reference patterns identified in §2.12. All new strings in Spanish, consistent with the app.

1. **Sidebar**: new entry **Herramientas** (gate: `tool:read`/`bot:admin`/`superadmin`) in `layout-shell.ts`.
2. **Tools list** (`/tools`): `PageShellComponent` + `PaginatedTableComponent` — name, connection, category, status pill, consequential badge, last-tested, actions. Filters: search/status/connection. Empty/loading/error/forbidden states per the dashboard/audit idioms.
3. **Connection management** (`/tools/connections` or a tab): card/table of connections; create/edit form with connector type, base URL, auth type, **masked secret inputs** exactly like `settings.ts` (`hasSecret` + prefix + write-only field), timeout, status toggle; "Probar conexión" button → probe endpoint result.
4. **Tool editor** (`/tools/create`, `/tools/:id/edit`): tabbed shell mirroring `BotEdit` + store pattern:
   - *General* — name (auto-normalized snake_case with preview; reserved built-in names rejected inline), display name, category, classification, connection selector.
   - *Instrucciones IA* — description, usage notes, result framing; helper text explaining these are read by the model.
   - *Técnica* — method, path template with `{param}` chips, query/headers/body editors; **parameter table** (name, type, required, description, enum) that generates `ParametersSchemaJson` — the first schema-driven form surface; validation via `ErrorSummaryComponent`/`ValidationResultsPanelComponent` (ui-core, currently unused).
   - *Políticas* — read-only vs consequential, confirmation required, max calls/conversation, timeout override.
   - *Pruebas* — test console: form generated from the parameter schema; run → show resolved request (redacted) and redacted response/error; latency; link to save as test case (Phase 3).
   - *Historial* — executions table for this tool (outcome, bot, latency, when), row → detail drawer (redacted payloads), reusing the audit drawer idiom.
5. **Bot edit — new "Herramientas" tab** (`bots/:id/edit/herramientas`): table of the org's published tools with per-bot enable toggles + optional per-bot instructions; visible with `bot:update`. Includes a read-only «Base de conocimiento — herramienta del sistema» row with no controls (§4.5).
6. **Conversation feed**: already renders tool pills/segments; extend labels for the new events (`tool_call_blocked`, etc.), and link a tool pill to its definition page for admins.
7. **Publish workflow UI**: publish/unpublish transitions with confirmation modals (adopt ui-core `ConfirmModalComponent` instead of `window.confirm` for these); change-history panel fed by the automatic audit-log diffs (no version entity — §5.5).

---

## 8. LiveKit and Agent-Runtime Changes

The core insight: **no tool logic enters the agent.** Tools execute inside the backend SSE turn the agent is already streaming. Changes are minimal and mostly protective:

1. **Dynamic tool loading** — per turn, in the backend (bot-aware scoped `ToolRegistry`); the agent is unaffected.
2. **Latency UX = extend the existing thinking sound (review direction).** The thinking/loading sound is driven by the agent's state (frontend loops the sound while the agent is "thinking"), and the agent stays in "thinking" until the first speakable delta arrives [Confirmed flow]. Because a Gemini `functionCall` typically arrives **before** any text delta, the session remains in "thinking" for the whole tool execution — the sound covers tool latency automatically, for voice and for the text UI's loading indicator alike. Plan items make this a guarantee rather than an accident:
   - The gateway emits **no user-visible deltas** during tool execution, so the "thinking" state (and the sound) persists until the model produces its post-tool answer. Verified by an E2E test asserting the agent state stays "thinking" across a slow stubbed tool call.
   - **Keepalives:** the `/livekit/chat/turn` endpoint writes a `: keepalive` SSE comment whenever ~5 s pass without a data write (generic timer in the endpoint). The agent parser already ignores `:` lines [Confirmed — `llm_backend.py::_parse_sse_line`], so keepalives neither reach TTS nor change agent state — they only prevent the agent's 30 s `httpx` read timeout from killing slow tool turns.
   - Default tool budget 8 s (connection `TimeoutMs`), hard ceiling ~15 s for voice-enabled bots (validation rule); practical guidance of one external call per voice turn (the iteration cap keeps this bounded).
3. **Progress signaling (later, optional)** — a named SSE event `tool_status` (`{state: "running", tool: displayName}`) the agent could speak as a filler phrase. Explicitly not needed for v1: the thinking sound already fills the gap.
4. **Interruptions/cancellation** — barge-in already cancels the SSE request (agent closes the stream; `ChatTurnService` observes `OperationCanceledException` → provider `gemini-partial`). The gateway must pass the same `CancellationToken` into the connector so external calls abort with the turn; in-flight consequential operations must **not** be cancellable after dispatch (execute-then-report; idempotency keys in Phase 4).
5. **Long-running operations** — beyond the voice budget: return a structured "in progress / tracking reference" result immediately and (Phase 4) convert to a Hangfire job + notify on a later turn. Keep the live session waiting only within the budget; never poll inside a voice turn.
6. **Reconnection/duplicates** — `close_on_disconnect=False` keeps sessions through blips; `BackendLLM` does not retry failed turn POSTs [Confirmed], so duplicate tool execution from transport retries is not a current risk; idempotency keys still planned for consequential ops (Phase 4).
7. **Correlation** — already in place: room name = conversationId; `TurnId` minted by the agent flows through `/livekit/chat/turn` into every telemetry event and now into `tool_executions.TurnId`. LiveKit room/participant appear in agent-sourced events.
8. **Failure behavior** — on connector failure the model receives a structured error result (`{error, userMessageHint}`) and is instructed (system suffix) to state that the external service is unavailable and, where relevant, fall back to KB information — never to invent a result. The grounded-only message remains KB-only (§6.1).
9. **Channel hardening** — shared-secret header on `/livekit/*` (§6.1); agent adds it to both `llm_backend.py` and `relays.py` from an env var.

---

## 9. Security and Privacy Assessment

### Threats and controls

| Threat | Control |
|---|---|
| Model-crafted requests (URL/SQL/envelope injection) | Model output is only `(tool name, args)`; args validated against declared schema; values substituted into fixed templates with encoding; connectors are platform code; no raw request surface |
| SSRF / internal network access | `EgressGuard` (generalized `KbUrlValidator` [Confirmed exists]): deny loopback/private/link-local/metadata IPs, DNS re-resolution at call time, host pinned to connection `BaseUrl`, https-only in production |
| Credential theft | Secrets AES-encrypted at rest (`IEncryptionService`), `[AuditMask]`ed, never in DTOs/logs/frontend/room metadata; decrypt only inside the gateway at call time; storage stays on the existing AES pattern (review decision — no Key Vault migration now), with `IToolCredentialProvider` as the seam if that ever changes. **Do not repeat the room-metadata pattern** used for the ElevenLabs key |
| Unauthenticated agent channel abuse | Shared-secret header on `/livekit/*` (prerequisite, Phase 0); longer-term LiveKit webhook signing |
| Prompt injection via external responses | Responses are data: apply `ResponseMappingJson` projection (drop unexpected fields), size caps, and wrap results in a system-framed envelope instructing the model to treat content as untrusted data, not instructions; never echo raw HTML/scripts |
| Tampering with the built-in KB tool | No data surface exists for built-ins (§4.5): code-defined, reserved names, no `BotTool` rows, registry seeds built-ins first, regression tests lock the declaration |
| Cross-institution leakage | All new entities are `BaseEntity` → automatic org query filter [Confirmed mechanism]; FK integrity keeps tools/connections/bots within one org; executions org-stamped |
| Cross-conversation leakage | Tool results persist only in the conversation's own messages/executions; no shared caches keyed without org+bot |
| Abuse / runaway usage | Per-conversation call caps, iteration cap, per-org daily ceiling (Phase 3 — stability/abuse protection; cost is out of scope by review), connection kill switch (`Status=disabled`), emergency global flag |
| Malformed/hostile responses | Streaming size enforcement, JSON parse limits, timeout, error classification; non-JSON content types rejected unless configured |
| PII exposure | `DataClassification` on definitions; redaction list (configurable header/field names always masked in `ToolExecution` payload snapshots); args/results truncated. Retention policy reviewed and deferred (§10) |
| State-changing operation misuse | `IsConsequential` + mandatory confirmation via deterministic pending-action flow (§4.4); idempotency keys; stronger audit (execution rows + audit log). **v1 is read-only by review decision** — consequential tools arrive in Phase 4 behind this control set |

### Anonymous-bot considerations

At exploration time no anonymous chat existed — conversation creation required an authenticated GOB-ID session [Confirmed — `BotEndpoints.cs` group auth]. **Anonymous access is now being implemented in a parallel effort, and this feature starts only after it lands (review decision §15).** The tool design therefore treats anonymous users as a first-class policy input from day one: `RequiresAuthenticatedUser` (default **true**) is evaluated at execution time against the conversation's actual auth state as defined by that work — tools default to blocked for anonymous users and must be individually opted in (public informational tools like weather). Per-bot and per-tool anonymous quotas ride the same policy evaluator. A public bot never inherits tools implicitly — `BotTool` association plus policy must both pass.

### Items for formal review

Legal/institutional: inter-institution data-sharing agreements before connecting registries; consequential-operation liability (Phase 4). Security: pen-test of the gateway + egress guard; the pre-existing findings flagged during exploration (dev seeder key in `DbSeeder.cs:35`, plaintext session tokens, LiveKit creds in `docker-compose.prod.yml`, ElevenLabs key in room metadata) should be triaged independently of this feature.

---

## 10. Observability Plan

- **Events** (via `ITelemetryService`, all with `TurnId`, `BotId`, `ConversationId`, org): `tool_call_started`, `tool_call_completed` (latency, outcome, httpStatus, bytes), `tool_call_failed` (errorCategory), `tool_call_blocked` (policy reason code); Phase 4: `tool_confirmation_requested`/`tool_confirmation_resolved`. Add completed/failed/blocked to `TelemetryService.FeedAllowlist`; route a user-visible display item through the existing content-event mechanism (`ConversationStreamPublisher.ContentEvents`) like `kb_retrieved`. Update `docs/telemetry-events.md` (repo convention).
- **Decision record, not chain-of-thought:** each execution row already captures the observable decision basis — triggering turn (`TurnId` → user message), selected tool, validated args, validation result, policy result + reason code, outcome, and the definition snapshot. That satisfies the "why did this happen" question deterministically.
- **`backend_completed.segments`** already includes tool spans [Confirmed] — no change needed for the waterfall; the feed UI gains labels for the new events.
- **Logs:** structured `ILogger` scopes (`ToolExecutionId`, `TurnId`); never log secrets or full payloads (redacted summaries only).
- **Metrics/dashboards (Phase 3):** admin pages query `tool_executions` aggregates — success/error/timeout rates, p50/p95 latency, calls per tool/bot/org, blocked-by-policy counts. A recurring Hangfire job evaluates alert rules (failure-rate spike, latency spike, auth failures, nearing call ceilings) and surfaces them **in the dashboard** — no email, per review.
- **Cost:** out of scope by review decision — external calls are assumed free in the current scope. `tool_executions` still records latency/bytes/call counts, so usage visibility exists if this ever changes; no token/cost capture work is planned.
- **Retention/privacy:** redaction is applied at write time (never store unredacted payloads). A retention policy was reviewed and deferred — rows are kept indefinitely for now; revisit before any high-volume or sensitive-data deployment.

---

## 11. Protocol Strategy

| Protocol | Gov-compat | Usefulness | Effort | Security surface | Voice latency fit | Verdict |
|---|---|---|---|---|---|---|
| REST/JSON | High (modern APIs) | Highest | Low (existing HttpClient patterns) | Well-understood | Good | **Phase 2 — first** |
| SOAP/WSDL | **Very high (LatAm registries)** | High for this market | Medium (envelope templating, XML mapping, WS-Security later) | XML parsing hardening needed | Acceptable | **Phase 4 — or pulled into Phase 2/3 if the Costa Rica meeting confirms SOAP** |
| GraphQL | Low in gov | Medium | Medium | Query-shape control needed | Good | Defer |
| gRPC | Very low in gov | Low here | Medium-high | Cert handling | Excellent | Defer |
| Webhooks / async callbacks | Medium | Medium (async results) | Medium (inbound surface + correlation) | New inbound attack surface | N/A (async) | Phase 4 |
| Message queues | Low (institution-internal) | Low now | High | Broker creds | N/A | Defer |
| Platform functions (built-in handlers) | N/A | Medium (fees calc, date logic) | Low — **the `IToolHandler` seam already supports this** [Confirmed] | Minimal | Excellent | Opportunistic — ship curated built-ins as templates |
| Controlled DB queries | Medium | Medium | High to do safely | Highest risk | Good | Defer; only ever as parameterized read-only service interfaces, never model-generated SQL |

**Abstractions required now** so later protocols don't force redesign: `ConnectorType` as an open string enum on `ToolConnection`; `IToolConnector` receiving `(definition, connection, validated args, credentials, ct)` and returning a normalized `ConnectorResult` (payload/object + status + diagnostics); per-connector config blobs (`AuthConfigJson`, operation fields) rather than protocol-specific columns; the gateway, policies, telemetry, and UI stay protocol-agnostic. WSDL/OpenAPI import writes *into* `ToolDefinition` rows — import is tooling, not runtime.

---

## 12. Alternatives and Trade-offs

| Choice | Options | Recommendation |
|---|---|---|
| Direct model function calling vs platform gateway | Model calls REST directly / gateway mediates | **Gateway.** Function calling remains the *selection* mechanism (already implemented); execution goes through validation/policy/credentials/observability. Direct calling is untenable for credentials and governance |
| Tool ownership | Bot-owned / institution-owned / platform-global | **Institution-owned** (`BaseEntity` gives it for free), associated to bots by reference (`BotTool`), shared across voice and chat by construction. Platform-global: **resolved (review)** — ship as seeded templates ("Plantillas", e.g. weather) instantiated per org, avoiding surgery on the reflection-based tenancy filter |
| Copy vs reference on association | Copy config into bot / reference | **Reference** + per-bot override fields; copies would fork governance |
| Tool + connection as one entity | Single entity / separate | **Separate.** The civil-registry case is exactly one connection ↔ many operations; credentials rotate independently of definitions |
| Manual config vs import | Manual / OpenAPI-WSDL import first | **Manual first** (matches the hand-built form culture of the codebase; import is Phase 4 tooling that fills the same rows). The parameter-table UI keeps manual entry tractable |
| Sync vs async execution | Block the turn / job + callback | **Synchronous within budget** (8–15 s) for v1; async job + "I'll have it shortly / tracking reference" pattern in Phase 4 for known-slow operations |
| Latest vs pinned versions | Always latest / pin per bot | **Resolved (review): always latest published, no version entity.** Execution snapshots + audit diffs provide traceability; pinning only if formal change control is ever demanded |
| One generic connector vs per-protocol | Generic "HTTP-ish" / typed connectors | **Typed connectors** behind one interface. A generic connector inevitably leaks protocol details into config and weakens validation |
| Free-form instructions vs structured policies | Prompt-only / structured | **Both, with structure winning:** AI-facing text guides selection; policies (confirmation, caps, auth) are enforced deterministically in the gateway — the model is never the authority on whether an operation may run |
| Built-in tools: config rows vs code-only | Migrate `kb_search` into the config system / keep code-only | **Resolved (review): code-only, always present, unmodifiable** (§4.5). Migrating it into config would add risk to a working path for zero benefit |

---

## 13. Phased Implementation Plan

**Phase 0 — prerequisites**
*External start gate:* the anonymous-access work currently being implemented (outside this plan) lands first — it defines the conversation auth-state model that tool policy reads. Then, small items: gate `/livekit/*` with a shared-secret header (backend + agent env); remove the seeded dev key in `DbSeeder.cs`; integrate `JsonSchema.Net` (resolved choice); request `tool:*` permission strings from GOB-ID (interim: `bot:admin` passes automatically).

**Phase 1 — Foundations (domain + registry)**
Entities/migrations for `ToolConnection`, `ToolDefinition`, `BotTool`, `ToolExecution`; DTOs + CRUD endpoints + permissions; bot-aware scoped `ToolRegistry` with built-in guardrails (§4.5 — `kb_search` always present, reserved names, regression tests); the three small `ChatTurnService` edits (§6.1); secrets via existing `IEncryptionService` behind `IToolCredentialProvider` (storage unchanged); basic frontend: connections + tools list/editor (General, IA, Técnica, Políticas tabs), bot Herramientas tab. *Exit criterion:* a configured tool appears in Gemini's tool list only for associated bots, and a config-free bot's payload is byte-identical to today's — both proven by tests (a stub connector suffices before REST lands).

**Phase 2 — REST execution (first real capability)**
`ToolExecutionGateway` (validation, policy, credentials, redaction, execution records, telemetry events + feed); `RestToolConnector` + `EgressGuard`; SSE keepalives + thinking-sound E2E on the voice path (§8); test console (probe + test-mode execution with redacted echo); execution history views; integration tests incl. voice-path timing; docs update (`docs/telemetry-events.md`, CHANGELOG). *Target first use cases:* read-only lookups — service-request status, registry consultation, weather.

**Phase 3 — Production hardening**
Per-org/per-bot call ceilings (stability/abuse protection — cost out of scope by decision); dashboards + Hangfire-evaluated alerts (no email, per review); `ToolTestCase` saved tests/regression runs; OAuth2 client-credentials auth if a target service requires it; optional tuning: windowed replay of stored tool results into history (§3 item 6) if re-call rates prove noisy. (Versioning, environment pairing, and approval-state expansion were reviewed and dropped — publishing with `tool:publish` *is* the approval.)

**Phase 4 — Protocol extensibility & consequential operations**
SOAP connector (envelope templates, XML→JSON mapping, WS-Security basics; **pull earlier if the civil-registry meeting confirms SOAP**); OpenAPI import (spec → prefilled definitions); async long-running pattern (Hangfire job + follow-up delivery + webhook/callback receiver with signed inbound verification); mTLS/client-cert support if the registry requires it; **consequential tools** — confirmation flow (§4.4), `confirm_pending_action` built-in, idempotency keys, `AddConversationPendingAction` migration — only when product green-lights state-changing operations (v1 is read-only by decision).

**Phase 5 — Advanced intelligence and analytics**
Tool-selection quality evaluation (blocked/mis-called/invalid-args analytics); automated regression on publish; policy rules by user attributes; configuration assistance (suggest schema from example responses); per-institution template marketplace.

---

## 14. Implementation Backlog

**Epic A — Security prerequisites** (Phase 0; starts after the in-flight anonymous-access work lands)
- A1 Agent channel auth: shared secret on `/livekit/*` — *api, agent* — AC: unauthenticated calls 401; agent E2E passes. Priority P0.
- A2 Remove hardcoded seed key (`DbSeeder.cs:35`); rotate exposed key — *infra* — P0.
- A3 Integrate `JsonSchema.Net` (resolved choice) — *infrastructure* — AC: invalid args rejected with structured errors; unit-tested. P0.

**Epic B — Tool domain model** (Phase 1)
- B1 Entities + EF configurations + 4 migrations — *core, infrastructure* — AC: org filter applies; audit diffs recorded for connections/definitions/bot-tools; secrets `[AuditMask]`ed. P0.
- B2 CRUD endpoints + DTOs + permissions (`tool:*`) — *api, core* — AC: secrets never serialized; OpenAPI updated; `npm run api:generate` clean. P0.
- B3 Bot-aware scoped `ToolRegistry` (+ `IToolsetContext` pair) + the three small `ChatTurnService` edits (§6.1) — *infrastructure* — AC: `GeminiChatService` untouched; kb_search regression green; unassociated bots see no configured tools; grounded-only fires only for kb_search. P0. Depends B1.
- B4 Credential provider over existing `IEncryptionService` (storage unchanged) — *infrastructure* — P0. Depends B1.
- B5 Built-in guardrails: reserved-name validation + `kb_search` regression suite (§4.5) — *infrastructure, tests* — AC: config-free bot's Gemini payload byte-identical to today; `kb_search` name rejected at creation in any casing. P0. Depends B1.

**Epic C — Admin UI foundations** (Phase 1)
- C1 Connections page (list/form/masked secrets/probe) — *frontend* — AC: settings.ts-style masking; probe shows redacted result. P1. Depends B2.
- C2 Tools list + tabbed editor + parameter-table schema builder — *frontend* — AC: schema round-trips; snake_case + reserved-name validation; Spanish copy. P1. Depends B2.
- C3 Bot Herramientas tab (incl. read-only system-tool row) — *frontend* — AC: toggle persists; visible with `bot:update`. P1. Depends B2.

**Epic D — Execution gateway + REST** (Phase 2)
- D1 Gateway pipeline (validate→policy→credentials→invoke→map→record) — *infrastructure* — AC: every outcome enum value covered by tests; execution rows written on all paths incl. blocks. P0. Depends B3, B4, A3.
- D2 `RestToolConnector` + `EgressGuard` + timeouts/size caps — *infrastructure* — AC: SSRF suite (metadata IP, DNS rebind, redirect) blocked; template injection attempts encoded inert. P0. Depends D1.
- D3 Voice-path keepalives + thinking-sound continuity — *api, tests* — AC: 12 s stubbed tool doesn't kill the agent stream in E2E; agent state stays "thinking" throughout (sound keeps looping). P1. Depends D1.
- D4 Telemetry events + feed allowlist + `docs/telemetry-events.md` — *infrastructure, docs* — P1. Depends D1.
- D5 Test console (test-mode execution + redacted echo) — *api, frontend* — P1. Depends D1, C2.
- D6 Execution history views (per-tool tab + drawer) — *frontend* — P2. Depends D4.
- D7 Integration test suite (external tool happy/timeout/error/policy paths through `/livekit/chat/turn`) — *tests* — P0. Depends D2.

**Epic E — Production hardening** (Phase 3): E1 per-org/per-bot call ceilings (stability, not cost); E2 dashboards + Hangfire alert job (no email); E3 saved test cases/regression runs; E4 OAuth2 client credentials (only if a target service needs it); E5 optional windowed tool-result replay into history (§3 item 6). All depend on Epic D.

**Epic F — Protocols & consequential operations** (Phase 4): F1 SOAP connector; F2 OpenAPI import; F3 async job pattern + callback receiver; F4 mTLS/client certs; F5 consequential tools — confirmation flow (§4.4) + idempotency keys + `AddConversationPendingAction` migration. F1 priority rises to P1 if the registry confirms SOAP.

Foundational work (Epics A–D) is deliberately protocol-agnostic; only Epic F is protocol-specific.

---

## 15. Decisions — Resolved and Remaining

### Resolved (2026-07-18 review)

| # | Decision | Resolution |
|---|---|---|
| 1 | GOB-ID `tool:*` permission strings | Keep as planned: define constants now, request GOB-ID addition in parallel; `bot:admin`/`superadmin` override keeps admins unblocked meanwhile |
| 2 | Secret storage | **No change** — existing AES `AppSetting`-style pattern; Key Vault migration out of scope |
| 3 | Civil registry protocol | Nothing to do until the meeting; REST first, SOAP held ready in Phase 4 |
| 4 | Anonymous bot access | **Being implemented now (outside this plan) and gates this feature's start.** Tools proceed only after it lands; `RequiresAuthenticatedUser` defaults to true and is evaluated against actual conversation auth state at execution time |
| 5 | Consequential tools | **Read-only only in v1**; confirmation flow deferred to Phase 4 |
| 6 | Approval authority | **The admin, per the permission model** — publishing requires `tool:publish`; no separate approval state; publishing *is* the approval |
| 7 | Platform-global tools | Templates ("Plantillas") instantiated per org, as recommended |
| 8 | Versioning + environment pairing | **Not now** — execution snapshots + audit diffs suffice; no version entity, no test/prod pairing |
| 9 | JSON Schema library | **`JsonSchema.Net`** (most actively maintained) |
| 10 | Execution retention | No retention policy now; redaction at write time stays mandatory |
| 11 | Call ceilings | As recommended: configurable conservative defaults (stability/abuse protection — cost is explicitly not a concern in this scope) |
| 12 | Alerting channel | Dashboard + Hangfire-evaluated alerts; no email |

Additional review directions incorporated: built-in `kb_search` must be always present and unmodifiable (§4.5); changes to the working pipeline must be minimal (§6.1); the existing thinking/loading sound is the latency-masking mechanism for tool execution (§8).

### Still open (external dependencies)

| Item | Owner / trigger | Impact |
|---|---|---|
| Civil registry protocol, auth, network requirements | Costa Rica registry meeting | Decides SOAP timing (Phase 4 → earlier), mTLS, possible static-IP/VPN egress needs |
| GOB-ID delivery of `tool:*` permissions | GOB-ID team | Until delivered, only `bot:admin`/`superadmin` can administer tools |
| Anonymous-access completion | In-flight implementation | Start gate for Phase 1; defines the auth-state model tool policy reads |

---

## Appendix — Key file index

Backend seam: `BotFactory.Core/Tools/{IToolHandler,IToolRegistry,FunctionDeclaration}.cs` · `BotFactory.Infrastructure/Tools/{ToolRegistry,KbSearchToolHandler}.cs` · `BotFactory.Infrastructure/Services/{ChatTurnService,GeminiChatService}.cs` · `BotFactory.Api/Program.cs:177-179`
Security/patterns to reuse: `AppSetting.cs` + `AppSettingService.cs` + `AesEncryptionService.cs` (secrets) · `KbUrlValidator.cs` (SSRF) · `KbScraperClient.cs` (cross-service client + retry) · `ProviderErrorClassifier.cs` (error taxonomy) · `BotFactoryDbContext.cs` (tenancy + audit) · `TenantContext.cs` (scoped context-setter idiom the registry mirrors)
Voice path: `apps/agent/src/bot_factory_agent/{agent.py,llm_backend.py,relays.py,config.py}` · `BotFactory.Api/Endpoints/LiveKitEndpoints.cs` · `BotFactory.Infrastructure/Services/LiveKitService.cs`
Telemetry: `TelemetryService.cs` · `ConversationHub.cs` · `ConversationStreamPublisher.cs` · `docs/telemetry-events.md`
Frontend patterns: `features/settings/settings.ts` · `features/bots/{bot-edit.ts,bot-edit.store.ts,bot-edit-kb.ts}` · `features/conversations/{conversation-item.ts,conversation-feed.ts}` · `layout-shell.ts` · `src/app/api` (ng-openapi-gen)
