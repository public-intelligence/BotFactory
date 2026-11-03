# Stack

- ASP.NET 10 (Minimal APIs + EF Core)
- Angular 21 (standalone components + signals)
- SQL Server 2025 (relational + vector)
- Tailwind CSS 4 (via Core-UI)
- LiveKit (self-hosted WebRTC SFU + Node.js voice agent)
- Gemini (LLM via Microsoft.Extensions.AI)
- Hangfire (background jobs)
- S3-compatible blob storage (Garage in dev)
- Redis (LiveKit state)
- ASP.NET Identity (auth)

# Logical tasks

0- Project planning
1- App boilerplate (.NET + Angular, ASP.NET Identity, multi-tenancy, Docker, CI/CD) [no dependencies]
2- Roles configuration (RBAC lib, Angular services/guards, EF Core migrations) [depends on #1]
3- Chat MVP with LiveKit (voice UI, LiveKit JS SDK, Node.js audio bridge agent, chat turn endpoint, Gemini, streaming) [no dependencies]
4- OAuth + GOB-ID authentication (Google/Microsoft OAuth, GOB-ID OIDC, session handling) [depends on #2]
5- Bot management CRUD (create bots, runtime params, publish/unpublish) [depends on #2]
6- Chat boilerplate (DB persistence, conversations/messages schema, SignalR text chat) [depends on #5]
7- KB module (CRUD documents, S3 blob storage) [depends on #5]
8- Background jobs (Hangfire setup, indexing job type) [depends on #5]
9- Embeddings and chunking (text chunking, Gemini embeddings, SQL Server vector storage) [depends on #7, #8]
10- Merge chat MVP into chat system (production UI, persistence, bot runtime params) [depends on #3, #6]
11- Knowledge retrieval integration (top-k search, RAG context injection, grounded-only) [depends on #9, #10]
12- Telemetry (event hub, backend service, Angular service, persistence) [depends on #11]
13- Admin chat mode (debug panel, events, latencies, prompt visibility) [depends on #10]
14- Homepage (landing page, login CTA, mobile layout) [depends on #1]
