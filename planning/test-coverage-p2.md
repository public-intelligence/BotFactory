# P2 Test Coverage — Implementation Plan

**Status:** Implemented  
**Depends on:** [P0](test-coverage-p0.md), [P1](test-coverage-p1.md) (CI now runs FE/agent)  
**Related:** coverage analysis canvas

## Goals / out of scope

**Goals**
- Tool execution internals (policy, credentials, gateway, kb_search handler) — unit, not full chat-turn.
- Audit / system / telemetry HTTP API tests.
- First kb-scraper test project (mocked Playwright, no Chromium in default CI).
- Targeted Vitest for bot-admin UX helpers (conversation-item, chat-history helpers, tools mapping, light store).
- Thin Playwright smoke harness + 1–2 smokes; **CI optional / skip if E2E_BASE_URL unset**.

**Out of scope**
- Payments/billing.
- Full bot-edit tab UI e2e.
- Duplicating RestToolConnectorTests, ToolArgumentValidatorTests, ToolGuardrailTests, TelemetryServiceTests, ExternalToolsChatTurnTests, KbSearchServiceTests.
- Live Chromium in default scraper CI.

## Workstreams

### A — Tool internals (M)
`ToolPolicyEvaluatorTests.cs`, `ToolCredentialProviderTests.cs`, `ToolExecutionGatewayTests.cs`, `KbSearchToolHandlerTests.cs`

### B — Audit / system / telemetry HTTP (M)
`AuditLogApiTests.cs`, `TelemetryApiTests.cs`, `SystemLogApiTests.cs`  
Stub support-dropbox HttpClient; temp CLEF file for system logs.

### C — KB scraper (M)
New `apps/kb-scraper/BotFactory.KbScraper.Tests/`  
Health, auth, render with mocked render service; optional InternalsVisibleTo / tiny IRenderService seam if needed.  
`public partial class Program { }` if required for WebApplicationFactory.

### D — Frontend bot admin Vitest (M)
`conversation-item.spec.ts`; extract+test chat-history date helpers; tools mapping helpers; light `bot-edit.store.spec.ts`; optional `filter-ui.spec.ts`.

### E — Playwright smoke (L harness / S cases)
`apps/frontend/e2e/playwright.config.ts` + `smoke/login-chat-close.spec.ts`  
npm script `test:e2e`; skip when `E2E_BASE_URL` unset; **do not** wire into default `make test` / PR gate yet. Document in plan or brief README note under e2e/.

## Implementation order

1. A1–A2 policy + credentials  
2. A3–A4 gateway + kb handler  
3. B audit + telemetry + system logs  
4. C scraper tests  
5. D frontend Vitest  
6. E Playwright last (optional CI)

## Definition of done

- [x] A–D green in default local runs (`dotnet test` backend + scraper; `npm test`; agent still green)
- [x] E exists and skips without E2E_BASE_URL
- [x] No secrets; dropbox upload stubbed
- [x] CHANGELOG `[Unreleased]` Added for P2
- [x] planning status Implemented
- [x] Do not commit

## Risks

- Gateway tests: stub IToolConnector; don't go through LiveKit turn
- SystemLog file I/O: temp file + config override
- Scraper: mock render, not live Playwright in default CI
- FE: extract pure helpers rather than heavy TestBed for full templates
