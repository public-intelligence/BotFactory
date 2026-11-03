# Playwright smoke tests (optional)

Run against a deployed or local stack by setting `E2E_BASE_URL`:

```bash
cd apps/frontend
E2E_BASE_URL=http://localhost:4200 npm run test:e2e
```

When `E2E_BASE_URL` is unset, specs skip automatically. These tests are **not** part of `make test` or PR CI gates.
