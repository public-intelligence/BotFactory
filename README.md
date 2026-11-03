# Bot Factory

Multi-tenant bot platform with knowledge base, voice, and LLM capabilities.

## Quick Start

Start infrastructure first, then run backend and frontend:

```bash
make infra       # SQL Server, Redis, Garage (S3), LiveKit
make backend     # .NET API on https://localhost:7130
make frontend    # Angular dev server on http://localhost:4200
make agent       # LiveKit voice agent
```

> **Voice on Windows (Docker Desktop):** LiveKit advertises the container's
> internal IP in ICE candidates, which the Windows-host browser cannot reach —
> signaling connects but audio fails. Create a gitignored
> `docker-compose.local.yml` overriding the livekit service command with
> `--node-ip <your LAN IP>` and start infra with
> `docker compose -f docker-compose.dev.yml -f docker-compose.local.yml up -d`.

## All Commands

| Command            | Description                                          |
|--------------------|------------------------------------------------------|
| `make backend`     | Run the .NET backend API                             |
| `make frontend`    | Run the Angular frontend                             |
| `make agent`       | Run the LiveKit agent                                |
| `make infra`       | Start dev infrastructure (SQL Server, Redis, Garage) |
| `make api-generate`| Regenerate frontend API client from OpenAPI spec     |
| `make test`        | Run backend tests                                    |
| `make help`        | Show all available commands                          |
