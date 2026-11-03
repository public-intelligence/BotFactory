# P1 Test Coverage — Implementation Plan

**Status:** Implemented  
**Depends on:** [P0](test-coverage-p0.md) (public-token + auth helpers — already in working tree)  
**Blocks:** CI for [P2](test-coverage-p2.md) FE/agent runs  
**Related:** coverage analysis canvas

## Goals / out of scope

**Goals**
- Tools admin CRUD HTTP (not turn execution).
- Middleware/filters: tenant, public-token scope, permission filter.
- Frontend auth stack Vitest (guard / service / interceptor).
- Bot publish/unpublish + logo / thinking-sound / voices HTTP.
- Wire `ng test` + agent `pytest` into Makefile + Azure + GHA.

**Out of scope**
- Deep tool turn / connector execution (already covered).
- P0 surfaces (public/auth/hub/chat/agent session).
- Full UI e2e, live ElevenLabs, real probe networks.
- P2 workstreams.

## Fixtures

Reuse from P0: `TestAuthHelper`, `PublicBotTestHelper`, `BotFactoryWebApplicationFactory`, `InMemoryBlobStorageService`.  
FakeAuth emails for permission matrix (`public@example.com` chat-only → 403 on tools/publish).  
Stub ElevenLabs / avoid live probe.

## Workstreams

### WS1 — Tools admin CRUD (L)
`ToolConnectionApiTests.cs`, `ToolDefinitionApiTests.cs`, `BotToolApiTests.cs`  
Routes: `/api/tool-connections`, `/api/tools`, `/api/bots/{botId}/tools`

### WS2 — Middleware / filters (M)
`TenantResolutionMiddlewareTests.cs`, `PublicTokenScopeMiddlewareTests.cs`, `PermissionEndpointFilterTests.cs`

### WS3 — Frontend auth (M)
`auth.guard.spec.ts`, `auth.service.spec.ts`, `auth.interceptor.spec.ts`

### WS4 — Publish + media (M)
`BotPublishApiTests.cs`, `BotLogoApiTests.cs`, `BotThinkingSoundApiTests.cs`, `VoiceApiTests.cs`

### WS5 — CI wiring (S)
- Makefile: `test-backend`, `test-frontend`, `test-agent`, `test` runs all three
- `.azure/frontend.yml`: Vitest before build
- `.azure/agent.yml`: pytest before image
- `.github/workflows/backend-tests.yml`: add FE + agent jobs (path-filtered)

## Implementation order

1. WS2 middleware (uses P0 public token)  
2. WS1 tools CRUD  
3. WS4 publish/media  
4. WS3 frontend auth (can parallel in spirit; same agent)  
5. WS5 CI last  

## Definition of done

- [x] All WS1–WS5 landed
- [x] `dotnet test apps/backend/BotFactory.Tests` green
- [x] Frontend auth specs green under `npm test -- --watch=false`
- [x] Agent pytest still green
- [x] `make test` runs backend + frontend + agent
- [x] Azure FE/agent + GHA invoke those tests
- [x] CHANGELOG `[Unreleased]` note for P1
- [x] No live ElevenLabs/probe in CI
- [x] Do not commit

## Risks

- FakeAuth may lack `tool:*` claims — use superadmin/bot:admin bypass vs chat-only deny
- Multipart uploads: copy KbDocumentApiTests patterns
- NPM registry token already used by FE pipelines — reuse
