# Open Standards and Best Practices

This document lists the open technical standards, specifications, and software-engineering best practices followed by **Bot Factory**, along with verifiable repository evidence. It supports DPGA Indicator 8 (Adherence to Standards and Best Practices).

## 1. Adopted Standards and Practices (Verified in Repository)

| Standard or practice | Specification / Reference | Evidence in repository |
|---|---|---|
| **OpenAPI Specification** | <https://spec.openapis.org/oas/latest.html> | REST API described in `apps/backend/BotFactory.Api/BotFactory.Api.json`; Angular frontend API client is generated from it via `make api-generate` |
| **JSON Web Token (JWT, RFC 7519)** | <https://www.rfc-editor.org/rfc/rfc7519> | Used for server-side session token validation (`SessionAuthHandler.cs`) and stateless anonymous public-access tokens (`PublicTokenService.cs`) |
| **WebRTC** | <https://www.w3.org/TR/webrtc/> | Real-time browser-to-agent voice transport via LiveKit (`deploy/`, `apps/agent/`) |
| **Server-Sent Events (SSE)** | <https://html.spec.whatwg.org/multipage/server-sent-events.html> | Used for streaming language-model responses (`GeminiChatService.cs` and backend-to-voice-agent `/livekit/chat/turn` stream) |
| **WebSockets (via SignalR)** | <https://datatracker.ietf.org/doc/html/rfc6455> | Real-time bidirectional chat streaming between the Angular frontend and the .NET backend (`/hubs`) |
| **JSON Schema** | <https://json-schema.org/> | Used to define and validate external REST tool parameters (`ToolArgumentValidator`) |
| **Keep a Changelog (v1.1.0)** | <https://keepachangelog.com/en/1.1.0/> | Followed in [`CHANGELOG.md`](../CHANGELOG.md) and documented in `AGENTS.md` and [`CONTRIBUTING.md`](../CONTRIBUTING.md) |
| **Apache License 2.0 / SPDX** | <https://spdx.org/licenses/Apache-2.0.html> | Root [`LICENSE`](../LICENSE), [`NOTICE`](../NOTICE), `apps/frontend/package.json`, and `apps/agent/pyproject.toml` |
| **Contributor Covenant (v2.1)** | <https://www.contributor-covenant.org/version/2/1/code_of_conduct/> | Adopted in [`CODE_OF_CONDUCT.md`](../CODE_OF_CONDUCT.md) |
| **Developer Certificate of Origin (v1.1)** | <https://developercertificate.org/> | Adopted in [`CONTRIBUTING.md`](../CONTRIBUTING.md) |
| **OCI Containers / Docker Compose** | <https://opencontainers.org/> | Reproducible containerized builds and production stack in `Dockerfile`, `docker-compose.yml`, and `deploy/` |

## 2. Standards Under Adoption or Pending Verification

The following standards are planned or require formal verification before being claimed as fully adopted:

| Standard or practice | Specification / Reference | Current status |
|---|---|---|
| **OAuth 2.0 (RFC 6749) & OpenID Connect** | <https://www.rfc-editor.org/rfc/rfc6749> · <https://openid.net/specs/openid-connect-core-1_0.html> | Current `GobIdAuthProvider.cs` uses a JWT-based authorization-code exchange against GOB-ID's custom endpoints; a standards-compliant generic OIDC `IAuthProvider` is planned |
| **Semantic Versioning (SemVer 2.0.0)** | <https://semver.org/> | Planned: `CHANGELOG.md` currently tracks changes under `[Unreleased]` without tagged SemVer releases yet |
| **WCAG 2.2 (Accessibility)** | <https://www.w3.org/TR/WCAG22/> | Not yet audited: accessibility testing and remediation of the public chat and admin panel are pending |
| **OWASP ASVS** | <https://owasp.org/www-project-application-security-verification-standard/> | Several OWASP-aligned controls are implemented (tenant isolation, rate limiting, secret masking, egress guards, Fake-ID production lock), but a formal ASVS assessment has not yet been completed |
| **CycloneDX / SPDX SBOM** | <https://cyclonedx.org/> · <https://spdx.dev/> | Planned: automated SBOM generation in CI for `.NET`, `npm`, and `Python` dependencies (after pinning floating package versions such as `Scalar.AspNetCore`) |
| **REUSE Specification** | <https://reuse.software/> | Optional: SPDX file headers and `LICENSES/` directory under consideration |
