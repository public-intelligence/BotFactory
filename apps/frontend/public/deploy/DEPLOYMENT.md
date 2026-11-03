# Bot Factory — Deployment (agent runbook)

Three-server topology, everything in Docker except the database. Human-facing version: [`deployment-guide.html`](./deployment-guide.html).

| Server | Role | Runs |
|---|---|---|
| **1 — Windows** | Database | SQL Server (customer-managed, not Docker) |
| **2 — Linux + Docker** | Voice | LiveKit + nginx TLS (`deploy/livekit/`) |
| **3 — Linux + Docker** | App | frontend, api, agent, kb-scraper, azurite (`deploy/bot_factory/`) |

Registry host (both Docker servers): `crvozimages-h3a3g5dzbkamfpd8.azurecr.io`. Registry username/password are provided out-of-band (pull-only).

Deploy order: **Server 1 (DB ready) → Server 2 (LiveKit) → Server 3 (app)**.

---

## Server 1 — SQL Server (prep only)

- Enable **SQL Server Authentication**.
- Create a login (e.g. `botfactory`) with a strong password. Either pre-create an empty `BotFactory` database and make the login its owner, **or** grant the login `dbcreator` (the API creates + migrates the DB on first start).
- Open TCP **1433** to Server 3.
- Note for Server 3's `.env`: host/IP, port, DB name, login, password.

---

## Server 2 — LiveKit

Preconditions: Docker installed; a **public DNS name** pointing here; a **TLS
certificate + key** for that name (full chain); ports **443, 80, 7881/tcp,
50000-50100/udp** open to the internet, and **7880/tcp** reachable from Server 3.

```bash
# copy deploy/livekit/ here, then:
cd livekit
mkdir -p certs && cp /path/to/fullchain.pem /path/to/privkey.pem certs/
cp .env.example .env          # LIVEKIT_API_KEY + LIVEKIT_API_SECRET only
$EDITOR nginx/site.inc        # server_name + ssl_certificate paths
docker compose -f docker-compose.livekit.yml up -d
```
Verify:
```bash
curl -s https://$LIVEKIT_DOMAIN/            # expect: OK   (valid TLS cert)
docker logs bf-livekit-tls 2>&1 | tail -5   # no "cannot load certificate"
docker logs bf-livekit 2>&1 | tail -20      # no fatal errors; "starting LiveKit server"
```
Keep `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` — Server 3 must use the exact same values.

The image registry is only needed on Server 3; this server runs stock `livekit` and
`nginx` images from Docker Hub.

---

## Server 3 — application stack

Preconditions: Docker installed; ports **80, 443** open (plus 30000-30003 for direct debug access); can reach Server 1 on 1433 and Server 2 on 7880.

### 1. Files + registry login
```bash
mkdir -p ~/botfactory && cd ~/botfactory   # copy deploy/bot_factory/ here (compose + nginx/)
echo '<REGISTRY_PASS>' | docker login crvozimages-h3a3g5dzbkamfpd8.azurecr.io -u '<REGISTRY_USER>' --password-stdin
```

### 2. `.env`
Fill from `deploy/bot_factory/.env.example`. Strict formats: `ENCRYPTION_MASTER_KEY` = `openssl rand -base64 32`; `SQL_PASSWORD` from Server 1; `LIVEKIT_*` must match Server 2.
```bash
SERVER=<server3-public-host>
cat > .env <<EOF
ACR=crvozimages-h3a3g5dzbkamfpd8.azurecr.io
SQL_HOST=<server1-host>
SQL_PORT=1433
SQL_DB=BotFactory
SQL_USER=<sql-login>
SQL_PASSWORD=<sql-password>
SQL_ENCRYPT=true
SQL_TRUST_CERT=true
LIVEKIT_HOST=<server2-host-or-ip>
LIVEKIT_PORT=7880
LIVEKIT_PUBLIC_HOST=wss://<server2-domain>
LIVEKIT_API_KEY=<same-as-server2>
LIVEKIT_API_SECRET=<same-as-server2>
ENCRYPTION_MASTER_KEY=$(openssl rand -base64 32)
FAKE_SIGNING_KEY=$(openssl rand -base64 32)
AGENT_CALLBACK_SECRET=$(openssl rand -base64 24)
PUBLIC_ACCESS_SIGNING_KEY=$(openssl rand -base64 32)
KB_SCRAPER_API_KEY=$(openssl rand -base64 24)
ALLOWED_ORIGINS=https://<server3-public-domain>
PUBLIC_SPA_INDEX_URL=https://<server3-public-domain>/index.html
AUTH_USE_FAKE=true
EOF

mkdir -p certs && cp /path/to/fullchain.pem /path/to/privkey.pem certs/
$EDITOR nginx/site.inc        # server_name + ssl_certificate paths
```
The domain lives in two places by design: `nginx/site.inc` (`server_name`, cert
paths) and `.env` (`ALLOWED_ORIGINS`, `PUBLIC_SPA_INDEX_URL`). They must name the
same host — `diagnose.sh` cross-checks them.

nginx config is plain nginx, mounted from `./nginx` over `conf.d`: `default.conf`
is ours and gets replaced on upgrade, `site.inc` is the operator's and never is.
The whole directory is mounted, not individual files — a single-file bind mount
keeps pointing at the old inode when an editor rewrites the file.

