# Bot Factory Privacy Policy (Software)

**Effective date:** 2026-11-25
**Last updated:** 2026-11-25
**Published by:** Center for Public Intelligence (CPI), Santo Domingo, Dominican Republic

## 1. Scope

This Privacy Policy explains how the **Bot Factory** open-source software processes personal data. It applies to the software as published by the Center for Public Intelligence (CPI) at `https://github.com/public-intelligence/BotFactory`.

Bot Factory is a multi-tenant software platform that enables public institutions and other organizations to create, test, publish, and operate voice and text assistants grounded in their own approved knowledge bases. Each deploying institution determines what information its assistants collect, for what purposes, and under what legal basis. This document describes the software's data-handling behavior and is not a substitute for the deploying institution's own public privacy notice to end users.

## 2. Data Controller and Data Processor Roles

Responsibility for personal data depends on how Bot Factory is deployed:

- **Self-hosted deployments:** The deploying institution is the sole **Data Controller** (and operates its own infrastructure). CPI does not receive, access, or process personal data from self-hosted instances.
- **Instances operated or hosted by CPI:** The deploying institution is the **Data Controller** of its assistants' data, and CPI acts as **Data Processor** solely on the institution's documented instructions, pursuant to a written Data Processing Agreement (DPA).

## 3. Personal Data Processed by the Software

Depending on how an institution configures its assistants, Bot Factory processes the following categories of data (the complete technical mapping is maintained in [`docs/DATA_INVENTORY.md`](docs/DATA_INVENTORY.md)):

1. **Conversation content (text and voice transcripts):** Messages submitted by visitors and the assistant's replies are stored in the deployment's database (`Message` table) so conversations can continue across turns and authorized institutional staff can review and improve assistant quality. Visitors may voluntarily type or speak personal information; institutions should advise visitors not to share sensitive personal data unless strictly required for the service.
2. **Voice audio (real-time processing only):** For voice-enabled assistants, live audio streams over WebRTC (via LiveKit) and is transmitted to the speech-to-text and text-to-speech provider configured by the institution (ElevenLabs by default). **Bot Factory does not record or store audio files**; only the resulting text transcripts are stored in the database. Audio handling by the external speech provider is governed by that provider's terms and data-processing commitments.
3. **Administrator and operator identifiers:** Identifiers of authorized personnel who sign in to manage assistants (issued by the configured identity provider, GOB-ID by default) are stored in server-side session records (`Session`), conversation ownership records (`Conversation.UserId`), and audit logs (`AuditLog`) for access control and accountability.
4. **Anonymous visitor session identifiers and IP addresses:**
   - Public visitors receive a short-lived, stateless signed session token (15 minutes by default) scoped solely to their active conversation.
   - The visitor's IP address is read **solely in memory** for a 1-minute fixed window to enforce per-IP rate limiting on public endpoints (`/api/public` and `/api/embed`). **The software does not persist visitor IP addresses** in the database or in telemetry events. Note that web-server or reverse-proxy access logs (such as nginx) may record IP addresses depending on the deploying institution's infrastructure configuration.
5. **Operational telemetry:** Per-turn technical telemetry (`TelemetryEvent`), such as latency, token usage, tool execution status, and rate-limit events, is stored in the deployment's database to monitor service reliability and capacity.

## 4. What the Software Does Not Do

As verified in the published source code, Bot Factory:

- **Does not sell** personal data or conversation content;
- **Does not use** conversations to train or fine-tune AI models (the software contains no training pipeline or training-data export);
- **Does not build** advertising profiles or embed third-party behavioral analytics or tracking SDKs (such as Google Analytics, Mixpanel, or advertising pixels).

## 5. Purposes and Legal Basis

Bot Factory processes data to:

- Deliver real-time text and voice answers grounded in the institution's approved sources;
- Enable authorized institutional staff to audit, evaluate, and improve assistant accuracy;
- Maintain a security and configuration audit trail; and
- Protect the service against abuse, automated scraping, and denial-of-service attacks.

Each deploying institution is responsible for establishing and documenting the appropriate **legal basis** for its deployment under applicable law (for example, performance of a task carried out in the public interest, compliance with a legal obligation, or informed consent).

**Applicable legal frameworks considered in this policy:**

