# Anonymous Access, Sharing & Embedding for Bots — Planning Document

## Context

The platform ("Bot Factory" / **VOZ**) lets government institutions configure AI voice/chat bots backed by a per-bot Knowledge Base. Today **every** deployed-bot conversation requires the end user to log in through **GOB-ID**. The goal is to let an authorized platform user mark a published bot as **public** (usable with no login) and to **embed** that bot in external websites via an `<iframe>` — both as *configurable, per-bot, reusable* platform capabilities, without hardcoding anything for a specific bot, institution, or site. Administration/configuration stays fully protected; only the *deployed bot experience* becomes public.

This document is exploration + plan only. **No code is to be written until explicitly instructed.**

**Product decisions already taken (this session):**
1. **Access model = a boolean `RequiresAuthentication`** (not an extensible policy enum). Simplest field that meets the requirement; migration path to a richer policy noted in §11.
2. **Public/embed URL keyed on an opaque `PublicId`**; the human `UrlSlug` stays a per-org label. This fixes a real cross-org slug-shadowing bug (below).
3. **Public voice ships together with public text in the first release.** Consequence: the abuse/cost guardrails that would otherwise be Phase 3 (agent-callback hardening, basic rate limiting, concurrent-room caps) become **prerequisites of the first release**, because anonymous voice is the most expensive and abuse-prone surface.

---

## 1. Executive summary

- **How access works today.** Auth is a custom scheme (`BfSession`): the browser holds an opaque `bf_session` cookie → a DB `Sessions` row holds the real GOB-ID JWT ([SessionAuthHandler.cs](apps/backend/BotFactory.Api/Auth/SessionAuthHandler.cs)). `TenantResolutionMiddleware` fills a `TenantContext` (OrganizationId, UserId, Permissions) from JWT claims — **only when authenticated**. Every entity is org-scoped by a global EF query filter. The only anonymous surface is `GET /api/bots/by-slug/{slug}` (published-only lookup for the chat landing page).
- **How the deployed frontend loads.** One Angular app serves both admin and runtime. Route `chat/:slug` (no auth guard) → the [`Chat`](apps/frontend/src/app/features/chat/chat.ts) component resolves the bot by slug, then a `stage()` computed shows an **`auth-required` login gate** whenever `!isAuthenticated()`. `onStartSession()` calls `POST /api/bots/{id}/chat`, which is behind `.RequireAuthorization()` — **this is the login blocker.**
- **How LiveKit sessions init.** `POST /bots/{id}/chat` creates a `Conversation` and, for voice, calls `LiveKitService.CreateRoomAsync` — server-mints a room + token (identity `user-{botId}-{conversationId}`, 1h TTL) and puts bot config + the ElevenLabs key into `room.metadata`. The Python agent is **fully identity-agnostic** — it reads everything from room metadata and never sees the user. It posts back to **unauthenticated** `/livekit/chat/turn` and `/livekit/telemetry/events`, keyed only by an enumerable integer conversation id.
- **Recommended access model.** Add `RequiresAuthentication` (bool, **default `true`** → backward compatible) on `Bot`. "Public" = published + `RequiresAuthentication=false`. Embedding is a **separate** capability: `EmbedEnabled` (bool, default false) + `AllowedEmbedOrigins`. Add an opaque `PublicId` as the canonical public/embed key.
- **Recommended sharing/embedding.** A backend-served `GET /api/embed/{publicId}` entry that injects a **per-bot `Content-Security-Policy: frame-ancestors`** header and serves the SPA shell; static `frame-ancestors 'none'` for direct routes. Admin "Compartir" section generates the direct link + an `<iframe … allow="microphone; autoplay">` snippet with a live preview.
- **Anonymous session model.** A **server-signed, short-lived public JWT** (stateless, no `Sessions` row) carrying `organization_id`=bot's org, `sub`=`anon:{opaqueId}`, `permissions=["public:chat"]`, `token_type=public`. Presented via the existing `Authorization: Bearer` / `?access_token=` seam. Anonymous conversations belong to the **bot's org**, not the caller.
- **Main risks / open decisions.** (a) Unauthenticated agent callbacks become internet-abusable the moment a bot is public — must be hardened first. (b) A naive public principal would leak the authenticated `/bots` surface — public REST must be a separate, inert surface. (c) Cost/abuse of anonymous voice with no metering today. (d) Cross-origin mic-in-iframe browser quirks (esp. Safari/iOS). (e) Anonymous transcript/PII retention needs legal sign-off for a government context.

