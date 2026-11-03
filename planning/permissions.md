# Bot Factory — Permissions & Roles

## Permissions

Each permission represents a single atomic action. Roles are collections of these permissions.

### Special

| Permission       | Description                                                                          |
|------------------|--------------------------------------------------------------------------------------|
| `superadmin`     | Cross-org access to everything. Platform-level operations (GOB-ID internal users).   |
| `bot:admin`      | All permissions within the organization. Includes Conversation debug panel, settings, user management, and full access to all org data. |

### Bot

| Permission       | Description                                                                 |
|------------------|-----------------------------------------------------------------------------|
| `bot:read`       | View bot configuration in the admin portal (read-only)                      |
| `bot:update`     | Create and edit bots (name, language, voice, master prompt, VAD settings)   |
| `bot:delete`     | Delete a bot and its associated data                                        |
| `bot:publish`    | Publish or unpublish a bot (generates/removes public URL)                   |
| `bot:chat`       | Interact with a published bot via voice or chat                             |

### Knowledge Base

| Permission       | Description                                                              |
|------------------|--------------------------------------------------------------------------|
| `kb:update`      | Create, replace, and reindex knowledge base documents                    |
| `kb:delete`      | Delete KB documents                                                      |

### Sensitive Read Access

| Permission            | Description                                                          |
|-----------------------|----------------------------------------------------------------------|
| `conversation:read`   | View historical bot conversation transcriptions in the admin portal  |
| `telemetry:read`      | View telemetry dashboards, events, and metrics across all bots       |
| `audit:read`          | View audit log entries                                               |

---

## Roles

Each role is a named collection of permissions. Roles are assigned in GOB-ID and read from JWT claims.

GOB-ID app name: **VOZ**

### R1 — Administrador Maestro (Global)

`superadmin, bot:admin, bot:read, bot:update, bot:delete, bot:publish, kb:update, kb:delete, conversation:read, telemetry:read, audit:read, bot:chat`

### R2 — Administrador

`bot:admin, bot:read, bot:update, bot:delete, bot:publish, kb:update, kb:delete, conversation:read, telemetry:read, audit:read, bot:chat`

### R3 — Creador de Bots

| Permission              |
|-------------------------|
| `bot:read`              |
| `bot:update`            |
| `bot:publish`           |
| `conversation:read`     |
| `telemetry:read`        |
| `bot:chat`              |

### R4 — Editor de Conocimiento

| Permission              |
|-------------------------|
| `bot:read`              |
| `kb:update`             |
| `kb:delete`             |
| `bot:chat`              |

### R5 — Analista

| Permission              |
|-------------------------|
| `bot:read`              |
| `conversation:read`     |
| `telemetry:read`        |
| `bot:chat`              |

### R6 — Auditor

| Permission              |
|-------------------------|
| `bot:read`              |
| `conversation:read`     |
| `audit:read`            |
| `bot:chat`              |

### P1 — Usuario Público

| Permission              |
|-------------------------|
| `bot:chat`              |

### P0 — Visitor

No permissions. Can view bot landing page and login CTA only.