### 3. Launch
```bash
docker compose -f docker-compose.stack.yml pull
docker compose -f docker-compose.stack.yml up -d
```
Upgrading an install that predates the single frontend container? Add `--remove-orphans` —
a retired `bf-caddy` container otherwise stays up and holds port 80, so `bf-frontend`
fails to bind.

---

## Verification (Server 3)

```bash
# containers: 5 Up; bf-kb-scraper + bf-agent (healthy)
docker compose -f docker-compose.stack.yml ps

# API connected to external SQL + ran migrations (expect {"status":"Healthy"...})
curl -s http://$SERVER:30001/health

# agent registered with LiveKit on Server 2 (expect a "registered worker" line)
docker logs bf-agent 2>&1 | grep "registered worker" | tail -1

# web app over TLS (expect 200, valid cert) and the API through the SPA proxy
# (expect JSON with redirectUri on your https origin, not http://)
curl -s -o /dev/null -w '%{http_code}\n' https://$APP_DOMAIN/
curl -s "https://$APP_DOMAIN/api/auth/login?provider=fake"

# plain-HTTP debug port still answers regardless of TLS
curl -s -o /dev/null -w '%{http_code}\n' http://$SERVER:30000/
```
Voice end-to-end is manual: open the app over HTTPS, start a call — LiveKit room opens on Server 2 and the agent joins.

## Diagnostics

`diagnose.sh` checks a server end to end — containers, ports, certificates, the SQL
and LiveKit connections, and recent errors — and prints what works and what does
not. It detects which server it is on. Run it on both.

Ships in the deploy bundle, not in the frontend image — the operator unpacks it
deliberately rather than finding an executable of ours already running on their
server.

```bash
bash diagnose.sh                     # check this server
bash diagnose.sh > report.txt        # capture for support
bash diagnose.sh --egress            # also test outbound internet (off by default)
```

Exit code 0 when everything passed, 1 when a check failed. It makes no outbound
connections unless `--egress` is given, and prints secrets only as set/empty plus
length.

## Failure → cause map

| Symptom | Cause | Fix |
|---|---|---|
| API loops on `Cannot open database` / login failed | SQL host/login/password wrong, 1433 blocked, or login can't create the DB | fix `SQL_*` in `.env`; pre-create the DB or grant `dbcreator` |
| API loops on `master key must be exactly 32 bytes` | `ENCRYPTION_MASTER_KEY` not base64-of-32-bytes | `openssl rand -base64 32`, recreate `.env`, `up -d` |
| agent never logs `registered worker` | can't reach Server 2:7880, or `LIVEKIT_API_KEY/SECRET` differ between servers | open 7880 Server3→Server2; make keys identical |
| voice connects but no audio | UDP 50000-50100 / TCP 7881 blocked on Server 2, or LiveKit advertised the wrong IP | open those ports; check `docker logs bf-livekit` for the address it picked and pin `node_ip` in `livekit.yaml` |
| `bf-livekit` restart-loops on `could not resolve external IP` | container DNS or outbound UDP is blocked, so STUN discovery fails | pin `use_external_ip: false` + `node_ip` in `livekit.yaml`; fix container DNS (below) |
| containers can't resolve any hostname — `lookup … on 127.0.0.53:53: connection refused` | the host's `/etc/resolv.conf` points at systemd-resolved's stub, which does not exist inside a container | set `{"dns": ["<your resolver>"]}` in `/etc/docker/daemon.json` and restart Docker. Image pulls still work while this is broken — `dockerd` resolves on the host — so "Docker works" is not evidence against it |
| `bf-frontend` / `bf-livekit-tls` exits at start with `TLS file not readable` | `TLS_CERT`/`TLS_KEY` set to a path that is not mounted | put the PEMs in `./certs` and use the `/etc/nginx/certs/...` path |
| browser blocks the microphone | app served over plain http | set `TLS_CERT`/`TLS_KEY` (or terminate TLS in your own proxy) |
| login redirects to `http://` on an HTTPS site | a TLS proxy in front is not sending `X-Forwarded-Proto` | add `proxy_set_header X-Forwarded-Proto $scheme;` in that proxy |
| browser CORS errors after enabling TLS | `ALLOWED_ORIGINS` still on `http://…:30000` | match it to the real browser origin, `up -d api` |
| `not authorized` on pull | registry login expired/missing | rerun `docker login` |

## Management (either Docker server)
```bash
docker compose -f <compose-file> pull <svc> && docker compose -f <compose-file> up -d <svc>   # update
docker compose -f <compose-file> logs -f                                                       # logs
docker compose -f <compose-file> down            # stop (keep data)
```

## Management (certificates)
nginx reads the certificate once at start. After replacing the files:
```bash
docker compose -f <compose-file> restart frontend   # or: nginx, on Server 2
```
Also restart `frontend` after recreating `bf-api` — nginx resolves the backend
address at start and keeps it.

## Production switches (config-only, same images)
- **HTTPS on the app (Server 3)**: set `TLS_CERT`/`TLS_KEY` to your own certificate (required for the microphone), or terminate TLS in your own proxy in front of port 30000 — that proxy must forward `X-Forwarded-Proto`.
- **Auth**: set `AUTH_USE_FAKE=false` and fill `GOBID_*`; register the app's redirect URI with GOB-ID.
- **Blob**: replace Azurite by pointing `BlobStorage__ConnectionString` at durable storage; back up.
- **Rotate** `LIVEKIT_API_SECRET` and SQL credentials from the defaults.
