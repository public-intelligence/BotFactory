# Contributing to Bot Factory

Thank you for your interest in contributing to **Bot Factory**. Bot Factory is maintained by the **Center for Public Intelligence (CPI)** and licensed under the [Apache License, Version 2.0](LICENSE).

## Before You Start

- **Discuss large changes first:** Open a GitHub issue to discuss new features, architectural changes, or breaking changes before writing code.
- **Code of Conduct:** All contributors and maintainers are expected to follow our [Code of Conduct](CODE_OF_CONDUCT.md).
- **Security vulnerabilities:** Do **not** report security issues in public GitHub issues. Follow the private reporting process in [SECURITY.md](SECURITY.md).

## Development Setup

See the [README](README.md) for full prerequisites (.NET 10 SDK, Node.js 22, Docker, and optionally Python 3.11+ with `uv` for the voice agent).

The frontend installs `@public-intelligence/ui-core` from GitHub Packages; export a GitHub token with `read:packages` as `NPM_TOKEN` (or place it in a git-ignored `.env` file at the repository root).

Start the local stack using the `Makefile`:

```bash
make infra        # Start SQL Server, Azurite, Redis, and LiveKit in Docker
make setup        # Restore backend packages and install frontend dependencies
make backend      # Run the .NET 10 API on http://localhost:5000
make frontend     # Run the Angular 21 app on http://localhost:4200
```

Optional services:

```bash
make kb-scraper   # Run the Playwright web-page scraper
make agent        # Run the Python LiveKit voice agent
```

Run `make help` to see all available targets.

## Contribution Workflow

1. **Fork and branch:** Fork the repository and create a feature or fix branch from `development`.
2. **Keep changes focused:** Write clear, cohesive commits and include automated tests covering your changes.
3. **Sign off your commits (DCO):** Include a `Signed-off-by` line in every commit message (`git commit -s`).
4. **Update the changelog:** Add a concise entry under `[Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md) (following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)) for any user-visible or operator-visible change.
5. **Regenerate the frontend API client when needed:** If you modify backend API endpoints or DTOs, run `make api-generate` and commit the generated output. **Never edit `apps/frontend/src/app/api/` by hand.**
6. **Run the test suite:**
   ```bash
   make test
   ```
   You can also run individual suites (`make test-backend`, `make test-kb-scraper`, `make test-frontend`, `make test-agent`).
7. **Verify deployment bundles (if touching `deploy/`):** Run `python3 scripts/build-deploy-zips.py --check` to verify that deployment bundles stay in sync and contain no secrets.
8. **Open a pull request:** Target the `development` branch. `master` receives reviewed, approved releases from `development`.

## Security and Data Hygiene

**Never commit** any of the following to the repository:

- Real API keys, passwords, signing keys, certificates, or `.env` files;
- Real personal data, conversation logs, or pilot/customer data;
- Unauthorized references to institutional deployments or customer names.

## Licensing and Ownership of Contributions

All contributions to Bot Factory are licensed under the **Apache License, Version 2.0** (see [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE)). By submitting a pull request, you agree that your contribution is offered under the terms of Section 5 of the Apache License 2.0.

### Developer Certificate of Origin (DCO)

Every commit must include a `Signed-off-by` trailer (`git commit -s`) certifying that you have the right to submit the work under the project's license, in accordance with the [Developer Certificate of Origin v1.1](https://developercertificate.org/):

```text
Signed-off-by: Your Name <your.email@example.com>
```

Core CPI contributors and employees also execute a written rights assignment / contributor agreement in favor of the Center for Public Intelligence (CPI).

## Pull Request Review

Every pull request is reviewed by a project maintainer before merging into `development`.

- **Maintainers:** Rainier Mallol and José Cid
- **Expected response time:** We aim to provide an initial review or triage response within **30 days**.
- **General inquiries:** Melissa Muñoz (`melissa@publicintelligence.ai`)
