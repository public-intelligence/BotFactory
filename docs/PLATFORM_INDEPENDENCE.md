# Platform Independence and Open Alternatives

This document maps every external or proprietary dependency in **Bot Factory**, its current implementation in the repository, the open-source alternative, and the engineering changes required to operate Bot Factory without proprietary lock-in. It supports DPGA Indicator 4 (Platform Independence).

Bot Factory's own source code (backend API, Angular frontend, Python voice agent, and Playwright web scraper) is licensed under the **Apache License 2.0**.

## 1. Component Inventory

| Component | Open or closed today | Current state (verified in code, `cddeee1`) | Open alternative | Required changes | Status |
|---|---|---|---|---|---|
| **Chat LLM** | Closed (Google Gemini) | `GeminiChatService.cs` calls Gemini directly; `ProviderType` only includes `Gemini` and `ElevenLabs`; `ChatTurnService.cs` uses Gemini-specific types | Any OpenAI-compatible `/v1/chat/completions` endpoint (e.g., **Apertus** via Public AI Inference Utility, self-hosted **vLLM**, or **Ollama**) | Introduce `ILlmChatService` and provider-neutral message/tool/stream models; implement `OpenAiCompatibleChatService`; add base URL and model name to `AppSetting` | **Planned (Decided: D3)** — implementation not started |
| **Embeddings** | Closed (Google Gemini) | `GeminiEmbeddingService.cs` (`gemini-embedding-001`), stored as `vector(1536)` | Any OpenAI-compatible `/v1/embeddings` endpoint serving an open-weight embedding model | Implement `OpenAiCompatibleEmbeddingService`; add provider setting; handle vector dimension migration and knowledge-base reindexing when switching models | **Planned** — implementation not started |
| **Database & Vector Search** | Closed (SQL Server 2025) | EF Core SQL Server, `SqlVector<float>` / `vector(1536)`, `VECTOR_DISTANCE('cosine', ...)` | **PostgreSQL + pgvector** (open source, PostgreSQL License) | Add PostgreSQL EF Core provider and migrations, pgvector similarity query implementation, and database provider selection | **Planned (Decided: D2)** — implementation pending |
| **Background Job Queue** | Open library (Hangfire, LGPL-3.0) on SQL Server storage | `Hangfire.SqlServer` | **Hangfire with PostgreSQL storage** (`Hangfire.PostgreSql`) | Paired with PostgreSQL database support (D2) | **Pending (with D2)** |
| **Object Storage** | Open client (MIT) + open local/prod emulator (Azurite, MIT) | `IBlobStorageService` implemented by `AzureBlobStorageService`; the documented production stack (`deploy/DEPLOYMENT.md`) deploys **Azurite** (MIT), not Azure cloud | **Azurite** (already deployed in `deploy/`); local filesystem or S3-compatible storage (e.g., MinIO / Garage) as a second backend | Add a second `IBlobStorageService` implementation (filesystem or S3-compatible) so operators do not rely on a storage emulator | **Partly done** (no Azure cloud dependency today; native filesystem/S3 planned) |
| **Speech-to-Text & Text-to-Speech** | Closed (ElevenLabs) | `apps/agent/src/bot_factory_agent/agent.py` uses `elevenlabs.STT` and `elevenlabs.TTS` | **Optional feature:** text-only assistants (`EnableVoice=false`, `EnableChat=true`) work without any speech provider. For voice, open speech models (e.g., Whisper / faster-whisper for STT, Piper or Kokoro for TTS) via LiveKit plugins are under evaluation | Make `EnableChat=true` easy to run standalone; evaluate and add open STT/TTS LiveKit plugin configuration | **Optional** (text-only works without it; open voice plugins to evaluate) |
| **Real-Time Voice Transport** | Open (LiveKit, Apache-2.0) | `deploy/` LiveKit server + Redis | N/A — already open source | None | **Done** |
| **Voice Activity & Turn Detection** | Silero VAD (MIT); LiveKit multilingual turn-detector (separate LiveKit model terms) | `agent.py` uses Silero VAD + `MultilingualModel` from `livekit-plugins-turn-detector` | **Silero VAD** alone (MIT) if strict OSI-only licensing is required | Verify exact license terms of LiveKit turn-detector model and make it optional/configurable so deployments can run Silero VAD only | **To verify** |
| **Identity Provider** | Open (GOB-ID, Apache-2.0) | `IAuthProvider` interface with `GobIdAuthProvider` (and `FakeAuthProvider` for local dev) | Any standard **OpenID Connect (OIDC)** provider (e.g., Keycloak, Authentik) | Add a generic `OidcAuthProvider` implementing `IAuthProvider` | **Partly done** (abstraction exists; generic OIDC pending) |
| **Frontend UI Components** | Open source (Apache-2.0), token-gated package | `@public-intelligence/ui-core` (`0.0.16`) is open source at `public-intelligence/UICORE` (Apache-2.0), but currently installed from GitHub Packages requiring `NPM_TOKEN` | Public **npm** registry package or vendored build | Publish `@public-intelligence/ui-core` publicly (with explicit `license` field) so third parties can build the frontend without a GitHub token | **Pending** |
| **Web Scraping** | Open (Playwright, Apache-2.0) | `apps/kb-scraper/` (.NET 10 + Playwright/Chromium) | N/A — already open source | None | **Done** |

## 2. Supporting Open Models Without Native Function Calling

In Bot Factory, knowledge-base search is implemented as a tool (`KbSearchToolHandler`). Some open-weight language models do not support native tool/function calling reliably. To ensure open-model independence without sacrificing grounding accuracy:

- **Planned fallback (retrieve-before-generate):** When an assistant is configured with a model that lacks native function calling, the backend will run knowledge-base retrieval before generation and inject the retrieved chunks directly into the prompt, preserving the deterministic `GroundingGate` verification on figures, phone numbers, and links.
- **Existing reusable components:** Bot Factory already includes `WrittenToolCall` (parses text-formatted tool calls), `ToolInstructionPrompt`, and `ToolArgumentValidator` (JSON Schema validation).
- **Institutional REST tools:** External REST tool integrations will be documented as requiring a model with native function-calling support (or compatible structured-output support).

## 3. Planned Open-Stack Demonstration

To demonstrate full platform independence for DPGA review, CPI plans to publish:

1. A reproducible open-stack compose configuration (`docker-compose.open.yml`) running Bot Factory in text-only mode with an open-weight LLM (**Apertus** via the OpenAI-compatible Public AI Inference Utility, with self-hosted **vLLM** as a second phase), open embeddings, PostgreSQL + pgvector, and Azurite/S3 storage; and
2. A recorded demonstration of a live assistant operating end-to-end without proprietary AI or database services.
