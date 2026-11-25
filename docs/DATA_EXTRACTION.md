# Data Extraction and Import Mechanisms

This document describes how institutions and authorized administrators can extract and import non-personal and operational data from **Bot Factory** using open, non-proprietary formats. It supports DPGA Indicator 6 (Mechanism for Extracting Data).

## 1. Supported Open Formats

Bot Factory uses standard, non-proprietary formats across its API and file workflows:

- **JSON** (REST API described by an OpenAPI 3.x specification)
- **Plain text (`.txt`, UTF-8)** for knowledge-base documents and raw system logs
- **CSV** (`.csv`) and **OpenXML spreadsheets (`.xlsx`)** for bulk URL-source import

## 2. Available Today (Verified in Code)

Authorized institutional users (scoped to their organization by tenant isolation and role permissions) can currently extract and import data through the following mechanisms:

- **Documented REST API (JSON / OpenAPI):**
  - OpenAPI specification: [`apps/backend/BotFactory.Api/BotFactory.Api.json`](../apps/backend/BotFactory.Api/BotFactory.Api.json).
  - Exposes structured JSON endpoints for bots, knowledge-base documents, external tool definitions, conversations, operational telemetry (`TelemetryEvent`), and configuration audit logs (`AuditLog`), all protected by role-based permissions.
- **Knowledge-base document download:**
  - `GET /api/bots/{botId}/documents/{docId}/download` downloads any stored knowledge-base document in its original plain-text format (`.txt`).
- **Bulk knowledge-source import (CSV / Excel):**
  - The admin panel (`bot-edit-kb`) supports importing lists of web-page URLs in bulk from `.csv`, `.xlsx`, or `.xls` files.
- **System log extraction:**
  - `GET /api/logs` returns structured JSON log entries; `GET /api/logs/raw?download=true` downloads the raw UTF-8 plain-text server log file (restricted to administrators).

## 3. Planned Enhancements

To make full institutional portability and backup one-click operations (without requiring custom API scripts), the following endpoints are planned:

- **Bot configuration export (`GET /api/bots/{id}/export`):** Exports a bot's complete non-personal configuration (system prompt, greeting, fallback/error messages, grounding settings, allowed origins, and tool definitions **excluding secrets/credentials**) as a portable JSON document.
- **Full knowledge-base export:** Exports all knowledge-base sources (URLs, metadata, and extracted plain text) for a bot as a JSON or CSV archive that can be imported into another Bot Factory instance or external system.
- **Bulk conversation export (date range):** Exports conversation transcripts and metadata for a specified date range as JSON or CSV for authorized administrators (`conversation:read`). Because conversation transcripts may contain personal data typed or spoken by visitors, exports will carry an explicit data-protection notice reminding operators to handle the exported file in accordance with [`PRIVACY.md`](../PRIVACY.md) and applicable law.
