# Tech Debt

## Auth: Move session storage from DB to Redis

**Current state:** Auth sessions are stored in the SQL Server database (`sessions` table). The `SessionAuthHandler` looks up the session on every request, validates the stored JWT, and handles transparent token refresh.

**Problem:** Every authenticated request hits the DB for session lookup. This is fine at low scale but adds latency compared to an in-memory store.

**Proposed solution:** Replace `DbSet<Session>` lookups with Redis. The session structure is already a simple key-value (GUID → tokens + expiry), which maps naturally to Redis.

**When to address:** When scaling to multiple backend instances or when DB query latency becomes noticeable.