- **Dominican Republic:** Law No. 172-13 on Comprehensive Protection of Personal Data (*Ley Orgánica sobre Protección de Datos de Carácter Personal*).
- **Costa Rica:** Law No. 8968 on Protection of the Person Regarding the Processing of Personal Data (*Ley de Protección de la Persona frente al Tratamiento de sus Datos Personales*).
- **International good practice:** EU General Data Protection Regulation (GDPR) principles (data minimization, purpose limitation, storage limitation, integrity and confidentiality, and accountability).

## 6. Third-Party Sub-Processors and International Data Transfers

When an institution configures external AI or speech providers, message text and/or live voice audio are transmitted to those providers to generate responses:

- **Language model and embeddings:** Google Gemini API (default provider in the current release).
- **Speech-to-text and text-to-speech (optional, voice bots only):** ElevenLabs (default voice provider in the current release).

These third-party providers may process data outside the Dominican Republic, Costa Rica, or the deploying institution's country. Before enabling external providers, each deploying institution must review the provider's data-processing terms (including zero-data-retention or non-training commitments for API traffic) and ensure compliance with applicable cross-border data-transfer rules.

Institutions that require full data residency or wish to avoid third-party transfers may disable voice features (running text-only assistants) and, once open-model support is deployed (see [`docs/PLATFORM_INDEPENDENCE.md`](docs/PLATFORM_INDEPENDENCE.md)), connect Bot Factory to self-hosted open-weight models.

## 7. Data Retention and Deletion

- **Active session timeouts:** The software automatically closes idle text conversations after 15 minutes of inactivity by default (configurable via `ConversationLifecycle__TextIdleCloseMinutes`) and idle voice conversations after twice the configured no-speech timeout (~30 seconds by default). Closing a conversation ends the active session but **does not currently delete** stored conversation records.
- **Default retention period:** For instances operated by CPI, the default retention period for conversation records, operational telemetry, and audit logs is **90 days**, unless a different period is required by applicable law or specified in the institution's Data Processing Agreement. Self-hosted institutions define their own retention periods in accordance with applicable public-records and data-protection laws.
- **Planned software capability:** Configurable per-organization retention periods and automated deletion or anonymization of expired conversations and inactive session records are planned. Until automated purging is enabled in the software, retention is enforced through scheduled operator cleanup.

## 8. Data Subject Rights

Individuals whose personal data has been processed through a Bot Factory assistant may exercise their rights under applicable law, including the rights to **access**, **rectification**, **erasure (deletion)**, **objection**, and **restriction of processing** (ARCO rights under Law 172-13 and Law 8968, and Chapter III rights under GDPR).

- **How to submit a request:** Because each deploying institution controls its own assistants and database, individuals should direct rights requests to the institution operating the assistant. For instances operated by CPI, requests may also be sent to CPI at the contact below, and CPI will promptly forward and assist the responsible institution in fulfilling the request.
- **Response timeframe and fulfillment:** Valid requests on CPI-operated instances are addressed within **10 business days**. Authorized database administrators can locate, export, anonymize, or delete specific conversation records on request. A self-service administrative deletion and anonymization workflow in the admin panel is planned.

## 9. Security Measures

Bot Factory implements defense-in-depth technical safeguards, including per-organization tenant isolation via global database query filters, AES encryption of provider API keys and tool credentials, role-based access control, short-lived anonymous visitor tokens, per-bot allowed origins, multi-layer rate limiting, secret redaction in audit logs and tool outputs, and egress guards for external tool calls.

Full details of technical protections are documented in [`docs/DATA_INVENTORY.md`](docs/DATA_INVENTORY.md). To report a security vulnerability, follow [`SECURITY.md`](SECURITY.md).

## 10. Children and Minors

Bot Factory assistants are designed for adults **aged 18 and older** seeking information about public services and are not directed at children or minors under 18. Deploying institutions whose public services involve minors must implement appropriate age-appropriate notices, parental-consent mechanisms where required by law, and content safeguards in accordance with [`CONTENT_POLICY.md`](CONTENT_POLICY.md).

## 11. Changes to This Policy

CPI may update this Privacy Policy as the Bot Factory software evolves (for example, when automated retention controls or new provider integrations are added). Material changes will be documented in [`CHANGELOG.md`](CHANGELOG.md) and reflected in the "Last updated" date above.

## 12. Contact

For privacy questions regarding the Bot Factory software or instances operated by CPI:

- **Organization:** Center for Public Intelligence (CPI)
- **Privacy Contact:** Melissa Muñoz (`melissa@publicintelligence.ai`)
- **Website:** `https://publicintelligence.ai`