---

## 2. Current-state findings (confirmed, with references)

**Stack.** .NET 10 minimal-API backend (`apps/backend`: `BotFactory.Api` / `.Core` / `.Infrastructure`), Angular 21 frontend (`apps/frontend`), Python LiveKit agent (`apps/agent`), plus `apps/kb-scraper`. SQL Server + EF Core, Hangfire, SignalR, self-hosted LiveKit. Azure DevOps repo.

**Bot domain model.** Single [`Bot`](apps/backend/BotFactory.Core/Entities/Bot.cs) aggregate — all config is columns on it. `PublishedState` is a **string** (`"draft"`/`"published"`) via [BotStates.cs](apps/backend/BotFactory.Core/Entities/BotStates.cs) (const strings + `IsValid`). `EnableChat`/`EnableVoice` capability flags. **No access/auth/embed fields exist.** DTOs in [BotDtos.cs](apps/backend/BotFactory.Core/DTOs/BotDtos.cs) (`CreateBotRequest`, `UpdateBotRequest`, `BotResponse`); partial update via `Bot.ApplyUpdate`.

**Persistence & CRUD.** Minimal-API [BotEndpoints.cs](apps/backend/BotFactory.Api/Endpoints/BotEndpoints.cs); `/bots` group is `.RequireAuthorization()`; writes gated by `.RequirePermission("bot:update"|"bot:delete"|"bot:publish")`. EF [BotConfiguration.cs](apps/backend/BotFactory.Infrastructure/Data/Configurations/BotConfiguration.cs) → table `bots`, **unique index on `(UrlSlug, OrganizationId)`** filtered to non-empty. DB auto-migrates + seeds on startup ([Program.cs](apps/backend/BotFactory.Api/Program.cs)). No FluentValidation — validation is inline (`BotInteractionModes.IsValid`, `BotStates.IsValid`).

**General config page.** [bot-edit-general.ts](apps/frontend/src/app/features/bots/bot-edit-general.ts) — reactive form; shows the live public URL `{productUrl}/chat/{slug}`; saves via generated `updateBot`. Publish/unpublish/delete live in the parent [bot-edit.ts]. The API client under `apps/frontend/src/app/api/` is **auto-generated** (`ng-openapi-gen`) — regenerate after backend model changes.

**Auth & authorization.** [SessionAuthHandler.cs](apps/backend/BotFactory.Api/Auth/SessionAuthHandler.cs) resolves the token from `Authorization: Bearer <guid>`, `?access_token=<guid>` (for `/hubs` WS upgrades), or the `bf_session` cookie; validates the stored JWT against a singleton `TokenValidationParameters` ([Program.cs](apps/backend/BotFactory.Api/Program.cs)). GOB-ID provider is hand-rolled ([GobIdAuthProvider.cs](apps/backend/BotFactory.Infrastructure/Auth/GobIdAuthProvider.cs)) — **no OIDC middleware**. Authorization = `.RequireAuthorization()` + custom `.RequirePermission()` filter over `TenantContext.Permissions` (superadmin/bot:admin bypass). Permission constants in [Permissions.cs](apps/backend/BotFactory.Core/Auth/Permissions.cs) — **no `public:chat` exists**. Draft preview gated by `CanViewDraftBots()` ([TenantContextExtensions.cs](apps/backend/BotFactory.Core/Auth/TenantContextExtensions.cs)).

**Public routes.** Only `GET /api/bots/by-slug/{slug}` and the thinking-sound download are `AllowAnonymous`. `BotService.GetBySlugAsync` uses `IgnoreQueryFilters()` (no tenant for anon). Frontend public routes (`chat/:slug`, `login`, `auth/callback`) sit **outside** the `authGuard` shell ([app.routes.ts](apps/frontend/src/app/app.routes.ts)).

**Deployed frontend & the login gate.** [chat.ts](apps/frontend/src/app/features/chat/chat.ts): `stage()` returns `auth-required` when `!auth.isAuthenticated()` (a full-screen "Inicia sesión…" gate). `onStartSession()` → `POST /bots/{id}/chat` → `conversationId` (+ LiveKit `token`/`url` for voice) → opens SignalR and/or LiveKit.

