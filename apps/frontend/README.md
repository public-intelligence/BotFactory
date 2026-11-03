# Frontend

Angular 21 app with Tailwind CSS 4, zoneless change detection, and Vitest.

## Getting started

```bash
npm install
npm start
```

App runs on `http://localhost:4200`. A dev proxy forwards `/api` requests to the backend on port 5000 (see `src/proxy.conf.json`).

Make sure the backend is running (`dotnet run` from `apps/backend/src/BotFactory.Api`).

## Project structure

```
src/app/
  core/                  # Singleton services, interceptors, guards
    interceptors/        # HTTP interceptors (e.g. auth)
    services/            # App-wide services (ApiService, AuthService)
    guards/              # Route guards
  shared/                # Reusable components, pipes, directives
    components/
    pipes/
    directives/
  features/              # Feature modules (one folder per domain area)
    dashboard/
    bots/
    auth/
```

### Features convention

Each feature folder groups everything related to one domain area — components, services, models, pipes:

```
features/bots/
  bot-list.ts            # component — lists all bots
  bot-detail.ts          # component — single bot view
  bot-form.ts            # component — create/edit form
  bot.service.ts         # service — bot-specific API calls
  bot.model.ts           # interfaces/types
```

Feature code is only used within that feature. Reusable pieces shared across features go in `shared/`.

### Lazy loading

Routes use dynamic imports so each feature is loaded on demand:

```typescript
{
  path: 'bots',
  loadComponent: () => import('./features/bots/bot-list').then(m => m.BotList),
}
```

This keeps the initial bundle small — users only download code for routes they visit.

## Key conventions

- **Standalone components** — no NgModules
- **Signals** for reactive state (zoneless, no zone.js)
- **Functional interceptors/guards** — not class-based
- **2025 file naming** — `app.ts` not `app.component.ts`
- **`bf-` prefix** for component selectors

## Auth (development)

In dev mode, the backend exposes a Fake-ID login page at `GET /api/auth/dev/login` with seeded test users. The proxy (`proxy.conf.json`) forwards `/api/*` to the backend. Authentication uses server-side sessions via an httpOnly `bf_session` cookie.

## Scripts

| Command | Description |
|---------|-------------|
| `npm start` | Dev server on port 4200 with API proxy |
| `npm run build` | Production build to `dist/` |
| `npm test` | Run unit tests with Vitest |

