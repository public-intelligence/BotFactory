# Bot Factory Backend

.NET 9 Minimal API backend for the Bot Factory platform.

## Project Structure

```
src/
  BotFactory.Api/           → Entry point, endpoints, middleware, auth handler
  BotFactory.Core/          → Entities, DTOs, interfaces, domain logic
  BotFactory.Infrastructure/ → EF Core, auth providers, external integrations
tests/
  BotFactory.Tests/         → Integration tests (Testcontainers + SQL Server)
```

## URL Structure

Endpoints are grouped by auth method:

- **`/api/*`** — External API (Angular frontend). Auth: `bf_session` cookie (session-based).
- **`/livekit/*`** — Internal API (LiveKit server). Auth: API key header. _(planned)_
- **`/health`** — No auth, stays at root.

## Endpoints

### Auth (`/api/auth`)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/auth/login?provider=gobid\|fake` | No | Returns login URL for the provider (default: gobid) |
| POST | `/api/auth/token` | No | Exchange authorization code for session |
| GET | `/api/auth/me` | Yes | Current user profile |
| POST | `/api/auth/logout` | No | Clear session |
| GET | `/api/auth/dev/login` | No | Fake-ID user picker page (dev only) |
| POST | `/api/auth/dev/token` | No | Create session directly from email (dev only) |

### Bots (`/api/bots`)

| Method | Path | Auth | Permission | Description |
|--------|------|------|------------|-------------|
| GET | `/api/bots` | Yes | — | List all bots for current org |
| GET | `/api/bots/{id}` | Yes | — | Get bot by ID |
| POST | `/api/bots` | Yes | `bot:update` | Create bot |
| PUT | `/api/bots/{id}` | Yes | `bot:update` | Update bot |
| DELETE | `/api/bots/{id}` | Yes | `bot:delete` | Delete bot |

### Conversations (`/api/bots/{botId}/conversations`)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/bots/{botId}/conversations/{id}/close` | Yes | Close a conversation (`user_ended` or `user_aborted`) |

### LiveKit internal (`/livekit`)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/livekit/chat/turn` | Agent chat turn (SSE) |
| POST | `/livekit/telemetry/events` | Agent telemetry; `Close` events close the conversation (`timeout` or `user_aborted`) |

Hangfire runs `close-stale-conversations` hourly: active conversations with no messages (non-tool) newer than **2 × `bot.NoSpeechTimeoutMs`** are closed with `timeout`.

### Other

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Health check |

## Authentication

Uses server-side sessions with an opaque `bf_session` httpOnly cookie. Tokens from GOB-ID (or Fake-ID in dev) are stored in the database `Sessions` table — never exposed to the frontend.

`SessionAuthHandler` validates the stored JWT on each request and transparently refreshes expired tokens.

See [planning/auth-flow.md](../../planning/auth-flow.md) for the full auth flow documentation.

## Running Locally

```bash
# Start SQL Server
docker compose up -d

# Run the API (port 5000)
dotnet run --project src/BotFactory.Api
```

The API auto-migrates the database and seeds sample data in development mode.

## Environment Variables

Configured via `.env` file (loaded automatically via DotNetEnv):

| Variable | Description |
|----------|-------------|
| `ConnectionStrings__BotFactoryDb` | SQL Server connection string |
| `Auth__GobId__ApiUrl` | GOB-ID API base URL (also used as JWT issuer) |
| `Auth__GobId__ApplicationId` | App ID in GOB-ID |
| `Auth__GobId__AppSecret` | HS256 signing secret |
| `Auth__GobId__Audience` | JWT audience (`gobid-users`) |
| `Auth__GobId__LoginBaseUrl` | GOB-ID login page URL |
| `Auth__UseFakeProvider` | Enable Fake-ID provider (`true`/`false`) |

## Testing

```bash
# Requires Docker (Testcontainers spins up SQL Server)
dotnet test
```

Tests authenticate via `POST /api/auth/dev/token` which creates real sessions, testing the full auth pipeline.