**Voice vs chat — same auth, different transport.** Both create one `Conversation` via `POST /bots/{id}/chat?mode=`. Text streams over SignalR `/hubs/conversation` (`.RequireAuthorization()`, authed via `?access_token=`); voice rides LiveKit. `Conversation.UserId = tenant.UserId`; `OrganizationId` auto-stamped.

**LiveKit.** [LiveKitService.cs](apps/backend/BotFactory.Infrastructure/Services/LiveKitService.cs) `GenerateToken` (identity `user-{botId}-{conversationId}`, grants `RoomJoin+CanPublish+CanSubscribe+CanPublishData`, 1h TTL). Bot config + **plaintext ElevenLabs key** go in `room.metadata`. Room name == conversation id (resume: `{id}-r{ts}`). Agent callbacks in [LiveKitEndpoints.cs](apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs) are **unauthenticated**, keyed by integer `ConversationId`, with an explicit `// TODO: gate this endpoint`.

**Session/conversation & observability.** `Conversation`/`Message`/`TelemetryEvent` entities; DB-backed telemetry pipeline (agent → `/livekit/telemetry/events` → `AgentEventMapper` → `TelemetryService.EmitAsync`, with a live admin feed over SignalR). Per-turn `TurnId` correlation + stage latencies. `AuditLog` auto-written for `IAuditable` (Bot is auditable; Conversation is not). **No cost/usage metering, no APM SDK.**

**Hosting/security topology (critical for embedding).**
- Prod: frontend is a **separate Azure Static Web App** at `https://bf.example.com`; backend at `wa-voz-api-dev-…azurewebsites.net`. Frontend [environment.ts](apps/frontend/src/environments/environment.ts) has `apiUrl: ''` → **`/api/*` is same-origin, proxied by the SWA to the backend**; `hubsUrl` points directly at the backend. SPA fallback via [staticwebapp.config.json](apps/frontend/public/staticwebapp.config.json) (excludes `/api/*`, `/hubs/*`).
- CORS: one default policy `WithOrigins(AllowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()` ([Program.cs](apps/backend/BotFactory.Api/Program.cs)).
- **No CSP, no `X-Frame-Options`, no `frame-ancestors`, no rate limiting, no quotas** anywhere.

---

## 3. Gaps & constraints

1. **`ConversationAuthorization.CanView` blocks anonymous text chat.** ([ConversationAuthorization.cs](apps/backend/BotFactory.Core/Auth/ConversationAuthorization.cs) l.16–17) — the owner path requires `tenant.Has(bot:chat)`. An anonymous principal has empty permissions → `ConversationHub.SubscribeConversation`/`SendTurn` throw *forbidden*. Text is the primary anon path and is closed until `CanView` accepts a `public:chat` claim. **(verified)**
2. **A valid public principal would leak the authenticated `/bots` surface.** The `/bots` group is only `.RequireAuthorization()`, and two endpoints have **no** `.RequirePermission`: `GET /bots/` (list — enumerates all org bots incl. drafts) and `POST /bots/{id}/chat` (start chat against *any* bot in the org, ignoring its access mode). So one public bot's token could reach the whole tenant catalog. **(verified in [BotEndpoints.cs](apps/backend/BotFactory.Api/Endpoints/BotEndpoints.cs))**
3. **Unauthenticated agent callbacks become internet-abusable when a bot is public.** `/livekit/chat/turn` and `/livekit/telemetry/events` are keyed only by an enumerable int id and have no auth — anyone could drive LLM/TTS spend or forge conversation state. The existing `TODO` is **not optional** once bots are public.
4. **Cross-org slug shadowing.** Slug uniqueness is per-`(slug, org)`; `GetBySlugAsync` does `IgnoreQueryFilters().FirstOrDefault()`. Two orgs can both *publish* the same slug and org B can hijack org A's global public URL. Motivates the opaque `PublicId` (decision #2).
5. **Tight coupling to an authenticated principal.** `TenantResolutionMiddleware` only hydrates on `IsAuthenticated`; the EF query filter and `Conversation.UserId` ownership assume a populated tenant. Anonymous needs org-from-bot + synthetic identity (precedent exists: `ITenantContextSetter.SetTenant` + `IgnoreQueryFilters`).
6. **Frontend assumes login.** `stage()` returns `auth-required` for all anonymous visitors; `auth.interceptor.ts` calls `logout()` on any non-`/auth/` 401 → an anon 401 would bounce the visitor into a nonexistent login. Both need a public branch/carve-out. **(verified)**
7. **Third-party cookies die in iframes.** `bf_session` is `SameSite=None` but Safari ITP blocks it and Chrome partitions it → the embedded anonymous flow must be **cookie-less** (bearer token).
8. **No per-bot header control at the static host.** SWA `staticwebapp.config.json` supports only *static* headers; per-bot `frame-ancestors` can't be computed there → needs a backend-served embed entry.
9. **No metering/limits.** No rate limiting, quotas, or cost tracking — a hard gap given public voice ships in release 1.

