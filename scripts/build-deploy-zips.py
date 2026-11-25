#!/usr/bin/env python3
"""Build (or verify) the deployment bundle that ships with Bot Factory.

The single source of truth is `deploy/`. This script derives from it:

  * `deploy/bot-factory-deploy.zip` and `apps/frontend/public/deploy/bot-factory-deploy.zip`
    (the same bundle, written twice; the second one is what the web app offers for download);
  * the flat copies next to the second zip in `apps/frontend/public/deploy/`
    (DEPLOYMENT.md, diagnose.sh, update.sh, docker-compose.stack.yml, docker-compose.sql.yml, env.example).

Never edit the zips or the flat copies by hand: edit `deploy/` and run

    python3 scripts/build-deploy-zips.py           # regenerate
    python3 scripts/build-deploy-zips.py --check   # verify (what CI runs); exit 1 if anything is stale

Both modes also scan every file that goes into the bundle for things that must never be committed
(API keys, private keys, a literal support key, a literal bearer token) and refuse to continue if they find any.
"""
import io
import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEPLOY = ROOT / "deploy"
PUBLIC = ROOT / "apps" / "frontend" / "public" / "deploy"

# Entry path inside the zip == path under deploy/. Order is kept stable on purpose.
ENTRIES = [
    "diagnose.sh",
    "update.sh",
    "DEPLOYMENT.md",
    "bot_factory/docker-compose.stack.yml",
    "bot_factory/docker-compose.sql.yml",
    "bot_factory/nginx/site.inc",
    "bot_factory/nginx/default.conf",
    "bot_factory/.env.example",
    "livekit/livekit.yaml",
    "livekit/nginx/site.inc",
    "livekit/nginx/default.conf",
    "livekit/.env.example",
    "livekit/docker-compose.livekit.yml",
]
ZIPS = [DEPLOY / "bot-factory-deploy.zip", PUBLIC / "bot-factory-deploy.zip"]
# flat copy in public/deploy  <-  source under deploy/
FLAT = {
    "DEPLOYMENT.md": "DEPLOYMENT.md",
    "diagnose.sh": "diagnose.sh",
    "update.sh": "update.sh",
    "docker-compose.stack.yml": "bot_factory/docker-compose.stack.yml",
    "docker-compose.sql.yml": "bot_factory/docker-compose.sql.yml",
    "env.example": "bot_factory/.env.example",
}
FIXED_DATE = (2026, 1, 1, 0, 0, 0)  # constant, so the zip does not change when nothing else did

FORBIDDEN = [
    ("Google API key", re.compile(rb"AIza[0-9A-Za-z_-]{35}")),
    ("private key block", re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----")),
    ("literal SUPPORT_KEY", re.compile(rb"SUPPORT_KEY=[\"']?[A-Za-z0-9][A-Za-z0-9_.-]{7,}")),
    ("literal bearer token", re.compile(rb"Bearer [A-Za-z0-9_-]{20,}")),
]


def scan(name: str, data: bytes) -> list[str]:
    return [f"{name}: contains a {label}" for label, rx in FORBIDDEN if rx.search(data)]


def build_zip() -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        for entry in ENTRIES:
            info = zipfile.ZipInfo(entry, FIXED_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o755 if entry.endswith(".sh") else 0o644) << 16
            z.writestr(info, (DEPLOY / entry).read_bytes())
    return buf.getvalue()


def zip_is_current(path: Path) -> bool:
    if not path.exists():
        return False
    with zipfile.ZipFile(path) as z:
        names = [n for n in z.namelist() if not n.endswith("/")]
        if sorted(names) != sorted(ENTRIES):
            return False
        return all(z.read(e) == (DEPLOY / e).read_bytes() for e in ENTRIES)


def main() -> int:
    check = "--check" in sys.argv[1:]
    problems: list[str] = []

    for entry in ENTRIES:
        if not (DEPLOY / entry).is_file():
            problems.append(f"missing source: deploy/{entry}")
    if problems:
        print("\n".join(problems))
        return 1

    for entry in ENTRIES:
        problems += scan(f"deploy/{entry}", (DEPLOY / entry).read_bytes())
    # The committed zips are only scanned when verifying; when regenerating they are about to be replaced.
    for zpath in ZIPS if check else []:
        if zpath.exists():
            with zipfile.ZipFile(zpath) as z:
                for n in z.namelist():
                    if not n.endswith("/"):
                        problems += scan(f"{zpath.relative_to(ROOT)}!{n}", z.read(n))
    if problems:
        print("Refusing to continue:\n  " + "\n  ".join(problems))
        return 1

    stale: list[str] = []
    for zpath in ZIPS:
        if not zip_is_current(zpath):
            stale.append(str(zpath.relative_to(ROOT)))
    for flat, src in FLAT.items():
        target = PUBLIC / flat
        if not target.exists() or target.read_bytes() != (DEPLOY / src).read_bytes():
            stale.append(str(target.relative_to(ROOT)))

    if check:
        if stale:
            print("Out of date with deploy/ (run: python3 scripts/build-deploy-zips.py):\n  " + "\n  ".join(stale))
            return 1
        print("Deployment bundle is up to date.")
        return 0

    data = build_zip()
    for zpath in ZIPS:
        zpath.write_bytes(data)
    for flat, src in FLAT.items():
        (PUBLIC / flat).write_bytes((DEPLOY / src).read_bytes())
    print(f"Wrote {len(ZIPS)} zips ({len(data)} bytes each) and {len(FLAT)} flat copies.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
