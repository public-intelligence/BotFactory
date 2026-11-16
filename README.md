# Bot Factory

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

Multi-tenant platform to create, test, publish and operate **voice and chat assistants** grounded in each organization's approved knowledge.

- [Overview](#overview)
- [Architecture](#architecture)
- [Repository structure](#repository-structure)
- [Tech stack](#tech-stack)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Testing](#testing)
- [Deployment](#deployment)
- [Contributing](#contributing)
- [Third-party software](#third-party-software)
- [License](#license)
- [Vision and acknowledgments](#vision-and-acknowledgments)

## Overview

Bot Factory lets many organizations share one platform while each keeps its own bots, knowledge, conversations, tools and provider keys, isolated from the others.

- **Multi-tenant by design.** The organization comes from the signed-in user's token, issued by the GOB-ID identity provider. Every query is filtered by organization.
- **Knowledge base.**
  - Sources: `.txt` files (up to 5 MB) and web pages, which the kb-scraper renders.
  - Indexing: the content is split into chunks, embedded with Gemini (`gemini-embedding-001`, 1536 dimensions) and searched with SQL Server vector search.
- **Chat.**
  - Answers stream over SignalR from a Gemini model (`gemini-2.5-flash` by default).
  - The model can call tools: knowledge search and configurable REST tools.
  - An optional required-grounding mode makes the bot consult a source before answering, and checks figures, phone numbers and links against the sources.
- **Voice.** Calls run on LiveKit. A Python agent handles voice activity detection, end-of-turn detection and ElevenLabs speech-to-text and text-to-speech, and reuses the same chat engine as text.
- **Public access.** Published bots get a public chat page and an embeddable widget, with short-lived anonymous tokens and per-bot allowed origins.
- **Admin panel.** It manages bots, knowledge, tools and per-organization settings (provider keys, error messages, branding), and shows a chat history with per-turn telemetry and token usage.

## Architecture

```
 Browser: admin panel · public chat · embedded widget
        │  HTTPS: REST (/api) and SignalR (/hubs)
        ▼
 Frontend (Angular) ───▶ Backend API (.NET 10) ───▶ SQL Server 2025 (data, vectors, background jobs)
                           │    │    │    └──────▶ Blob storage (Azure Blob / Azurite): files, logos, sounds
                           │    │    └───────────▶ Gemini (chat, embeddings)
                           │    └────────────────▶ kb-scraper (renders web pages for the knowledge base)
                           └─────────────────────▶ LiveKit (voice rooms)
                                                      │
                            Voice agent (Python) ◀────┘ ───▶ ElevenLabs (speech-to-text, text-to-speech)
                            calls back the API at /livekit/chat/turn
```

## Repository structure

```
apps/
  backend/      .NET 10 API: BotFactory.Api, BotFactory.Core, BotFactory.Infrastructure, BotFactory.Tests
  frontend/     Angular 21 app: admin panel, public chat and embeddable widget
  agent/        Python LiveKit voice agent
  kb-scraper/   .NET 10 service that renders web pages with Playwright
deploy/         Docker Compose stack, nginx configuration and deployment guide
docs/           Technical notes (public access, telemetry events)
planning/       Design and planning documents
tools/          Development helpers (embed test harness)
examples/       Integration examples
```

Each app has its own README with more detail: [backend](apps/backend/README.md), [frontend](apps/frontend/README.md), [agent](apps/agent/README.md).

## Tech stack

| Component | Stack |
|---|---|
| Backend | .NET 10 Minimal API, EF Core with SQL Server 2025 (vector search), SignalR, Hangfire (background jobs), JWT and session cookies |
| Frontend | Angular 21 (zoneless, signals), Tailwind CSS 4, `@public-intelligence/ui-core`, client generated from the OpenAPI spec, Vitest and Playwright |
| Voice agent | Python 3.11+, LiveKit Agents 1.5, Silero VAD, LiveKit turn detector, ElevenLabs, httpx, uv |
| KB scraper | .NET 10, Playwright (Chromium) |
| Infrastructure | SQL Server 2025, Azure Blob Storage (Azurite locally), LiveKit server with Redis, Docker Compose, nginx |
| AI providers | Google Gemini (chat and embeddings), ElevenLabs (speech) |

## Getting started

### Prerequisites

- .NET 10 SDK
- Node.js 22 and npm
- Python 3.11+ and [uv](https://docs.astral.sh/uv/) (only for the voice agent)
- Docker

The frontend installs `@public-intelligence/ui-core` from GitHub Packages. Export a GitHub token with `read:packages` as `NPM_TOKEN`, or put it in a `.env` file at the repo root, which `make setup` reads.

### Run locally

```bash
make infra        # SQL Server, Azurite, Redis and LiveKit in Docker
make setup        # restore the backend and install the frontend dependencies
make backend      # API on http://localhost:5000
make frontend     # app on http://localhost:4200 (proxies /api and /hubs to the API)
make kb-scraper   # optional: web pages as knowledge sources
make agent        # optional: voice (needs LiveKit and an ElevenLabs key)
```

Run `make help` to list every command.

In development, sign-in uses a fake identity provider, so no GOB-ID instance is needed. **Never enable it in production.** Provider keys (Gemini, ElevenLabs) are set per organization in the admin panel, under Settings.

> **Voice on Windows (Docker Desktop):** LiveKit advertises the container's internal IP in ICE candidates, which the Windows host browser cannot reach: signaling connects but audio fails. Create a git-ignored `docker-compose.local.yml` that overrides the `livekit` service command with `--node-ip <your LAN IP>`, and start the infrastructure with `docker compose -f docker-compose.dev.yml -f docker-compose.local.yml up -d`.

## Configuration

- **Backend:** `apps/backend/BotFactory.Api/appsettings.json`, overridden by environment variables (`Section__Key`). The main groups are:
  - the SQL Server connection;
  - authentication (GOB-ID or the fake provider);
  - the signing key for public access;
  - the shared secret for agent callbacks;
  - LiveKit;
  - blob storage;
  - the kb-scraper;
  - the Gemini model.
- **Docker deployment:** every variable is listed in [`deploy/bot_factory/.env.example`](deploy/bot_factory/.env.example).
- **Voice agent:** [`apps/agent/.env.example`](apps/agent/.env.example).

Provider keys are stored encrypted with `ENCRYPTION_MASTER_KEY`. Never commit secrets: use environment variables or a git-ignored `.env`.

## Testing

```bash
make test               # backend, kb-scraper, frontend and agent
make test-backend       # xUnit integration tests (Testcontainers: needs Docker)
make test-kb-scraper    # xUnit, rendering mocked
make test-frontend      # Vitest
make test-agent         # pytest
```

## Deployment

The Docker Compose stack, its nginx configuration and the step-by-step guide are in [`deploy/`](deploy/DEPLOYMENT.md). For production:

- set `AUTH_USE_FAKE=false`;
- set `AGENT_CALLBACK_SECRET`, `PUBLIC_ACCESS_SIGNING_KEY` and `ENCRYPTION_MASTER_KEY`;
- publish only the reverse proxy ports.

## Contributing

- Work in a branch from `development` and open a pull request against it. `master` receives approved versions.
- Add an entry under `[Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md) for user- or operator-visible changes.
- After changing API models or endpoints, regenerate the frontend client with `make api-generate`. Never edit `apps/frontend/src/app/api/` by hand.

## Third-party software

Bot Factory depends on third-party packages that keep their own licenses. They are declared in the `.csproj` files under `apps/backend` and `apps/kb-scraper`, in `apps/frontend/package.json` and in `apps/agent/pyproject.toml`. Most are MIT, Apache 2.0 or BSD. Two need attention when you deploy:

- **Hangfire** (background jobs) is licensed under the LGPL-3.0. Using it as a library does not affect the license of this project; modifying Hangfire itself does carry LGPL obligations.
- **The LiveKit turn-detector model** used by the voice agent is distributed by LiveKit under its own terms. Review them before deploying.

## License

Copyright 2026 Rainier Christopher Domingo Mallol Cotes, Jean Antonio Garcia Periche and Melissa Michelle Muñoz Suro.

Licensed under the [Apache License, Version 2.0](LICENSE). See [`NOTICE`](NOTICE).

## Vision and acknowledgments

> *“AI systems are only as capable as the tools, services, definitions, and evidence structures they can access and reliably act upon.”*
>
> — ***Rainier Mallol***, *The Operative Republic*

### Vision

Bot Factory follows the software architecture and best practices of the vision paper [*The Operative Republic: Building an Operating System for Dominican Government Services*](https://publicintelligence.ai/govos), by Rainier Mallol.

The paper argues that the next phase of public-sector modernization will be defined less by portals and more by **Digital Public Infrastructure**: shared, interoperable capabilities that many institutions can reuse instead of rebuilding. Its architecture, **Federated Bounded-Context Microservices with Centralized Orchestration (FeBMO)**, lets each institution retain ownership of its systems and data, while shared identity, workflow governance, audit trails and service standards bring them together as one coherent service environment. Each system is designed to create value on its own and to become more powerful when connected to the others.

Bot Factory brings to life **VOZ**, the voice and AI service layer of that architecture. Instead of every institution building or buying its own chatbot, it offers a shared factory to create, test, publish and operate voice and chat assistants grounded in each institution's approved knowledge, with governance, traceability and isolation between institutions. Following the paper's view of meaningful AI, assistants retrieve from approved sources before answering, act through configured tools and integrations, and are designed to grow into a copilot for government services, always under controlled permissions.

**[Read the vision paper →](https://publicintelligence.ai/govos)**

### Acknowledgments

Bot Factory was designed and built with the participation of:

| Name | Role |
|---|---|
| Rainier Mallol | Architect and Engineer |
| José Ant. Cid Troncoso | Lead Engineer |
| Willy Ovalle | Engineer |
| Winner Crespo | Engineer |

These acknowledgments only recognize participation in the design and development of Bot Factory. They do not grant any copyright, ownership, license or other legal right in the software or its name. All rights belong exclusively to the copyright holders listed under [License](#license).

*Built to help move government services from fragmented coordination to structured execution.*