---

## 4. Proposed target architecture

**Principle:** public access and embedding are two independent, per-bot, opt-in capabilities layered on the existing published-bot flow. Reuse the anonymous-lookup + `SetTenant` + `IgnoreQueryFilters` patterns already in the codebase. Keep the public surface **physically separate and inert** on the authenticated surface.

| Concern | Design |
|---|---|
| **Access flag** | `Bot.RequiresAuthentication` (bool, default `true`). Public = `PublishedState=published` **and** `RequiresAuthentication=false`. |
| **Public identity** | Opaque `Bot.PublicId` (e.g. base62 ULID, ~22 chars, non-enumerable). Canonical public/embed key. `UrlSlug` stays a per-org human label. |
| **Anonymous principal** | Server-signed **public JWT** (stateless): `organization_id`=bot's org, `sub`=`anon:{opaqueId}`, `permissions=["public:chat"]`, `bot_id`, `token_type=public`, **TTL 15 min (configurable via app config)**. Presented via `Authorization: Bearer` / `?access_token=`. |
| **Public REST** | New `/api/public/**` group, `AllowAnonymous`, resolves bot by `PublicId`, enforces published + `!RequiresAuthentication` (404 otherwise), `SetTenant(bot.OrganizationId)`, mints anon identity + public token, writes `Conversation.UserId="anon:{id}"`. |
| **Authenticated flow** | Unchanged. Plus: close the two permission-less `/bots` endpoints; reject `token_type=public` outside `/api/public/**` + `/hubs`. |
| **LiveKit** | Token stays backend-minted & identity-free. For anon: consider shorter TTL and dropping `CanPublishData` (text goes over SignalR). Add `accessMode`/`isAnonymous` + source info to `room.metadata` for the agent/telemetry. |
| **Embed entry** | `GET /api/embed/{publicId}` (already proxied by SWA) verifies published + public + `EmbedEnabled`, emits per-bot `Content-Security-Policy: frame-ancestors <AllowedEmbedOrigins>`, and reverse-proxies the SPA `index.html` (header-inject only). |
| **Direct-link framing** | Static `frame-ancestors 'none'` + `X-Frame-Options: DENY` via SWA `globalHeaders` — direct `/chat/*` can't be framed. |
| **Guardrails (release 1)** | `AddRateLimiter` on `/api/public/**` (per-IP + per-bot); concurrent-room cap per bot; HMAC/shared-secret on agent callbacks + non-enumerable conversation ids. |
| **Observability** | Tag every public conversation/telemetry event with access mode, anon id, source URL, parent origin, surface (direct/iframe). |

---

## 5. Data-model changes

New columns on `Bot` (one migration, following the established one-feature-per-migration pattern):

| Field | Type | Default | Notes |
|---|---|---|---|
| `RequiresAuthentication` | `bool` | `true` | Backward compatible — all existing bots stay login-required. |
| `PublicId` | `string` (indexed **globally unique**, non-empty) | generated on create/publish | Opaque canonical public/embed key. |
| `EmbedEnabled` | `bool` | `false` | Independent of `RequiresAuthentication`. |
| `AllowedEmbedOrigins` | `string` (JSON/CSV) or child table `AllowedEmbedDomain` | empty | Origin allowlist. Empty + EmbedEnabled ⇒ decide "block" vs "any" (see §14). |

