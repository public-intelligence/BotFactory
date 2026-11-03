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

## Attributions and Authorship

> *“AI systems are only as capable as the tools, services, definitions, and evidence structures they can access and reliably act upon.”*
>
> — ***Rainier Mallol***, *The Operative Republic*

### Architecture

Software Architecture Design by ***Rainier Mallol***, alongside the best practices outlined in his vision paper [*The Operative Republic: Building an Operating System for Dominican Government Services*](https://publicintelligence.ai/govos).

The paper argues that the next phase of public-sector modernization will be defined less by portals and more by **Digital Public Infrastructure**: shared, interoperable capabilities that many institutions can reuse instead of rebuilding. Its architecture, **Federated Bounded-Context Microservices with Centralized Orchestration (FeBMO)**, lets each institution retain ownership of its systems and data, while shared identity, workflow governance, audit trails and service standards bring them together as one coherent service environment. Each system is designed to create value on its own and to become more powerful when connected to the others.

Bot Factory brings to life **VOZ**, the voice and AI service layer of that architecture. Instead of every institution building or buying its own chatbot, it offers a shared factory to create, test, publish and operate voice and chat assistants grounded in each institution's approved knowledge, with governance, traceability and isolation between institutions. Following the paper's view of meaningful AI, assistants retrieve from approved sources before answering, act through configured tools and integrations, and are designed to grow into a copilot for government services, always under controlled permissions.

**[Read the vision paper →](https://publicintelligence.ai/govos)**

### Engineering

**Engineers**

- José Cid (cidtroncoso)
- Willy Ovalle (wovalle)
- Winner Crespo (wistcc)

*Built to help move government services from fragmented coordination to structured execution.*
