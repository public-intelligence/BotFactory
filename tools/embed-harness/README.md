# Bot Factory — Embed harness

Static page that loads production chat embeds in a **cross-origin iframe** for QA.

- **Live site:** https://bot-factory-embed.netlify.app  
- **Embed URL (production):** `/api/embed/{publicId}` — PublicId only today; slug is not resolved by the API.
- **Bot list:** [`bots.json`](./bots.json) (`name`, `publicId`; optional `slug` for direct-chat links).
- **Query:** `?publicId=…` or `?id=…`
- **UI:** bot picker + “Cómo incrustar” modal.

**Allowlist origin:** `https://bot-factory-embed.netlify.app`

## Deploy

```bash
cd tools/embed-harness
NETLIFY_AUTH_TOKEN=… npx netlify-cli deploy --prod --dir .
```