- Extend `BotResponse` / `UpdateBotRequest` / `Bot.ApplyUpdate` + inline validation. **Do not** expose secrets; `PublicId` is safe to expose (it's the public key).
- Add `public:chat` to [Permissions.cs](apps/backend/BotFactory.Core/Auth/Permissions.cs) (claim-only; never granted to GOB users).
- **Conversation/telemetry attribution:** add `AccessMode`/`IsAnonymous` (+ optionally `SourceUrl`, `ParentOrigin`, `Surface`) to `Conversation` and/or `TelemetryEvent`. `Conversation.UserId` already holds a string → store `anon:{opaqueId}`.
- **PublicId generation** must run for existing published bots via a data migration/backfill.
- Access changes are audited automatically (Bot is `IAuditable`); consider surfacing a dedicated access-history view later.

---

## 6. Backend changes

1. **Model + migration** (§5) — new `Bot` fields, indices, backfill; DTO + `ApplyUpdate` + validation; regenerate OpenAPI client.
2. **Public token issuer** — a small service that mints the stateless public JWT (self-signed with a dedicated key; mirror the `FakeAuthProvider` self-signing precedent). Register its issuer/audience/key into the `IssuerSigningKeys`/`ValidIssuers`/`ValidAudiences` lists in [Program.cs](apps/backend/BotFactory.Api/Program.cs).
3. **`SessionAuthHandler`** — add a branch: if `rawToken` parses as a **JWT** (not a session GUID), validate it directly against `TokenValidationParameters` and build the principal **statelessly** (no `Sessions` row). Everything downstream (`TenantResolutionMiddleware`, hub `HydrateTenant`) then works unchanged.
4. **Public endpoints** — `MapPublicBotEndpoints`: `POST /api/public/bots/{publicId}/chat` (+ `/resume`, `/close`, and a public `GET /api/public/bots/{publicId}` resolve). `AllowAnonymous`; resolve by `PublicId`; enforce published + `!RequiresAuthentication` (else **404**); `SetTenant(bot.OrganizationId)`; mint anon identity + public token; sanitize error messages (no "go to Settings" leaks). Mirror the existing `/chat` and `/resume` logic.
5. **Close the authenticated leaks** — add `.RequirePermission(BotRead)` to `GET /bots/` and `.RequirePermission(BotChat)` to `POST /bots/{id}/chat`; add a filter/middleware that rejects `token_type=public` anywhere except `/api/public/**` and `/hubs`.
6. **`ConversationAuthorization.CanView`** — extend the owner clause to `owner && (bot:chat OR public:chat)`.
7. **Harden agent callbacks** ([LiveKitEndpoints.cs](apps/backend/BotFactory.Api/Endpoints/LiveKitEndpoints.cs)) — HMAC shared-secret header (or LiveKit webhook signing) + make conversation ids **non-enumerable** (GUID, or bind a per-room secret in metadata). **Release-1 prerequisite.**
8. **Embed entry** — `GET /api/embed/{publicId}`: validate published + public + `EmbedEnabled`; set per-bot `Content-Security-Policy: frame-ancestors`; reverse-proxy SWA `index.html` (cache by ETag; inject header only — do **not** add `default-src`/`connect-src`, which would break LiveKit `wss://`). Re-check `Origin`/`Referer` against `AllowedEmbedOrigins` at token issuance (defense-in-depth).
9. **Rate limiting + caps** — `AddRateLimiter`/`UseRateLimiter` scoped to `/api/public/**` (per-IP + per-bot); concurrent-room cap per bot before `CreateRoomAsync`.
10. **CORS** — the iframe path is same-origin with our API, so no per-embedder CORS is needed for v1; keep the existing allowlist. (Per-embedder CORS is only needed for the future script-widget/web-component channels.)
11. **Interceptor/hub** — ensure the hub accepts the public JWT via `?access_token=` (works once #3 lands).

---

## 7. Frontend changes

1. **General config page** ([bot-edit-general.ts](apps/frontend/src/app/features/bots/bot-edit-general.ts)) — add an **"Acceso"** control (toggle: *Autenticado (GOB-ID)* ↔ *Público*) with explanatory text + a warning that public bots are reachable by anyone with the link. Gate the control on publish/manage permission.
2. **New "Compartir" section** — direct link (`{productUrl}/chat/{publicId}`) with copy-to-clipboard; a read-only "requires authentication / public" indicator; an `<iframe>` snippet generator (`allow="microphone; autoplay"`, responsive width/height, `loading="lazy"`); a **live embed preview**; contextual warnings for draft/archived/authenticated/embed-disabled/no-permission states. Reuse `@public-intelligence/ui-core` (`ConfirmModalComponent`, `AlertBannerComponent`, toasts via `ngx-sonner`); build the copy/snippet/preview controls (none exist yet).
3. **Embed domain config** (Phase 2/3) — manage `AllowedEmbedOrigins`.
4. **Public chat experience** ([chat.ts](apps/frontend/src/app/features/chat/chat.ts)) — branch `stage()` on `bot.requiresAuthentication === false` to **skip** the `auth-required` gate and call the `/api/public/**` endpoints; hold the public token **in memory**; pass it as `Bearer` + `?access_token=`. Add an embedded layout variant (compact, branding-aware).
5. **Interceptor carve-out** ([auth.interceptor.ts](apps/frontend/src/app/core/interceptors/auth.interceptor.ts)) — do **not** `logout()` on 401 for `/api/public/**`.
6. **States** — loading, permission-denied, draft ("publica y marca público para compartir"), embed-disabled, error.

---

## 8. LiveKit changes

- **Anon participant identity** — keep `user-{botId}-{conversationId}` (already anonymous) or add an `anon:{id}` marker; put `accessMode`/`isAnonymous` + `sourceUrl`/`parentOrigin` into `room.metadata` so the agent and telemetry can distinguish public traffic. Agent code needs **no auth awareness** (it already reads metadata only).
- **Token creation** — stays 100% backend-side; for anon, consider a shorter TTL and dropping `CanPublishData` (confirm the web client doesn't need the data channel — text is on SignalR).
- **Room/agent/session correlation** — unchanged (room name == conversation id); the hardened callbacks (§6.7) re-associate agent events safely.
- **Reconnection / TTL** — re-mint the public token on resume; keep resume allowed while the conversation is Active/Paused within TTL.
- **Iframe mic** — see §9; requires `allow="microphone; autoplay"` on the iframe + a start gesture inside the frame.
- **Abuse controls** — rate-limit the public room-create path; concurrent-room cap per bot; verify published + public + origin **before** `CreateRoomAsync`.

---

## 9. Security & privacy assessment

**Threats / abuse cases → controls:**
- *Tenant-surface leak via public token* → separate `/api/public/**` group; close the two permission-less `/bots` endpoints; `token_type=public` guard. **(§6.5)**
- *Agent-callback abuse (spend, forged state)* → HMAC/signing + non-enumerable ids. **(§6.7, release-1 prerequisite)**
- *Cross-org URL hijack* → opaque `PublicId` + global-unique index. **(§5)**
- *Clickjacking / unwanted framing* → per-bot `frame-ancestors` on the embed entry; `frame-ancestors 'none'` + `X-Frame-Options: DENY` on direct routes. **(§6.8)**
- *Token forgery / ownership bypass* → server-signed token, server-issued anon id (never client-chosen). **(§4)**
- *Cost/DoS via anonymous voice* → rate limits + concurrent-room caps + quotas. *(An interactive challenge/CAPTCHA is deferred per product decision — revisit only if abuse emerges.)* **(§6.9)**
- *KB/data exposure* → public access is an explicit per-bot opt-in on a **published** bot; the configuring platform user owns that choice. *(No separate sensitive-bot approval gate — product decision: there are no sensitive bots requiring extra approval.)*
- *Info leak in errors* → sanitize anon-facing error messages.

**Privacy:** anonymous transcripts are stored under the bot's org with `anon:` user, at the same retention as authenticated conversations, with **no** linkage between anonymous and authenticated identities. A visible consent/privacy notice is shown in the public/embedded UI. *(Proceeding per product decision; a legal review of the notice wording for the government context remains advisable but is not a blocker.)*

---

## 10. Observability plan

- **Events/fields to add** to `Conversation`/`TelemetryEvent`: access mode (public/authenticated), `IsAnonymous`, `AnonSessionId`, `SourceUrl`, `ParentOrigin`, `Surface` (direct/iframe), LiveKit room/participant refs (already inferable), plus token-issuance/auth/rate-limit failure events.
- **Reuse** the existing telemetry pipeline (`AgentEventMapper` → `TelemetryService.EmitAsync`) and the live admin feed; extend the feed allowlist for the new failure events.
- **Dashboards** (later): public vs authenticated volume, direct vs embedded, usage by parent domain, failed session creation, per-bot cost (needs new cost metering — a current gap), anonymous-session volume, suspicious traffic, LiveKit latency/failures.
- **Correlation** — keep the per-turn `TurnId`; add a correlation id spanning the public token → conversation → room.
- **Redaction** — apply the existing `[AuditMask]` discipline; ensure `SourceUrl`/`ParentOrigin` never carry query-string PII.

---

## 11. Alternatives & trade-offs (recommendation in **bold**)

- **Boolean `RequiresAuthentication` vs access-policy enum** → **Boolean** (product decision). Simplest; if institution/domain/hybrid modes are needed later, migrate to a string `AccessMode` (mirroring `BotStates`) — the boolean maps cleanly to `authenticated`/`public`.
- **Public URL: bot id / slug vs opaque id** → **Opaque `PublicId`** (product decision). Fixes cross-org shadowing, resists enumeration; slug stays a human label.
- **Iframe vs script widget vs web component** → **Iframe first.** Strong isolation, minimal CORS (same-origin with our API), fastest to ship. Script/web-component later (Phase 4) for launcher UX — they need per-embedder CORS + `postMessage`.
- **Open embedding vs domain allowlist** → **Allowlist**, with embedding **disabled by default** and independent from public-link access.
- **Cookie continuity vs stateless token** → **Stateless signed token in memory.** Cookies are unreliable third-party in iframes and a client-chosen id breaks ownership.
- **Authenticated experience inside vs outside iframe** → **Outside for v1** (GOB-ID redirects + third-party cookies are hostile inside frames). Embedding targets **public** bots first; authenticated-in-iframe is a Phase 4 investigation.

---

## 12. Phased implementation plan

**Phase 0 — Schema & config (unblocks everything).** `Bot` fields (`RequiresAuthentication`, `PublicId`, `EmbedEnabled`, `AllowedEmbedOrigins`) + migration + backfill; DTO/`ApplyUpdate`/validation; regenerate client; admin toggle in the general page.

**Phase 1 — Public access foundation (text + voice).** Public JWT issuer + `SessionAuthHandler` JWT branch + `tokenValidationParams` wiring; `/api/public/**` chat/resume/close (resolve by `PublicId`, enforce published+public, `SetTenant` from bot org, anon identity); `public:chat` + `CanView` clause; **close the permission-less `/bots` endpoints + `token_type` guard**; **harden agent callbacks (HMAC + non-enumerable ids)**; **basic rate limiting + concurrent-room cap** (required because voice ships now); frontend public branch + interceptor carve-out; direct public link in the share section.

**Phase 2 — Sharing & embedding.** `GET /api/embed/{publicId}` with per-bot `frame-ancestors` + `index.html` reverse-proxy; SWA `globalHeaders` for direct-route framing lock; origin allowlist at token issuance; iframe snippet generator + live preview; responsive embedded layout; mic-permission handling (§9); embed-domain config UI.

**Phase 3 — Governance & security hardening.** Advanced/tunable rate limits; per-bot & per-institution quotas + cost metering; anon transcript retention/PII policy + consent notice; active-session revocation (LiveKit room termination); `PublicId` regeneration action; dashboards & alerts. *(No abuse challenge and no sensitive-bot approval gate — out of scope per product decision; revisit challenge only if abuse emerges.)*

**Phase 4 — Additional channels.** Script widget / web component (per-embedder CORS + `postMessage`); floating launcher; voice-only widget; QR access; authenticated-in-iframe SSO; per-bot theming/branding.

*(Resequencing vs. the generic template: agent-callback hardening + basic rate limiting move into Phase 1 because public **voice** ships in release 1.)*

---

## 13. Implementation backlog (by epic)

**Epic A — Access model & data.** Add `Bot` fields + migration + `PublicId` backfill (dep: none; AC: existing bots default to authenticated, each gets a unique `PublicId`; risk: backfill on live data). DTO/validation/client regen. Add `public:chat` permission.

**Epic B — Anonymous auth.** Public JWT issuer (AC: token validates statelessly, carries org/anon/`public:chat`/`token_type`). `SessionAuthHandler` JWT branch (dep: issuer). `CanView` clause (AC: anon owner can use the hub; authed behavior unchanged). Close `/bots` leaks + `token_type` guard (AC: public token 403s on `/bots/**`; regression tests for GOB users).

**Epic C — Public endpoints.** `/api/public/**` chat/resume/close/resolve (dep: B; AC: anonymous text **and** voice work end-to-end for a published+public bot; 404 for draft/private; sanitized errors). Frontend public branch + interceptor carve-out.

**Epic D — Agent-callback hardening.** HMAC/signing + non-enumerable ids (dep: none; AC: unsigned/forged callbacks rejected; **release-1 gate**).

**Epic E — Rate limiting & caps.** `/api/public/**` limiter + concurrent-room cap (dep: C; AC: bursts throttled; caps enforced pre-`CreateRoomAsync`).

**Epic F — Embedding.** `/api/embed/{publicId}` + per-bot `frame-ancestors` + reverse-proxy (dep: A, C). SWA direct-route framing lock. Origin allowlist at token issuance. Share UI (link, snippet, preview) + embed-domain config. Mic-permission handling + docs (AC: bot loads and voice works inside an allowed cross-origin iframe on Chrome/Edge/Safari; blocked on non-allowlisted origins).

**Epic G — Observability.** Access-mode/anon/source tagging on conversations + telemetry; new failure events; dashboards (dep: A, C).

**Epic H — Governance (Phase 3).** Quotas/cost metering; retention/PII policy + consent notice; active-session revocation; `PublicId` regeneration. *(Abuse challenge and sensitive-bot approval are out of scope per product decision.)*

---

## 14. Decisions (resolved with product)

All eight open decisions have been resolved:

1. **Empty `AllowedEmbedOrigins` semantics** → **Block all** when `EmbedEnabled` — origins must be explicitly listed (no accidental allow-all).
2. **Sensitive bots** → **No special handling.** There are no bots requiring extra approval; the configuring platform user owns the public-access choice. No hard-block, no approval gate.
3. **Anonymous transcript retention + PII + consent** → **Proceed with recommendation:** same retention as authenticated, under the bot's org, visible consent/privacy notice in the public/embedded UI, **no** anon↔identity linkage. (Legal review of the notice wording advisable but not a blocker.)
4. **Anon token TTL & resume window** → **15-minute access token, made configurable** (app config), re-minted on resume while the conversation is Active/Paused.
5. **Anti-abuse for anonymous voice** → **Rate-limit always; no interactive challenge/CAPTCHA yet** — revisit only if abuse emerges.
6. **Revocation of active public sessions** → **Block-new for v1**; active LiveKit room termination deferred to Phase 3.
7. **URL stability** → `PublicId` **stable**, with an explicit "regenerate" action (invalidates old embeds) as a later Phase 3 feature.
8. **Direct public URL shape** → Reuse **`{productUrl}/chat/{publicId}`** (generalize the resolver to accept `PublicId`).

---

## Verification (when implementation begins)

- **Backend build/tests:** `dotnet build` + `dotnet test` in `apps/backend`; add tests covering (a) anon `CanView` allows `public:chat` owner, authed unchanged; (b) public token 403s on `/bots/**`; (c) `/api/public/**` returns 404 for draft/private bots; (d) agent callbacks reject unsigned requests.
- **End-to-end (local):** run the app via the project run/verify skills; create a bot, publish, mark public; open the direct link **in a logged-out browser** and confirm text + voice work without a login gate; confirm a draft/private bot 404s anonymously.
- **Embedding:** host a throwaway page on a different origin with the generated `<iframe … allow="microphone; autoplay">`; verify it loads only when the origin is allowlisted, mic works in Chrome/Edge/Safari, and framing is refused on a non-allowlisted origin and on the direct `/chat` URL.
- **Regression:** confirm existing authenticated GOB-ID chat (text + voice, resume, close) is unchanged; regenerate the OpenAPI client and confirm no `number | string` unions leaked.
- **Observability:** confirm public conversations are attributed to the bot's org and tagged public/anonymous in telemetry.
