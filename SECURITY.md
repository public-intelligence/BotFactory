# Security Policy

## Supported Versions

Security updates are provided for:

| Branch / Version | Supported |
|---|---|
| Latest release on `master` | Yes |
| `development` branch | Yes |
| Older commits or unmaintained forks | No |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues, pull requests, or discussions.**

Instead, report vulnerabilities privately through one of the following channels:

1. **GitHub Private Vulnerability Reporting:** Use the "Report a vulnerability" button in the Security tab of `https://github.com/public-intelligence/BotFactory/security/advisories`.
2. **Email:** Send an encrypted or plain-text report to Melissa Muñoz at `melissa@publicintelligence.ai`.

Please include:

- A clear description of the vulnerability and its potential impact;
- Step-by-step instructions or a minimal proof of concept to reproduce the issue;
- The affected branch, commit hash, or deployment component (`backend`, `frontend`, `agent`, `kb-scraper`, or `deploy/`).

**Do not include real personal data, live citizen conversations, or active third-party credentials in your report.**

## What to Expect

- **Acknowledgment:** We will acknowledge receipt of your report within **30 business days**.
- **Assessment and remediation:** Following acknowledgment, we will provide an initial technical assessment and target remediation timeline.
- **Coordinated disclosure:** We ask that you give us reasonable time to develop, verify, and publish a fix before disclosing the vulnerability publicly. With your permission, we will credit you in the security advisory and [`CHANGELOG.md`](CHANGELOG.md) once the fix is released.

## Scope

This policy covers:

- The source code in `https://github.com/public-intelligence/BotFactory`;
- The default deployment templates and scripts under `deploy/`.

Live instances deployed and operated by independent institutions are the responsibility of those institutions. If you discover a vulnerability affecting a specific institutional deployment, please report it to us privately and we will coordinate notification with the operating institution.

## Exposed Secrets or Credentials

If you discover an exposed API key, signing key, password, or other credential in the repository, its release bundles, or its git history:

1. Report it privately using the channels above;
2. We will immediately rotate or revoke the affected credential;
3. We will remove the secret from the repository and document any required operator action in [`CHANGELOG.md`](CHANGELOG.md).

Automated CI checks (`.github/workflows/deploy-bundle.yml` via `scripts/build-deploy-zips.py --check`) scan deployment bundles to prevent accidental publication of API keys, private keys, or support tokens.

## Production Deployment Hardening (Summary)

Operators deploying Bot Factory in production must follow the full checklist in [`deploy/DEPLOYMENT.md`](deploy/DEPLOYMENT.md). At minimum:

- Set `AUTH_USE_FAKE=false` and `AUTH_ALLOW_INSECURE_FAKE=false` (Fake-ID signs users in without a password and is strictly for local development or isolated test installs);
- Configure strong, unique secrets for `AGENT_CALLBACK_SECRET`, `PUBLIC_ACCESS_SIGNING_KEY`, and `ENCRYPTION_MASTER_KEY`;
- Configure `ConnectionStrings__BotFactoryDb` via environment variables or a secret manager (never commit credentials);
- Publish only the reverse-proxy ports (HTTPS/TLS) and keep SQL Server, Azurite/blob storage, Redis, and internal backend/scraper ports private;
- Enable encryption at rest on the database and object storage volumes in accordance with your institution's security standards.
