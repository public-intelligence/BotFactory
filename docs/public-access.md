# Public (anonymous) bot access — operations guide

How anonymous access, embedding, and their guardrails work in production.
Companion to `planning/anonymous-access-embedding-plan.md` (the design doc).

## Configuration

Backend (`PublicAccess` section / `PublicAccess__*` env vars):

| Setting | Default | Purpose |
| --- | --- | --- |
| `SigningKey` | *(generated per process)* | HS256 key for anonymous-session JWTs. **Required in production** — without it, tokens die on restart/scale-out. |
| `TokenTtlMinutes` | 15 | Anonymous session token lifetime; re-minted on resume. |
| `LiveKitTokenTtlMinutes` | 30 | LiveKit join-token TTL for anonymous voice. |
| `MaxActiveAnonymousConversationsPerBot` | 20 | Cap on simultaneously-active anonymous conversations per bot. |
| `RequestsPerMinutePerIp` | 30 | Fixed-window per-IP limit on `/api/public/**` and `/api/embed/*`. |
| `BotRequestsPerMinute` | 60 | Default per-bot cap on anonymous session start/resume per minute (all IPs). Overridable per bot in the Acceso section (`Bot.PublicRequestsPerMinute`). |
| `OrgRequestsPerMinute` | 300 | Per-organization cap across all its public bots. |
| `SpaIndexUrl` | *(empty)* | Public URL of the SPA `index.html` for embeds (prod: `https://bf.example.com/index.html`). On Docker stacks the API tries `bf-frontend:443` first (TLS Host/SNI = this URL's host, or the embed request host when this value is empty/http/`bf-frontend`) so embeds work when the public hostname does not hairpin; then this URL; then `wwwroot/index.html`. |

Agent callbacks: `AgentCallback__SharedSecret` (API) must match `BACKEND_SHARED_SECRET`
(agent). When unset the `/livekit/*` endpoints are open — dev only; the API logs a
startup warning.

The per-bot/per-org limiters are in-memory per API instance; revisit (e.g. Redis)
if the API is ever scaled out.

## Observability

Every conversation records at creation:

- `AccessMode` — `authenticated` | `public`.
- `Surface` — `direct` | `iframe`.
- `SourceUrl` — client-reported page URL, **query strings and fragments stripped** server-side.
- `ParentOrigin` — embedding site's origin (from the iframe's initial referrer), validated to origin shape.
- `UserId` — `anon:{opaque}` for anonymous sessions (this is the anonymous session identifier); GOB-ID `sub` otherwise.

`SourceUrl`/`ParentOrigin` are client-reported: fine for dashboards, never used for
authorization (framing is enforced by `frame-ancestors`, access by the public token).

Failure events (persisted `TelemetryEvent`s, `Source=Backend`):

- `public_session_rejected` — `Data.reason`: `anonymous_session_cap` | `voice_unavailable`.
- `rate_limit_exceeded` — `Data.scope`: `ip` | `bot` | `org`. Client IPs are never stored.

Aggregates: `GET /api/telemetry/public-usage?from=&to=` (requires `telemetry:read`;
defaults to the last 30 days) → public vs authenticated volume, direct vs embedded,
top embedding domains, rejection/rate-limit counts, daily series. Rendered on the
admin dashboard ("Uso público") for users with `telemetry:read`/`bot:admin`/`superadmin`.

## Retention & consent

- **Retention:** anonymous transcripts follow the same retention as authenticated
  conversations (no separate deletion schedule), stored under the bot's
  organization with the `anon:` user id. Approved product decision (2026-07-18).
- **No identity linkage:** anonymous history is never associated with an
  authenticated identity, including after the same visitor logs in.
- **Consent notice:** the public/embedded chat shows, before the session starts:
  *"Esta conversación puede quedar registrada para mejorar el servicio. No
  compartas datos personales sensibles como cédula, contraseñas o información
  financiera."* — in `apps/frontend/src/app/features/chat/chat.ts` (pre-connect
  stage). **Wording pending formal legal review**; update it there when legal
  settles the final text.

## Revocation

Flipping a bot back to *Autenticado* (or unpublishing it) immediately 404s new
anonymous sessions, resumes, and the embed route. Active sessions run out with
their short-lived tokens ("block-new"). Active LiveKit room termination and
`PublicId` regeneration are planned Phase 3 follow-ups.
