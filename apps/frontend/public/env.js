// Runtime configuration. Overridden at container start (see Dockerfile).
// Default: empty object -> app falls back to build-time environment.ts values.
// On the Static Web App this stays as-is (no override), so production is unaffected.
window.__env = {};
