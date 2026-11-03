#!/usr/bin/env bash
# Bot Factory — deployment diagnostics (v2.0)
#
# One script, two directions. Run it ON a server and it checks what that server
# is responsible for; give it a public hostname and it checks the same
# deployment from the outside, as a browser sees it. It picks the mode for you.
#
#   bash diagnose.sh                                  # check this server
#   bash diagnose.sh --update                         # pull the latest images,
#                                                     #   restart what changed,
#                                                     #   THEN run every check
#   bash diagnose.sh --update --dry-run               # only report what would change
#   bash diagnose.sh --no-egress                      # skip the outbound internet
#   bash diagnose.sh --external                       # ALSO ship report + container
#                                                     #   logs to Bot Factory support
#   bash diagnose.sh --app app.example.com \
#                    --livekit voz.example.com        # check from a laptop, outside-in
#   bash diagnose.sh > report.txt                     # capture for support
#
#   curl -fsSL https://<your-host>/deploy/diagnose.sh | bash
#   curl -fsSL https://<your-host>/deploy/diagnose.sh | bash -s -- --app app.example.com
#
# With no --app/--livekit it runs the server checks: containers, .env, TLS on
# disk, database rights, LiveKit, log extracts. With them it runs the outside
# checks: DNS, which ports the perimeter really lets through, the certificate as
# negotiated live, clock skew, and whether the two servers agree with each other.
# On a server, passing them runs both.
#
# The two directions answer different questions and neither substitutes for the
# other. From inside, a port check can only say "a socket is bound here" — a
# firewall dropping 7881 still passes, and the operator reads [ OK ] while no
# browser on earth can reach it. From outside, nothing can see .env, container
# health or migrations. Run both, ideally from a machine that is not on the
# server's own network.
#
# Uses only what a stock Ubuntu server (or a Mac) has: bash, coreutils, curl,
# openssl. No jq, no python, nothing to install. Tested on 20.04 through 25.04.
#
# Makes no changes without --update, and sends no credentials. In server mode its only outbound
# connections are the reachability tests against the AI providers and the image
# registry (a bare TLS connect each, no data) — --no-egress skips them. Prints
# no passwords, keys or certificates: secret values are reported only as
# set/empty and by length.
#
# --external additionally sends this report and every bf-* container's recent
# logs to Bot Factory support when the run finishes, and says so up front.
# Container logs can include user conversations, so this never happens
# implicitly — only when the person running the script asks for it.
#
# Exit code: 0 if nothing failed, 1 if any check failed.

# This script uses bash arrays. `sh diagnose.sh` on Debian/Ubuntu runs it under
# dash and dies with `Syntax error: "(" unexpected`, so re-exec under bash --
# but only when $0 is a real file. Piped in (`curl ... | sh`) there is nothing to
# re-exec: $0 is the shell itself and `exec bash sh` would run the wrong thing.
if [ -z "${BASH_VERSION:-}" ]; then
	if [ -f "$0" ]; then
		exec bash "$0" "$@"
	fi
	echo "this script needs bash: pipe it to 'bash', not 'sh'" >&2
	exit 2
fi

# Deliberately no `pipefail`: with it, `docker logs | grep -q` fails whenever grep
# exits on the first match and docker logs takes SIGPIPE, which reported healthy
# services as broken.
set -u
export LC_ALL=C

# Egress is tested by default: the KB jobs failing on a blocked outbound 443
# went unnoticed for days precisely because this was opt-in and nobody opted in.
# --egress is still accepted for old runbooks; --no-egress opts out.
EGRESS=1
EXTERNAL=0
UPDATE=0
DRY=0
APP_HOST=""
LIVEKIT_HOST=""
while [ $# -gt 0 ]; do
	case "$1" in
		--egress)  EGRESS=1; shift ;;
		--no-egress) EGRESS=0; shift ;;
		--external) EXTERNAL=1; shift ;;
		--update)  UPDATE=1; shift ;;
		--dry-run) DRY=1; shift ;;
		--app)     APP_HOST=${2:-};     [ -n "$APP_HOST" ]     || { echo "--app needs a hostname" >&2; exit 2; };     shift 2 ;;
		--livekit) LIVEKIT_HOST=${2:-}; [ -n "$LIVEKIT_HOST" ] || { echo "--livekit needs a hostname" >&2; exit 2; }; shift 2 ;;
		# Piped in, $0 is not readable and there is no header to print.
		-h|--help)
			[ -f "$0" ] && sed -n '2,37p' "$0" \
				|| echo "usage: diagnose.sh [--update [--dry-run]] [--no-egress] [--external] [--app <host>] [--livekit <host>]"
			exit 0 ;;
		*) echo "unknown option: $1 (try --help)" >&2; exit 2 ;;
	esac
done

# Asking about a public hostname is the outside-in run; asking about nothing in
# particular means "check the machine I am on".
OUTSIDE_MODE=0
if [ -n "$APP_HOST" ] || [ -n "$LIVEKIT_HOST" ]; then OUTSIDE_MODE=1; fi
SERVER_MODE=$((1 - OUTSIDE_MODE))

PASS=0; FAIL=0; WARN=0; SKIP=0
FAILURES=()
OKS=()
WARNINGS=()

ok()   { printf '  [ OK ] %s\n' "$*"; PASS=$((PASS+1)); OKS+=("$*"); }
bad()  { printf '  [FAIL] %s\n' "$*"; FAIL=$((FAIL+1)); FAILURES+=("$*"); }
warn() { printf '  [WARN] %s\n' "$*"; WARN=$((WARN+1)); WARNINGS+=("$*"); }
skip() { printf '  [SKIP] %s\n' "$*"; SKIP=$((SKIP+1)); }
info() { printf '         %s\n' "$*"; }
head1() { printf '\n=== %s ===\n' "$*"; }

# Docker may need sudo. Resolve once so every later call is consistent.
DOCKER=""
if command -v docker >/dev/null 2>&1; then
	if docker info >/dev/null 2>&1; then
		DOCKER="docker"
	elif sudo -n docker info >/dev/null 2>&1; then
		DOCKER="sudo docker"
	elif sudo docker info >/dev/null 2>&1; then
		DOCKER="sudo docker"
	fi
fi
d() { $DOCKER "$@"; }

# True when the container exists (any state).
has_container() {
	[ -n "$DOCKER" ] || return 1
	d ps -a --format '{{.Names}}' 2>/dev/null | grep -qx "$1"
}

# An outside-in run started on a server that also hosts the stack still gets to
# check the parts the server cannot see about itself, so pick up both.
if [ "$OUTSIDE_MODE" = "1" ] && { has_container bf-api || has_container bf-frontend || has_container bf-livekit; }; then
	SERVER_MODE=1
fi

# -------------------------------------------------------------- log shipping ---
# --external ships this report plus every container's recent logs to Bot
# Factory support, so a problem can be diagnosed without walking the operator
# through collecting files. It is OFF by default and stays off: container logs
# can contain user conversations and stack traces, so sending them anywhere is
# an explicit decision, made per run, by the person running the script.
#
# The endpoint is a write-only drop box: this key can upload a file and nothing
# else — it cannot list, read back or delete what was sent. The URL is the
# ministry's own deployment, which relays to the drop box — so a customer
# network only ever needs outbound 443 to that one host.
#
# The key is not kept in the repository: export SUPPORT_KEY before running
# with --external.
SUPPORT_URL="https://bf.example.com/v0/api/logs"
SUPPORT_KEY="${SUPPORT_KEY:-}"
SUPPORT_CLIENT="example"
REPORT_FILE=""

# The hostname goes into a URL query parameter; anything outside [A-Za-z0-9._-]
# (a space, say) makes curl reject the whole URL and the upload silently fail.
safe_hostname() { hostname 2>/dev/null | tr -c 'A-Za-z0-9._-' '-' | sed 's/-*$//'; }

ship_support_bundle() {
	local rc=$? bundle c
	# The tee feeding REPORT_FILE runs as a separate process; give it a beat to
	# flush the tail of the report before bundling it.
	sleep 1
	bundle=$(mktemp)
	{
		echo "=== diagnose report ($(hostname 2>/dev/null || echo unknown), $(date -u '+%Y-%m-%d %H:%M:%S') UTC, exit $rc) ==="
		cat "$REPORT_FILE" 2>/dev/null
		if [ -n "$DOCKER" ]; then
			echo; echo "=== docker ps -a ==="
			d ps -a 2>&1
			for c in $(d ps -a --format '{{.Names}}' 2>/dev/null | grep '^bf-'); do
				echo; echo "=== docker logs: $c (last 1500 lines) ==="
				d logs --tail 1500 "$c" 2>&1 | cut -c1-2000
			done
		fi
	} | head -c 8000000 > "$bundle"

	printf '\n=== Shipping logs to support ===\n'
	if [ -z "$SUPPORT_KEY" ]; then
		printf '  [FAIL] SUPPORT_KEY is not set: export it and run again with --external.\n'
		printf '         the report above is still complete — copy it manually instead.\n'
		rm -f "$bundle" "$REPORT_FILE"
		exit "$rc"
	fi
	local resp
	if resp=$(curl -sf -X POST --max-time 120 \
			-H "Authorization: Bearer $SUPPORT_KEY" \
			--data-binary @"$bundle" \
			"$SUPPORT_URL?client=$SUPPORT_CLIENT&filename=diagnose-$(safe_hostname || echo unknown)-$(date -u +%Y%m%d-%H%M%S).log" 2>&1); then
		# The id is the whole point of shipping: without it support has a bucket
		# full of bundles and no way to know which one is this run. Print it last
		# and unmistakably, so it survives being screenshotted or half-copied.
		printf '  [ OK ] logs shipped (%s bytes)\n\n' "$(wc -c < "$bundle" | tr -d ' ')"
		printf '  Locate this log with the id [%s]\n' "$(printf '%s' "$resp" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p')"
		printf '  Send that id to Bot Factory support.\n'
	else
		printf '  [FAIL] could not ship the logs (no outbound internet from this server?)\n'
		printf '         the report above is still complete — copy it manually instead.\n'
	fi
	rm -f "$bundle" "$REPORT_FILE"
	exit "$rc"
}

if [ "$EXTERNAL" = "1" ]; then
	if ! command -v curl >/dev/null 2>&1; then
		echo "--external needs curl, which is not installed; continuing without shipping" >&2
		EXTERNAL=0
	else
		REPORT_FILE=$(mktemp)
		exec > >(tee "$REPORT_FILE") 2>&1
		trap ship_support_bundle EXIT
		echo "external mode: when this run finishes, the report and container logs"
		echo "will be sent to Bot Factory support."
	fi
fi

# ------------------------------------------------------------ shared helpers ---
ENV_FILE=""

# Read a value from .env without sourcing it (no surprises from odd characters).
#
# Parse it the way Docker Compose does, or the checks disagree with what the
# containers actually got: an inline `# comment` is stripped when the # follows
# whitespace, and one layer of surrounding quotes comes off. Taking the whole
# rest of the line instead reported healthy deployments as broken — a base64 key
# "decoded to 0 bytes" because the comment was fed to base64, and a reachable
# host was dialled as "10.0.0.1   # LiveKit host:7880".
envval() {
	local raw
	[ -n "$ENV_FILE" ] && [ -f "$ENV_FILE" ] || return 0
	raw=$(sed -n "s/^[[:space:]]*$1=//p" "$ENV_FILE" | head -1)
	case "$raw" in
		# Inside quotes a # is data, so only the closing quote ends the value.
		\"*) raw=${raw#\"}; raw=${raw%%\"*} ;;
		\'*) raw=${raw#\'}; raw=${raw%%\'*} ;;
		*)   raw=$(printf '%s' "$raw" | sed 's/[[:space:]]\{1,\}#.*$//') ;;
	esac
	printf '%s' "$raw" | sed -e 's/[[:space:]]*$//' -e 's/^[[:space:]]*//'
}

# A variable as the running container sees it. envval only works when the script
# is run from the deployment directory, next to the .env; the container carries
# the same values wherever the script is run from, so prefer this when the value
# is needed to talk to a service rather than to report on the file.
container_env() {
	has_container "$1" || return 0
	timeout 10 $DOCKER exec "$1" printenv "$2" 2>/dev/null | tr -d '\r\n'
}

# Registry host and one repository under it, read from what is actually running
# rather than from .env — the container carries the image reference it was
# created with, wherever the script is run from.
acr_host() { local i; i=$(d inspect -f '{{.Config.Image}}' bf-api 2>/dev/null); case "$i" in */*) printf '%s' "${i%%/*}" ;; esac; }
acr_repo() { local i; i=$(d inspect -f '{{.Config.Image}}' bf-api 2>/dev/null); i=${i##*/}; printf '%s' "${i%%:*}"; }

# Does this registry serve pulls with no credentials at all?
#
# A bare request proves nothing: the registry v2 protocol always answers 401 with
# a Bearer challenge first, and the client is then expected to fetch a token. The
# real question is whether the token endpoint issues one WITHOUT credentials, and
# whether that token is then accepted — so ask it exactly that.
anon_pull_works() {
	local reg=$1 repo=$2 tok
	[ -n "$reg" ] && [ -n "$repo" ] || return 1
	tok=$(curl -s -m 10 "https://$reg/oauth2/token?service=$reg&scope=repository:$repo:pull" 2>/dev/null \
		| sed -n 's/.*"access_token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
	[ -z "$tok" ] && return 1
	[ "$(curl -s -o /dev/null -m 10 -w '%{http_code}' \
		-H "Authorization: Bearer $tok" "https://$reg/v2/$repo/tags/list" 2>/dev/null)" = "200" ]
}

# Is a login stored for this registry? Both config files matter: under
# `sudo docker` the credential Docker actually sends is root's, not the invoking
# user's, and an operator who ran docker login one way and the script the other
# leaves exactly the stale entry this is looking for.
docker_creds_stored() {
	local reg=$1 f
	[ -n "$reg" ] || return 1
	for f in "${DOCKER_CONFIG:-$HOME/.docker}/config.json" /root/.docker/config.json; do
		[ -r "$f" ] || continue
		grep -q "\"$reg\"" "$f" && return 0
	done
	return 1
}

# True when a hostname is covered by a certificate's SAN list, wildcards
# included. A wildcard matches exactly one label: *.example.go.cr covers
# bot.example.go.cr but not a.b.example.go.cr. Comparing literally reported
# every wildcard certificate as not covering its own domain.
cert_covers() {
	local host name rest
	host=$(printf '%s' "$1" | tr 'A-Z' 'a-z')
	while IFS= read -r name; do
		name=$(printf '%s' "$name" | tr 'A-Z' 'a-z')
		[ -z "$name" ] && continue
		[ "$name" = "$host" ] && return 0
		case "$name" in
			\*.*)
				rest=${host#*.}
				[ "$rest" != "$host" ] && [ "$rest" = "${name#\*.}" ] && return 0
				;;
		esac
	done <<-EOF
	$2
	EOF
	return 1
}

# --------------------------------------------------------------- addressing ---
# RFC1918, loopback and link-local. A private address is one no browser on the
# internet can reach, which is the distinction almost every topology mistake in
# this deployment turns on.
is_private_ip() {
	case "$1" in
		10.*|127.*|169.254.*|192.168.*|172.1[6-9].*|172.2[0-9].*|172.3[01].*) return 0 ;;
		*) return 1 ;;
	esac
}

# Every IPv4 address on this host, one per line.
host_ips() {
	if command -v ip >/dev/null 2>&1; then
		ip -4 -o addr show 2>/dev/null | awk '{print $4}' | cut -d/ -f1
	elif command -v hostname >/dev/null 2>&1; then
		hostname -I 2>/dev/null | tr ' ' '\n'
	elif command -v ifconfig >/dev/null 2>&1; then
		ifconfig 2>/dev/null | awk '/inet /{print $2}'
	fi | grep -E '^[0-9.]+$' | grep -v '^127\.'
}

# Resolve a hostname (or pass an address straight through) without needing dig,
# which stock Ubuntu does not ship.
resolve_host() {
	case "$1" in
		[0-9]*.[0-9]*.[0-9]*.[0-9]*) printf '%s' "$1"; return 0 ;;
	esac
	if command -v getent >/dev/null 2>&1; then
		getent ahostsv4 "$1" 2>/dev/null | awk '{print $1; exit}'
	elif command -v dig >/dev/null 2>&1; then
		dig +short "$1" 2>/dev/null | grep -E '^[0-9.]+$' | head -1
	else
		ping -c1 -W1 "$1" 2>/dev/null | sed -n 's/.*(\([0-9.]*\)).*/\1/p' | head -1
	fi
}

# Strip scheme, port and path, leaving the hostname.
host_of_url() {
	printf '%s' "$1" | sed -e 's#^[a-zA-Z][a-zA-Z0-9+.-]*://##' -e 's#[/?].*$##' -e 's#^[^@]*@##' -e 's#:[0-9]*$##'
}

host_owns_ip() {
	local want=$1 have
	while IFS= read -r have; do
		[ "$have" = "$want" ] && return 0
	done <<-EOF
	$(host_ips)
	EOF
	return 1
}

# --------------------------------------------------------------- HTTP helpers
# curl is not present in every image, so probe from the host where possible.
http_code() { curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$@" 2>/dev/null; }

# Status and content type from ONE request. Asking twice does not work: a HEAD
# can be answered differently from a GET — an Azure Static Web App 404s the HEAD
# and 200s the GET of the very same path — and a check that reads the code from
# one response and the type from another is comparing two different answers. It
# passed a missing /api that way.
#
# Prints "<code> <content-type>"; read both with: read -r code ctype <<<"$(...)"
http_probe() { curl -s -o /dev/null -w '%{http_code} %{content_type}' --max-time 15 "$@" 2>/dev/null; }

# Does the public name actually reach THIS server? On a split deployment the app
# server holds private addresses only and a reverse proxy in front carries the
# public name — a supported layout, and the one this script recommends. Comparing
# addresses cannot tell it apart from a genuine misconfiguration, so ask instead:
# fetch the SPA's runtime config through the public name and see whether it is
# the very file this host serves.
#
# Prints one of: loop | same | different | unreachable | unknown
origin_probe() {
	local host=$1 out rc mine
	out=$(curl -sS -L --max-redirs 5 --max-time 15 "https://$host/env.js" 2>/dev/null); rc=$?
	# 47 is curl's "maximum redirects followed" — the redirect loop itself.
	[ "$rc" = "47" ] && { printf 'loop'; return; }
	[ "$rc" != "0" ] && { printf 'unreachable'; return; }
	mine=$(curl -s --max-time 10 "http://127.0.0.1:30000/env.js" 2>/dev/null)
	[ -z "$mine" ] && { printf 'unknown'; return; }
	if [ "$out" = "$mine" ]; then printf 'same'; else printf 'different'; fi
}

# Does this curl speak telnet:// ? The minimal builds some distributions ship
# drop it, and the probe below would then call every port closed — which turns
# the "must NOT be reachable" checks green and reports a working 443 as blocked.
# Failing safe here means falling back to the HTTP probe, not trusting a zero.
HAVE_TELNET=0
case "$(curl -s -o /dev/null --connect-timeout 2 --max-time 3 -w '%{time_connect}' telnet://127.0.0.1:9 </dev/null 2>&1)" in
	*"not supported"*|*"Unsupported"*|*"Protocol"*) ;;
	*) HAVE_TELNET=1 ;;
esac

# A TCP connect and nothing more. Deliberately NOT nc: its connect timeout is
# spelled -G on macOS and -w on Linux, and guessing wrong means a filtered port
# hangs for the kernel's full ~75s SYN timeout — with four such ports to check
# that alone outran any sane run time. And deliberately not bash's /dev/tcp,
# which some builds omit and some sandboxes kill outright, both of which look
# like a closed port from here.
#
# telnet:// asks the question we actually mean — did a TCP handshake complete —
# with no application protocol layered on top, and curl reports the answer in
# %{time_connect}: nonzero only if the connection was established. The transfer
# then times out and the exit code is discarded, which is fine, because whether
# anything can be said over the socket was never the question.
#
# Getting this wrong is what the HTTP fallback below has to work around: `curl
# http://host:443/` sends a plaintext request to a TLS listener, gets nothing
# back, times out, and declares the single most important port in the whole
# deployment blocked. So the fallback speaks https on 443 and retries any
# timeout over TLS before calling a port closed.
tcp_open() {
	local host=$1 port=$2 scheme connected

	if [ "$HAVE_TELNET" = "1" ]; then
		# </dev/null is not cosmetic: curl's telnet handler reads stdin, and under
		# the documented `curl ... | bash` invocation stdin IS the rest of this
		# script. Letting the probe inherit it would feed the script's own text
		# into the socket and eat it before bash could run it.
		connected=$(curl -s -o /dev/null --connect-timeout 5 --max-time 6 \
			-w '%{time_connect}' "telnet://$host:$port" </dev/null 2>/dev/null)
		case "$connected" in
			""|0|0.000000|0.0000|0.00) return 1 ;;
			*) return 0 ;;
		esac
	fi

	scheme=http
	[ "$port" = "443" ] && scheme=https
	# -k because the certificate is checked properly elsewhere; here only the
	# handshake reaching a listener matters.
	curl -s --connect-timeout 6 --max-time 10 -k -o /dev/null "$scheme://$host:$port/" 2>/dev/null
	case $? in
		# 7 refused, 6 unresolvable: settled, no retry can change them.
		6|7)  return 1 ;;
		28)   ;;
		*)    return 0 ;;
	esac
	[ "$scheme" = "https" ] && return 1
	curl -s --connect-timeout 6 --max-time 10 -k -o /dev/null "https://$host:$port/" 2>/dev/null
	case $? in
		6|7|28) return 1 ;;
		*)      return 0 ;;
	esac
}

# ============================================================== SERVER SIDE ===
if [ "$SERVER_MODE" = "1" ]; then

# ---------------------------------------------------------------- host basics
head1 "Host"
printf '  date (UTC)      %s\n' "$(date -u '+%Y-%m-%d %H:%M:%S')"
printf '  hostname        %s\n' "$(hostname)"
printf '  os              %s\n' "$( (. /etc/os-release 2>/dev/null && echo "$PRETTY_NAME") || uname -s )"
printf '  kernel          %s\n' "$(uname -r)"
printf '  uptime          %s\n' "$(uptime -p 2>/dev/null || echo '?')"

if command -v timedatectl >/dev/null 2>&1; then
	SYNC=$(timedatectl show -p NTPSynchronized --value 2>/dev/null)
	if [ "$SYNC" = "yes" ]; then
		ok "clock synchronised (NTP)"
	else
		warn "clock is NOT NTP-synchronised — LiveKit tokens carry expiry times, and a server clock more than a minute off makes calls fail to authorise"
	fi
fi

DISK=$(df -P / | awk 'NR==2{print $5}' | tr -d '%')
if [ -n "$DISK" ] && [ "$DISK" -ge 90 ] 2>/dev/null; then
	bad "disk / is ${DISK}% full — Docker will fail to write logs and images"
elif [ -n "$DISK" ] && [ "$DISK" -ge 80 ] 2>/dev/null; then
	warn "disk / is ${DISK}% full"
else
	ok "disk space on / (${DISK:-?}% used)"
fi

MEMFREE=$(free -m 2>/dev/null | awk '/^Mem:/{print $7}')
MEMTOT=$(free -m 2>/dev/null | awk '/^Mem:/{print $2}')
[ -n "$MEMTOT" ] && info "memory          ${MEMFREE}MB available of ${MEMTOT}MB"

# -------------------------------------------------------------------- docker
head1 "Docker"
if [ -z "$DOCKER" ]; then
	bad "cannot talk to the Docker daemon (is Docker installed and running? try: sudo systemctl status docker)"
	echo
	echo "Nothing further can be checked on this host without Docker."
	echo "If you meant to check a deployment from the outside, pass its hostnames:"
	echo "  bash diagnose.sh --app app.example.com --livekit voz.example.com"
	exit 1
fi
ok "docker daemon reachable ($(d --version 2>/dev/null | head -1))"

if d compose version >/dev/null 2>&1; then
	ok "docker compose plugin present ($(d compose version --short 2>/dev/null))"
else
	bad "docker compose plugin missing — install docker-compose-plugin"
fi

# Container DNS. A resolv.conf pointing at systemd-resolved's stub (127.0.0.53)
# means containers cannot resolve anything, while image pulls still work because
# the daemon resolves on the host. Checked against a running container.
PROBE=$(d ps --format '{{.Names}}' 2>/dev/null | head -1)
if [ -n "$PROBE" ]; then
	RESOLV=$(d exec "$PROBE" cat /etc/resolv.conf 2>/dev/null | awk '/^nameserver/{print $2}' | tr '\n' ' ')
	if [ -z "$RESOLV" ]; then
		warn "could not read /etc/resolv.conf inside $PROBE"
	elif echo "$RESOLV" | grep -q '127.0.0.53'; then
		bad "container DNS points at 127.0.0.53 (systemd-resolved stub) — that address does not exist inside a container, so nothing in it can resolve a hostname. Fix: set {\"dns\": [\"<your resolver>\"]} in /etc/docker/daemon.json and restart Docker"
	else
		ok "container DNS servers: $RESOLV"
	fi
fi

# ------------------------------------------------------------- role detection
ROLE="unknown"
# Must be initialised: with no Bot Factory containers present neither branch
# below runs, and `set -u` would abort on the first reference — exactly when
# someone is running this because the stack did not come up.
COMPOSE_FILE=""
if has_container bf-api || has_container bf-frontend; then
	ROLE="app"
	COMPOSE_FILE="docker-compose.stack.yml"
elif has_container bf-livekit; then
	ROLE="livekit"
	COMPOSE_FILE="docker-compose.livekit.yml"
fi

# Locate the deployment directory (where .env and the compose file live).
DEPLOY_DIR=""
# Ask Docker before guessing. Compose stamps the directory it was launched from
# onto every container it creates, so this is right wherever the script runs and
# whoever runs it — the guesses below cannot survive `curl … | sudo bash` (where
# $HOME becomes /root) or a directory nobody thought to list. Three runs on a
# live deployment were lost to that: no compose file found means no .env, which
# silently empties the Configuration section and turns every check that depends
# on it into a phantom failure, LiveKit "unreachable" among them.
if [ -n "$COMPOSE_FILE" ]; then
	for c in bf-api bf-frontend bf-livekit; do
		has_container "$c" || continue
		wd=$(d inspect -f '{{index .Config.Labels "com.docker.compose.project.working_dir"}}' "$c" 2>/dev/null)
		if [ -n "$wd" ] && [ -f "$wd/$COMPOSE_FILE" ]; then DEPLOY_DIR="$wd"; break; fi
	done
fi
if [ -z "$DEPLOY_DIR" ]; then
	for c in "$PWD" "$HOME/botfactory" "$HOME/livekit" "$HOME/bot_factory"; do
		if [ -n "$COMPOSE_FILE" ] && [ -f "$c/$COMPOSE_FILE" ]; then DEPLOY_DIR="$c"; break; fi
	done
fi
ENV_FILE="${DEPLOY_DIR:-$PWD}/.env"

head1 "Addressing"
# Which addresses this host holds decides what it can serve and to whom, and it
# is the thing operators most often have to be asked for. Print it up front so a
# support report carries the topology instead of prompting a round trip.
HAS_PUBLIC_IP=0
IPLIST=$(host_ips)
if [ -z "$IPLIST" ]; then
	skip "could not read this host's IP addresses"
else
	while IFS= read -r a; do
		[ -z "$a" ] && continue
		if is_private_ip "$a"; then info "address         $a (private)"
		else info "address         $a (public)"; HAS_PUBLIC_IP=1; fi
	done <<-EOF
	$IPLIST
	EOF
fi

head1 "Deployment"
case "$ROLE" in
	app)     info "role            application server (frontend, api, agent, kb-scraper, azurite)" ;;
	livekit) info "role            LiveKit server" ;;
	*)       bad "no Bot Factory containers found on this host — run this from the server where the stack is deployed" ;;
esac
if [ "$ROLE" = "unknown" ]; then
	info "Nothing else can be checked here. On the application server you should see"
	info "bf-frontend / bf-api / bf-agent; on the voice server, bf-livekit."
	info "If they are missing entirely, start them with 'docker compose ... up -d'."
	info "To check a deployment from the outside instead, pass its hostnames:"
	info "  bash diagnose.sh --app app.example.com --livekit voz.example.com"
elif [ -n "$DEPLOY_DIR" ]; then
	info "directory       $DEPLOY_DIR"
	if [ -f "$ENV_FILE" ]; then
		ok ".env found"
	else
		warn ".env not found at $ENV_FILE — configuration checks skipped"
	fi
elif [ "$OUTSIDE_MODE" = "1" ]; then
	# --app/--livekit were given: those checks probe a hostname from outside and
	# need no .env, so run them rather than stopping. Only the on-host half is lost.
	warn "could not find $COMPOSE_FILE, so nothing that reads .env can be checked on this host — continuing with the outside-in checks only"
else
	# Stop. Carrying on without .env does not produce a shorter report, it
	# produces a wrong one: every value that lives there reads as unset, and an
	# unset value is indistinguishable from a broken one. A run in exactly this
	# state announced "LiveKit is NOT reachable — no call can be answered" while
	# LiveKit was answering normally, because LIVEKIT_HOST had nowhere to come
	# from. A report that invents failures costs more than no report at all.
	bad "could not find $COMPOSE_FILE — stopping here, because a run without it reports invented failures"
	echo
	echo "  This script reads the .env sitting beside $COMPOSE_FILE. Without it every"
	echo "  configured value — origins, LiveKit host, keys — reads as unset, and unset"
	echo "  is indistinguishable from broken: a previous run in this state reported"
	echo "  LiveKit unreachable while LiveKit was working."
	echo
	echo "  The containers know where the deployment is. Run:"
	echo
	echo "    cd \"\$(docker inspect -f '{{index .Config.Labels \"com.docker.compose.project.working_dir\"}}' bf-api)\""
	echo
	echo "  ...then run the same command again from there — it is unchanged."
	echo
	echo "  If that prints nothing, the containers were not started by docker compose;"
	echo "  cd to the directory holding $COMPOSE_FILE by hand."
	echo
	exit 1
fi

# ------------------------------------------------------------------ update ---
# --update pulls the latest published images and recreates what changed BEFORE
# the checks run, so a single command updates the server and then verifies the
# result end to end — every check below runs against the post-update state.
# `docker compose up -d` alone exits 0 even when a container starts and
# immediately dies; the wait-and-watch here plus the full diagnosis after is
# what closes that gap.
if [ "$UPDATE" = "1" ] && [ "$ROLE" != "unknown" ] && [ -n "$DEPLOY_DIR" ]; then
	head1 "Update"
	cd "$DEPLOY_DIR" || true
	# The SQL overlay must ride along on every compose command, or compose treats
	# bf-sql as a stray and stops it on up -d.
	UPD_COMPOSE=(-f "$COMPOSE_FILE")
	if [ "$ROLE" = "app" ] && [ -f docker-compose.sql.yml ] && has_container bf-sql; then
		UPD_COMPOSE+=(-f docker-compose.sql.yml)
		info "sql overlay     docker-compose.sql.yml (bf-sql runs here)"
	fi
	upd_compose() { d compose "${UPD_COMPOSE[@]}" "$@"; }
	upd_images() {
		upd_compose images --format '{{.Service}} {{.ID}}' 2>/dev/null \
			|| upd_compose images 2>/dev/null | awk 'NR>1 {print $1, $4}'
	}

	UPD_BEFORE=$(upd_images)
	# Drop a stored registry login before pulling. Every image here comes from our
	# registry and it serves anonymous pulls, so the login buys nothing — and costs
	# something: Docker sends a stored credential in preference to pulling
	# anonymously, so the day it expires the pull fails with "unauthorized" on a
	# deployment that needed no credentials at all, and the error sends the
	# operator hunting for a password that is no longer required. That is exactly
	# how a customer sat on a week-old image while every check passed.
	#
	# Gated on the registry actually being open, which costs one request to answer.
	# If it is ever closed again the login becomes the only way in, and removing it
	# would take the deployment offline until someone dug the credentials out.
	UPD_REG=$(acr_host); UPD_REPO=$(acr_repo)
	if docker_creds_stored "$UPD_REG" && anon_pull_works "$UPD_REG" "$UPD_REPO"; then
		if $DOCKER logout "$UPD_REG" >/dev/null 2>&1; then
			ok "removed the stored login for $UPD_REG — it serves anonymous pulls, and a stale login would fail them"
		fi
	fi

	UPD_PULL=$(upd_compose pull 2>&1); UPD_RC=$?

	if [ "$UPD_RC" != 0 ]; then
		bad "docker compose pull failed — the update did not happen; the checks below describe the UNCHANGED deployment"
		printf '%s\n' "$UPD_PULL" | tail -12
		echo "$UPD_PULL" | grep -qiE "unauthorized|denied" \
			&& info "the registry rejected the stored login and does not serve anonymous pulls — re-run the docker login command from the deployment guide"
		UPDATE=0
	else
		UPD_CHANGED=$(comm -13 <(printf '%s\n' "$UPD_BEFORE" | sort) <(printf '%s\n' "$(upd_images)" | sort) | awk '{print $1}')
		echo "$UPD_PULL" | grep -qiE "downloaded newer image|pull complete" && [ -z "$UPD_CHANGED" ] && UPD_CHANGED="(new images pulled)"
		if [ -z "$UPD_CHANGED" ]; then
			ok "already current — the registry has nothing newer"
		else
			ok "newer images pulled"
			printf '%s\n' "$UPD_CHANGED" | while read -r s; do [ -n "$s" ] && info "updated: $s"; done
		fi
	fi

	if [ "$UPDATE" = "1" ] && [ "$DRY" = "1" ]; then
		info "dry run: nothing restarted; the checks below describe the CURRENT deployment"
	elif [ "$UPDATE" = "1" ]; then
		# --remove-orphans clears containers a newer compose file no longer
		# defines — a retired service otherwise stays up and keeps its ports.
		if ! UPD_UP=$(upd_compose up -d --remove-orphans 2>&1); then
			bad "docker compose up failed after the pull"
			printf '%s\n' "$UPD_UP" | tail -12
		else
			ok "changed containers recreated (up -d --remove-orphans)"
			# Wait for running + healthy, then give a crash loop a moment to show
			# itself. The per-container checks further down report the details;
			# this loop only makes sure they observe a settled state, not a
			# container mid-start.
			UPD_DEADLINE=$(( $(date +%s) + 90 ))
			while :; do
				UPD_PENDING=""
				while IFS= read -r svc; do
					[ -z "$svc" ] && continue
					CID=$(upd_compose ps -q "$svc" 2>/dev/null | head -1)
					[ -z "$CID" ] && continue
					H=$(d inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{end}}' "$CID" 2>/dev/null)
					[ "$H" = "starting" ] && UPD_PENDING="$UPD_PENDING $svc"
				done <<-EOF
				$(upd_compose ps --services 2>/dev/null)
				EOF
				[ -z "$UPD_PENDING" ] && break
				[ "$(date +%s)" -ge "$UPD_DEADLINE" ] && { warn "still waiting on healthchecks after 90s:$UPD_PENDING — checking anyway"; break; }
				sleep 3
			done
			sleep 8
			UPD_BROKEN=""
			while IFS= read -r svc; do
				[ -z "$svc" ] && continue
				CID=$(upd_compose ps -aq "$svc" 2>/dev/null | head -1)
				[ -z "$CID" ] && { UPD_BROKEN="$UPD_BROKEN $svc"; continue; }
				STATE=$(d inspect -f '{{.State.Status}}' "$CID" 2>/dev/null)
				RESTARTS=$(d inspect -f '{{.RestartCount}}' "$CID" 2>/dev/null)
				if [ "$STATE" != "running" ] || [ "${RESTARTS:-0}" -gt 0 ]; then
					UPD_BROKEN="$UPD_BROKEN $svc"
				fi
			done <<-EOF
			$(upd_compose ps --services 2>/dev/null)
			EOF
			if [ -z "$UPD_BROKEN" ]; then
				ok "every container settled after the update"
			else
				for svc in $UPD_BROKEN; do
					CID=$(upd_compose ps -aq "$svc" 2>/dev/null | head -1)
					STATE=$(d inspect -f '{{.State.Status}}' "$CID" 2>/dev/null)
					EXITC=$(d inspect -f '{{.State.ExitCode}}' "$CID" 2>/dev/null)
					bad "$svc did not settle after the update — state=$STATE${EXITC:+ exit=$EXITC}"
					printf '\n--- last 40 log lines: %s ---\n' "$svc"
					[ -n "$CID" ] && d logs --tail 40 "$CID" 2>&1 | cut -c1-300
					echo
				done
			fi
		fi
	fi
	cd - >/dev/null 2>&1 || true
elif [ "$UPDATE" = "1" ]; then
	bad "--update given, but no deployment was found on this host to update. Nothing was pulled and nothing was restarted — the report below describes the deployment exactly as it was. This script has to run from the directory holding $COMPOSE_FILE (it reads the .env beside it). Ask the containers where that is:  docker inspect -f '{{index .Config.Labels \"com.docker.compose.project.working_dir\"}}' bf-api  — then cd there and run it again"
fi

# ------------------------------------------------------- mounted nginx config
# The compose files bind-mount these. A bind mount whose source does not exist
# makes Docker create a DIRECTORY on the host, the container then exits 127 with
# nothing readable in its log, and the host grows a directory named like a file.
check_nginx_files() {
	[ -n "$DEPLOY_DIR" ] || return 0
	head1 "nginx configuration files"
	local f found=0
	for f in "$@"; do
		local path="$DEPLOY_DIR/$f"
		if [ -d "$path" ]; then
			bad "$f is a DIRECTORY, not a file — it was never copied here, so Docker created it when starting the container. Delete it, copy the real $f from the deploy files, and start again"
			found=1
		elif [ -f "$path" ]; then
			ok "$f present"
			found=1
		else
			bad "$f is missing — copy the nginx/ folder from the deploy files next to the compose file"
			found=1
		fi
	done
	[ "$found" = "0" ] && skip "no nginx configuration files to check"
}

# ---------------------------------------------------------------- containers
check_containers() {
	head1 "Containers"
	local name state health restarts exitcode
	for name in "$@"; do
		if ! has_container "$name"; then
			bad "$name does not exist — it was never started, or 'docker compose up -d' did not include it"
			continue
		fi
		state=$(d inspect -f '{{.State.Status}}' "$name" 2>/dev/null)
		health=$(d inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$name" 2>/dev/null)
		restarts=$(d inspect -f '{{.RestartCount}}' "$name" 2>/dev/null)
		exitcode=$(d inspect -f '{{.State.ExitCode}}' "$name" 2>/dev/null)

		if [ "$state" != "running" ]; then
			bad "$name is '$state' (exit code $exitcode) — see the log extract below"
		elif [ "$health" = "unhealthy" ]; then
			bad "$name is running but its health check fails"
		elif [ "${restarts:-0}" -gt 5 ] 2>/dev/null; then
			bad "$name has restarted $restarts times — it is crash-looping, the log extract below shows why"
		elif [ "$health" = "starting" ]; then
			warn "$name is still starting up (health check has not passed yet)"
		else
			ok "$name running${health:+ (health: $health)}${restarts:+, restarts: $restarts}"
		fi
	done
}

# --------------------------------------------------------------- TLS checking
# The certificate path lives in nginx/site.inc now, so ask the running nginx
# what it actually loaded rather than guessing from .env.
check_tls() {
	local container=$1 domain=$2
	head1 "TLS ($container)"
	local certpath
	certpath=$(d exec "$container" nginx -T 2>/dev/null | sed -n 's/^ *ssl_certificate  *//p' | tr -d ' ;' | head -1)
	if [ -z "$certpath" ]; then
		# On the app server this is not a warning: browsers only grant microphone
		# access in a secure context, so over plain HTTP getUserMedia() is refused
		# outright and no call can ever start. Everything else can look healthy.
		if [ "$container" = "bf-frontend" ]; then
			bad "no ssl_certificate configured — this server is serving plain HTTP, and browsers refuse the microphone outside a secure context, so calls cannot start. Set TLS_CERT/TLS_KEY or terminate TLS in a proxy in front of :30000"
		else
			warn "no ssl_certificate configured — this server is serving plain HTTP"
		fi
		return
	fi
	local pem
	pem=$(d exec "$container" cat "$certpath" 2>/dev/null)
	if [ -z "$pem" ] || ! printf '%s' "$pem" | grep -q 'BEGIN CERTIFICATE'; then
		bad "$container cannot read the certificate at $certpath — check the path in nginx/site.inc and that the file is inside the mounted certs folder"
		return
	fi
	ok "certificate readable at $certpath"

	if ! command -v openssl >/dev/null 2>&1; then
		skip "openssl is not installed on this host, so the certificate's dates and names cannot be checked"
		return
	fi

	local end subject issuer names certcount
	end=$(printf '%s' "$pem" | openssl x509 -noout -enddate 2>/dev/null | cut -d= -f2)
	subject=$(printf '%s' "$pem" | openssl x509 -noout -subject 2>/dev/null | sed 's/^subject=//')
	issuer=$(printf '%s' "$pem" | openssl x509 -noout -issuer 2>/dev/null | sed 's/^issuer=//')
	if [ -z "$end" ]; then
		warn "the file at $certpath could not be parsed as a certificate"
		return
	fi
	info "subject         $subject"
	info "issuer          $issuer"

	if printf '%s' "$pem" | openssl x509 -noout -checkend 0 >/dev/null 2>&1; then
		if printf '%s' "$pem" | openssl x509 -noout -checkend 1209600 >/dev/null 2>&1; then
			ok "certificate valid (expires $end)"
		else
			warn "certificate expires within 14 days ($end) — replace it and reload nginx"
		fi
	else
		bad "certificate EXPIRED ($end) — browsers will refuse to connect"
	fi

	if [ -n "$domain" ]; then
		names=$(printf '%s' "$pem" | openssl x509 -noout -text 2>/dev/null \
			| grep -A1 'Subject Alternative Name' | tail -1 | tr -d ' ' | tr ',' '\n' | sed 's/^DNS://')
		[ -z "$names" ] && names=$(printf '%s' "$subject" | sed -n 's/.*CN *= *\([^,]*\).*/\1/p')
		if cert_covers "$domain" "$names"; then
			ok "certificate covers $domain"
		else
			bad "certificate does NOT cover $domain (it covers: $(printf '%s' "$names" | tr '\n' ' ')) — browsers will reject it"
		fi
	fi

	certcount=$(printf '%s' "$pem" | grep -c 'BEGIN CERTIFICATE')
	if [ "${certcount:-1}" -lt 2 ]; then
		warn "the file contains only one certificate — if it was issued by a CA you must use the FULL CHAIN file (leaf + intermediates), or browsers will reject it"
	else
		ok "certificate file contains a chain ($certcount certificates)"
	fi

	if [ -n "$subject" ] && [ "$subject" = "$issuer" ]; then
		warn "certificate is self-signed (subject equals issuer) — browsers will show a warning"
	fi
}

check_listening() {
	local port=$1 proto=${2:-tcp} label=$3
	local found=""
	if command -v ss >/dev/null 2>&1; then
		if [ "$proto" = udp ]; then found=$(ss -lnu 2>/dev/null | grep -E "[:.]$port\b")
		else found=$(ss -lnt 2>/dev/null | grep -E "[:.]$port\b"); fi
	elif command -v netstat >/dev/null 2>&1; then
		found=$(netstat -ln 2>/dev/null | grep -E "[:.]$port\b")
	fi
	# Deliberately worded as a local fact. This only proves a socket is bound on
	# this host; it says nothing about whether a browser on the internet can get
	# to it. A perimeter firewall blocking 7881 still passes this check, so
	# claiming "reachable from browsers" here sends people looking elsewhere.
	# The --app/--livekit run is what settles that.
	if [ -n "$found" ]; then ok "$label — bound on this host ($port/$proto); reachability from outside is not tested here"
	else bad "$label — nothing is listening on $port/$proto"; fi
}

# -------------------------------------------------------------- APP SERVER ---
if [ "$ROLE" = "app" ]; then
	check_containers bf-frontend bf-api bf-agent bf-kb-scraper bf-azurite
	check_nginx_files nginx/default.conf nginx/site.inc

	head1 "Configuration"
	if [ -f "$ENV_FILE" ]; then
		# Which SQL variables matter depends on where SQL runs. With the
		# containerized overlay the API reaches bf-sql over the compose network and
		# SQL_HOST/SQL_USER/SQL_PASSWORD are ignored outright (docker-compose.sql.yml
		# says so) — demanding them there reports a working stack as broken.
		INFO_VARS="LIVEKIT_HOST LIVEKIT_PUBLIC_HOST LIVEKIT_API_KEY ALLOWED_ORIGINS"
		SECRET_VARS="LIVEKIT_API_SECRET ENCRYPTION_MASTER_KEY AGENT_CALLBACK_SECRET KB_SCRAPER_API_KEY"
		if has_container bf-sql; then
			info "SQL runs as a container (bf-sql), so SQL_HOST/SQL_USER/SQL_PASSWORD are not used"
			SECRET_VARS="SQL_SA_PASSWORD $SECRET_VARS"
		else
			INFO_VARS="SQL_HOST SQL_USER $INFO_VARS"
			SECRET_VARS="SQL_PASSWORD $SECRET_VARS"
		fi

		for v in $INFO_VARS; do
			val=$(envval "$v")
			if [ -n "$val" ]; then info "$(printf '%-22s' "$v")$val"
			else warn "$v is empty in .env"; fi
		done
		for v in $SECRET_VARS; do
			val=$(envval "$v")
			if [ -n "$val" ]; then info "$(printf '%-22s' "$v")set (${#val} characters)"
			else bad "$v is empty in .env — the stack will not work without it"; fi
		done

		MK=$(envval ENCRYPTION_MASTER_KEY)
		if [ -n "$MK" ]; then
			LEN=$(printf '%s' "$MK" | base64 -d 2>/dev/null | wc -c | tr -d ' ')
			if [ "$LEN" = "32" ]; then ok "ENCRYPTION_MASTER_KEY decodes to 32 bytes"
			else bad "ENCRYPTION_MASTER_KEY decodes to ${LEN:-0} bytes, must be exactly 32 — regenerate with: openssl rand -base64 32"; fi
		fi

	fi

	head1 "Reachability from browsers"
	# The split deployment fails in a way no single check catches: each half is
	# healthy, and the addresses simply do not line up. Compare what this host
	# holds against what the configuration promises browsers.
	#
	# ORIGIN_IP must be initialised: it is read again further down, outside the
	# branch that assigns it, and under `set -u` an unset read aborts the run.
	ORIGIN_IP=""
	# Set when the public name was proved to forward here, so the private-address
	# check below does not repeat the complaint about a working reverse proxy.
	ORIGIN_FRONTED=0
	# Read again in the Frontend section below, outside the branch that assigns
	# it: under `set -u` an unset read aborts the whole run.
	ORIGIN_STATE=""
	ORIGIN_URL=$(envval ALLOWED_ORIGINS | tr ',' '\n' | head -1)
	ORIGIN_HOST=$(host_of_url "$ORIGIN_URL")
	if [ -z "$ORIGIN_HOST" ]; then
		skip "ALLOWED_ORIGINS is empty — cannot check what browsers are told to use"
	else
		ORIGIN_IP=$(resolve_host "$ORIGIN_HOST")
		if [ -z "$ORIGIN_IP" ]; then
			bad "ALLOWED_ORIGINS names $ORIGIN_HOST, which does not resolve — browsers cannot look it up either"
		elif host_owns_ip "$ORIGIN_IP"; then
			ok "ALLOWED_ORIGINS names $ORIGIN_HOST, which resolves to this host ($ORIGIN_IP)"
		else
			# A different address is not a verdict. Ask whether it forwards here
			# before calling a supported layout broken.
			ORIGIN_STATE=$(origin_probe "$ORIGIN_HOST")
			case "$ORIGIN_STATE" in
				same)
					ORIGIN_FRONTED=1
					ok "ALLOWED_ORIGINS names $ORIGIN_HOST ($ORIGIN_IP) — not an address of this host, but it serves this host's files, so a reverse proxy in front is forwarding here" ;;
				loop)
					bad "https://$ORIGIN_HOST/ redirects to itself endlessly — browsers get ERR_TOO_MANY_REDIRECTS and the site is down. The proxy in front forwards to this host over plain HTTP, and this host answers 'go to https://', which returns the browser to the proxy. Either point that proxy at this host's :443, or have it send 'X-Forwarded-Proto: https' and update nginx/default.conf from the deployment bundle" ;;
				different)
					bad "ALLOWED_ORIGINS names $ORIGIN_HOST ($ORIGIN_IP), which answers but serves a different application than this host — browsers are reaching another machine entirely" ;;
				*)
					warn "ALLOWED_ORIGINS names $ORIGIN_HOST ($ORIGIN_IP), which is not an address of this host and could not be reached from here to check. That is normal when a reverse proxy in front holds the public name and the network does not hairpin. Confirm from outside with: bash diagnose.sh --app $ORIGIN_HOST" ;;
			esac
		fi
		case "$ORIGIN_URL" in
			https://*) ;;
			*) warn "ALLOWED_ORIGINS is '$ORIGIN_URL' — browsers refuse the microphone outside a secure context, so the real origin has to be https://" ;;
		esac
	fi

	if [ "$HAS_PUBLIC_IP" = "0" ]; then
		# Not automatically wrong: an intranet-only deployment is legitimate. It is
		# wrong when the configuration claims a public origin, which is the state
		# a half-finished public rollout leaves behind.
		if [ "$ORIGIN_FRONTED" = "1" ]; then
			info "this host holds only private addresses and is reached through the proxy at $ORIGIN_HOST — the intended split layout, already verified above"
		elif [ -n "$ORIGIN_IP" ] && ! is_private_ip "$ORIGIN_IP"; then
			warn "this host holds only private addresses, but ALLOWED_ORIGINS points at the public address $ORIGIN_IP. That is correct if a reverse proxy there forwards to this host — it could not be confirmed from here. If nothing fronts this host, internet users cannot reach it at all: publish it (public address or DNAT), or serve it through the LiveKit host's nginx, which already has one"
		else
			info "this host holds only private addresses — reachable from the internal network only"
			info "That is fine for an intranet deployment. For internet users it needs a"
			info "public address, or a reverse proxy on a host that has one."
		fi
	fi

	# Server-to-server traffic should take the private path. Routing it through
	# the public address works only where the perimeter hairpins, and needlessly
	# sends internal traffic out and back.
	LKHOST=$(host_of_url "$(envval LIVEKIT_HOST)")
	if [ -n "$LKHOST" ]; then
		LKIP=$(resolve_host "$LKHOST")
		if [ -n "$LKIP" ] && ! is_private_ip "$LKIP"; then
			warn "LIVEKIT_HOST resolves to the public address $LKIP — this is the server-to-server path and should use the LiveKit server's private address, which avoids depending on the perimeter to hairpin"
		elif [ -n "$LKIP" ]; then
			ok "LIVEKIT_HOST uses a private address ($LKIP) — server-to-server traffic stays on the internal network"
		fi
	fi

	head1 "Frontend (nginx)"
	if d logs bf-frontend 2>&1 | grep -q "cannot load certificate"; then
		CERTERR=$(d logs bf-frontend 2>&1 | grep "cannot load certificate" | tail -1 | cut -c1-160)
		bad "nginx cannot load the certificate, so it will not start: $CERTERR — check the ssl_certificate paths in nginx/site.inc and that the files are in the mounted certs folder"
	fi
	SRVNAME=$(d exec bf-frontend nginx -T 2>/dev/null | sed -n 's/^ *server_name *//p' | tr -d ' ;' | grep -v '^_$' | head -1)
	if [ -n "$SRVNAME" ]; then
		info "server_name     $SRVNAME (from nginx/site.inc)"
		ORIGINS=$(envval ALLOWED_ORIGINS)
		if [ -n "$ORIGINS" ] && ! echo "$ORIGINS" | grep -q "$SRVNAME"; then
			bad "the domain in nginx/site.inc ($SRVNAME) does not appear in ALLOWED_ORIGINS ($ORIGINS) — the site will load but every API call will fail CORS. The two must name the same host"
		elif [ -n "$ORIGINS" ]; then
			ok "nginx/site.inc and ALLOWED_ORIGINS agree on the domain"
		fi
	fi

	# With TLS on, plain HTTP correctly answers 301 to https, so the real checks
	# have to go over HTTPS. Probing :30000 and demanding 200 would report a
	# working site as broken.
	# Ask the running nginx whether it is terminating TLS; the certificate is
	# configured in nginx/site.inc, not in .env.
	TLS_ON=0
	d exec bf-frontend nginx -T 2>/dev/null | grep -q '^ *ssl_certificate ' && TLS_ON=1
	if [ "$TLS_ON" = "1" ]; then
		BASE="https://127.0.0.1"; K=(-k)
		check_listening 443 tcp "HTTPS port"
		code=$(http_code "http://127.0.0.1:30000/")
		case "$code" in
			301|302) ok "plain HTTP on :30000 redirects to HTTPS (HTTP $code)" ;;
			200)     warn "plain HTTP on :30000 serves the site instead of redirecting to HTTPS" ;;
			*)       bad "plain HTTP on :30000 returned HTTP ${code:-no response}" ;;
		esac
		# That redirect is right for a browser typing http:// and catastrophic
		# behind a proxy. A proxy that terminates TLS forwards here over plain
		# HTTP with X-Forwarded-Proto: https; answering 301 sends the browser back
		# to the proxy, which forwards here again — ERR_TOO_MANY_REDIRECTS, site
		# down for everyone outside, while every other check on this host passes.
		# Ask this nginx directly what it does with that header.
		FWD_CODE=$(http_code -H "X-Forwarded-Proto: https" "http://127.0.0.1:30000/")
		case "$FWD_CODE" in
			200)
				ok "honours X-Forwarded-Proto — safe behind a proxy that terminates TLS in front" ;;
			301|302)
				# Redirecting on the header is a latent fault, not a verdict: it
				# only becomes a loop if the proxy in front forwards here over
				# plain HTTP. A proxy pointed at this host's :443 never triggers
				# it — and reporting "the site is down" while it is serving
				# perfectly is the false alarm that teaches operators to skim
				# past failures. Only the probe that actually followed the
				# redirects gets to call it.
				case "$ORIGIN_STATE" in
					loop)
						bad "redirect loop confirmed: :80 answers HTTP $FWD_CODE to https://\$host even when the request says X-Forwarded-Proto: https, and https://$ORIGIN_HOST/ was followed round in circles just now. Replace nginx/default.conf with the copy from the deployment bundle (it redirects on the browser's real scheme), or point the proxy at this host's :443" ;;
					same)
						info "this nginx redirects to HTTPS even when a request carries X-Forwarded-Proto: https, yet the proxy at $ORIGIN_HOST reaches this host anyway — so it is talking to :443, not :80. Nothing is broken; worth fixing before anyone repoints it at plain HTTP, with the nginx/default.conf in the deployment bundle" ;;
					*)
						if [ -n "$ORIGIN_IP" ] && ! host_owns_ip "$ORIGIN_IP"; then
							warn "this nginx redirects to HTTPS even when a request carries X-Forwarded-Proto: https. A proxy at $ORIGIN_HOST fronts this host: if it forwards over plain HTTP every page becomes a redirect loop, and if it uses :443 nothing is wrong. It could not be reached from here to tell which — settle it with 'bash diagnose.sh --app $ORIGIN_HOST'"
						else
							warn "this nginx redirects to HTTPS even when a request carries X-Forwarded-Proto: https. Nothing fronts this host today, so nothing is broken; put a TLS-terminating proxy in front and every page would become a redirect loop. The copy of nginx/default.conf in the deployment bundle fixes it"
						fi ;;
				esac ;;
			*)
				info "X-Forwarded-Proto probe returned HTTP ${FWD_CODE:-no response}" ;;
		esac
	else
		BASE="http://127.0.0.1:30000"; K=()
	fi

	code=$(http_code "${K[@]}" "$BASE/")
	[ "$code" = "200" ] && ok "web app served (HTTP $code)" || bad "the web app returned HTTP ${code:-no response} — the SPA is not being served"
	code=$(http_code "${K[@]}" "$BASE/env.js")
	[ "$code" = "200" ] && ok "env.js served (runtime config)" || bad "env.js returned HTTP ${code:-no response} — the SPA cannot find its API"
	code=$(http_code "${K[@]}" "$BASE/deploy/")
	[ "$code" = "200" ] && ok "bundled deployment guide served at /deploy/" || warn "/deploy/ returned HTTP ${code:-no response}"

	check_tls bf-frontend "$SRVNAME"

	head1 "API"
	HEALTH=$(curl -s --max-time 10 "http://127.0.0.1:30001/health" 2>/dev/null)
	if echo "$HEALTH" | grep -q '"status":"Healthy"'; then
		ok "API health endpoint reports Healthy"
	elif [ -n "$HEALTH" ]; then
		bad "API health endpoint reports: $HEALTH"
	else
		bad "API does not answer on :30001 — it is down or still starting"
	fi

	# /health returns one entry per registered check. Report them individually so
	# a failure names the subsystem instead of just "Unhealthy". Parsed with sed
	# rather than jq, which stock Ubuntu does not ship.
	if [ -n "$HEALTH" ]; then
		echo "$HEALTH" \
			| tr '{' '\n' \
			| sed -n 's/.*"name":"\([^"]*\)","status":"\([^"]*\)".*/\1 \2/p' \
		| while read -r cname cstatus; do
				info "$(printf '%-24s' "check: $cname")$cstatus"
			done
	fi

	code=$(http_code "${K[@]}" "$BASE/api/auth/providers")
	[ "$code" = "200" ] && ok "API reachable through the nginx /api proxy (HTTP $code)" \
		|| bad "/api through nginx returned HTTP ${code:-no response} — the SPA cannot reach the backend even if the API itself is up"

	head1 "Database"
	SQLH=$(envval SQL_HOST); SQLP=$(envval SQL_PORT); SQLP=${SQLP:-1433}
	SQLDB=$(envval SQL_DB); SQLDB=${SQLDB:-BotFactory}
	if has_container bf-sql; then
		# The containerized variant (docker-compose.sql.yml). SQL_HOST is unset and
		# irrelevant here: the API reaches bf-sql over the compose network.
		info "SQL Server runs as a container on this host (bf-sql), not on Server 1"
		if [ "$(d inspect -f '{{.State.Health.Status}}' bf-sql 2>/dev/null)" = "healthy" ]; then
			ok "bf-sql reports healthy (accepting queries)"
		else
			bad "bf-sql is not healthy — SQL Server did not finish starting; check 'docker logs bf-sql' (a weak SQL_SA_PASSWORD makes it exit at boot)"
		fi
	elif [ -z "$SQLH" ]; then
		skip "SQL_HOST not set"
	elif timeout 8 bash -c "cat < /dev/null > /dev/tcp/$SQLH/$SQLP" 2>/dev/null; then
		ok "TCP connection to SQL Server at $SQLH:$SQLP succeeds"
	else
		bad "cannot open a TCP connection to SQL Server at $SQLH:$SQLP — the port is blocked, the host is wrong, or SQL is not listening on TCP"
	fi

	# The port being open says nothing about whether the login can actually work.
	# SQL Server is specific about *how* it refuses, and the API logs the message
	# verbatim, so the logs are a reliable read on login and permission problems
	# without needing a SQL client on this host (there is none: SQL is external).
	if has_container bf-api; then
		APILOG=$(d logs --tail 400 bf-api 2>&1)
		DBERR=0
		if echo "$APILOG" | grep -q "Login failed for user"; then
			bad "SQL rejected the login (error 18456) — SQL_USER/SQL_PASSWORD in .env are wrong, or the login does not exist on $SQLH"
			DBERR=1
		fi
		if echo "$APILOG" | grep -qi "Cannot open database"; then
			bad "the login authenticates but cannot open database '$SQLDB' (error 4060) — the login exists on the server but is not mapped to a user in that database"
			DBERR=1
		fi
		if echo "$APILOG" | grep -qiE "(SELECT|INSERT|UPDATE|DELETE|EXECUTE|CREATE TABLE|ALTER) permission was denied"; then
			DENIED=$(echo "$APILOG" | grep -oiE "The [A-Z ]+ permission was denied[^\"]*" | head -1)
			bad "the login is missing a permission: ${DENIED:-permission denied} — grant it db_datareader + db_datawriter, and db_ddladmin so migrations can run"
			DBERR=1
		fi
		if echo "$APILOG" | grep -qi "certificate chain was issued by an authority that is not trusted"; then
			bad "the API refuses SQL Server's TLS certificate — set SQL_TRUST_CERT=true in .env, or install the SQL certificate's CA on this host"
			DBERR=1
		fi
		if echo "$APILOG" | grep -qiE "There is already an object named|PendingModelChangesWarning|Migration.*(failed|error)"; then
			warn "the API logged a migration problem — the schema may be half-applied; check 'docker logs bf-api | grep -i migrat'"
			DBERR=1
		fi
		if [ "$DBERR" = 0 ]; then
			if echo "$APILOG" | grep -qiE "Applying migration|No migrations were applied|Migrations applied"; then
				ok "the API ran its migrations against $SQLDB without a permission error"
			else
				ok "no SQL login, permission or migration errors in the API's recent logs"
			fi
		fi
	else
		skip "bf-api container not present — cannot read the API's view of the database"
	fi

	# Ask the server directly when there is any way to. With the containerized-SQL
	# variant (docker-compose.sql.yml) bf-sql carries sqlcmd, so the rights can be
	# read outright instead of inferred; with external SQL nothing on this host has
	# a client and the log reading above is all there is.
	SQLCMD=""
	if has_container bf-sql; then
		# The tools moved to /opt/mssql-tools18 in SQL Server 2022 and need -C for
		# the container's self-signed certificate.
		for p in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
			if d exec bf-sql test -x "$p" 2>/dev/null; then SQLCMD="container:$p"; break; fi
		done
	fi
	[ -z "$SQLCMD" ] && command -v sqlcmd >/dev/null 2>&1 && SQLCMD="host"

	if [ -n "$SQLCMD" ]; then
		SQLU=$(envval SQL_USER); SQLPW=$(envval SQL_PASSWORD)
		PERMQ="SET NOCOUNT ON; SELECT CONCAT(
			ISNULL(IS_ROLEMEMBER('db_owner'),0),
			ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','SELECT'),0),
			ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','INSERT'),0),
			ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CREATE TABLE'),0));"
		# -C trusts the server certificate, matching SQL_TRUST_CERT's default.
		if [ "${SQLCMD#container:}" != "$SQLCMD" ]; then
			# Containerized SQL: connect as sa over the container's own loopback.
			# The password comes from the container's own environment, and only
			# falls back to the .env: run from anywhere but the deployment
			# directory there is no .env to read, and an empty password does not
			# fail — sqlcmd sits on an interactive "Password:" prompt until the
			# timeout kills it, which reported itself as "the user pressed CTRL+C".
			SQLU=sa
			SQLPW=$(container_env bf-sql MSSQL_SA_PASSWORD)
			[ -z "$SQLPW" ] && SQLPW=$(envval SQL_SA_PASSWORD)
			# $DOCKER, not the d() wrapper: timeout execs a binary and cannot run a
			# shell function, so `timeout 20 d exec ...` only ever produced
			# "timeout: failed to run command 'd': No such file or directory".
			if [ -z "$SQLPW" ]; then
				PERMS=nopassword
			else
				PERMS=$(timeout 20 $DOCKER exec -e SQLCMDPASSWORD="$SQLPW" bf-sql \
					"${SQLCMD#container:}" -S localhost -U sa -d "$SQLDB" \
					-C -h -1 -W -Q "$PERMQ" 2>&1 | tr -d '[:space:]')
			fi
		elif [ -z "$SQLPW" ]; then
			PERMS=nopassword
		else
			PERMS=$(SQLCMDPASSWORD="$SQLPW" timeout 20 sqlcmd -S "$SQLH,$SQLP" -U "$SQLU" -d "$SQLDB" \
				-C -h -1 -W -Q "$PERMQ" 2>&1 | tr -d '[:space:]')
		fi
		case "$PERMS" in
			nopassword) skip "no SQL password available — permissions not queried (run this from the deployment directory so the .env can be read)" ;;
			1*)    ok "SQL login is db_owner on $SQLDB (all required rights)" ;;
			0111)  ok "SQL login has SELECT, INSERT and CREATE TABLE on $SQLDB" ;;
			0110)  warn "SQL login can read and write $SQLDB but cannot create tables — the next deployment's migrations will fail" ;;
			0???|0) bad "SQL login is missing read/write rights on $SQLDB (select/insert/create = ${PERMS#0}) — grant db_datareader, db_datawriter and db_ddladmin" ;;
			*)     warn "could not read permissions from ${SQLH:-the bf-sql container} (sqlcmd said: $(echo "$PERMS" | head -c 120))" ;;
		esac
	else
		skip "no SQL client on this host (external database) — permissions inferred from the API's logs above, not queried directly"
	fi

	head1 "LiveKit (Server 2)"
	LKH=$(envval LIVEKIT_HOST); LKP=$(envval LIVEKIT_PORT); LKP=${LKP:-7880}
	LK_REACHABLE=0
	if [ -z "$LKH" ]; then
		skip "LIVEKIT_HOST not set"
	else
		if timeout 8 bash -c "cat < /dev/null > /dev/tcp/$LKH/$LKP" 2>/dev/null; then
			LK_REACHABLE=1
			ok "TCP connection to LiveKit at $LKH:$LKP succeeds"
			code=$(http_code "http://$LKH:$LKP/")
			[ "$code" = "200" ] && ok "LiveKit answers HTTP on $LKH:$LKP (signalling reachable)" \
				|| bad "LiveKit at $LKH:$LKP did not answer HTTP (got ${code:-no response}) — it is running but not serving"
		else
			bad "cannot reach LiveKit at $LKH:$LKP — open TCP $LKP from this server to the LiveKit server"
		fi
	fi
	PUB=$(envval LIVEKIT_PUBLIC_HOST)
	if [ -n "$PUB" ]; then
		echo "$PUB" | grep -q '^wss://' && ok "LIVEKIT_PUBLIC_HOST uses wss:// ($PUB)" \
			|| bad "LIVEKIT_PUBLIC_HOST is '$PUB' — browsers need wss:// for a call on an https:// page"
	fi

	head1 "Agent"
	code=$(http_code "http://127.0.0.1:30003/healthz")
	[ "$code" = "200" ] && ok "agent health endpoint answers on :30003" || bad "agent health endpoint returned HTTP ${code:-no response}"
	# 'registered worker' is a historical fact: it is read from the whole log, so
	# an agent that registered at boot and lost LiveKit hours later still matches.
	# Read on its own it grants false confidence — exactly the confidence that
	# argued a genuinely closed 7880 was a false alarm. Qualify it with whether
	# LiveKit is reachable right now.
	if d logs bf-agent 2>&1 | grep -q "registered worker"; then
		if [ "$LK_REACHABLE" = "1" ]; then
			ok "agent registered with LiveKit"
		else
			bad "the agent logged 'registered worker' at some point, but LiveKit is NOT reachable now — that line is historical, not current. The worker has dropped and no call can be answered until $LKH:$LKP is open again"
		fi
	else
		bad "agent has NOT logged 'registered worker' — it cannot reach LiveKit on :7880, or LIVEKIT_API_KEY/SECRET differ between this server and the LiveKit server"
	fi

	head1 "Internal services"
	if d exec bf-api getent hosts bf-azurite >/dev/null 2>&1; then
		ok "API can resolve other containers by name (Docker network healthy)"
	else
		bad "API cannot resolve 'bf-azurite' — the Docker network or its DNS is broken"
	fi
	if d exec bf-kb-scraper curl -sf --max-time 8 http://127.0.0.1:8080/health >/dev/null 2>&1; then
		ok "kb-scraper answers its own health check"
	else
		bad "kb-scraper health check fails — knowledge-base URL ingestion will not work"
	fi
fi

# ---------------------------------------------------------- LIVEKIT SERVER ---
if [ "$ROLE" = "livekit" ]; then
	check_containers bf-livekit bf-livekit-tls
	check_nginx_files nginx/default.conf nginx/site.inc

	head1 "Configuration"
	if [ -f "$ENV_FILE" ]; then
		val=$(envval LIVEKIT_API_KEY)
		[ -n "$val" ] && info "$(printf '%-22s' LIVEKIT_API_KEY)$val" || warn "LIVEKIT_API_KEY is empty in .env"
		val=$(envval LIVEKIT_API_SECRET)
		[ -n "$val" ] && info "$(printf '%-22s' LIVEKIT_API_SECRET)set (${#val} characters)" \
			|| bad "LIVEKIT_API_SECRET is empty — the application server cannot authenticate"
	fi

	LKYAML="${DEPLOY_DIR:-$PWD}/livekit.yaml"
	if [ -f "$LKYAML" ]; then
		if grep -qE '^\s*use_external_ip:\s*true' "$LKYAML"; then
			warn "livekit.yaml has use_external_ip: true — LiveKit discovers its public address by contacting a STUN server on the internet. On a restricted network this fails and LiveKit will not start. Prefer use_external_ip: false with node_ip set to this server's public address"
		fi
		NODEIP=$(sed -n 's/^\s*node_ip:\s*//p' "$LKYAML" | head -1)
		[ -n "$NODEIP" ] && ok "livekit.yaml pins node_ip = $NODEIP"
	fi

	head1 "LiveKit"
	if d logs bf-livekit 2>&1 | grep -q "could not validate RTC config"; then
		bad "LiveKit cannot validate its RTC config (it could not work out its own public IP). It will restart forever. Fix: in livekit.yaml set use_external_ip: false and node_ip to this server's public address, then restart"
	fi
	STARTED=$(d logs bf-livekit 2>&1 | grep "starting LiveKit server" | tail -1)
	if [ -n "$STARTED" ]; then
		ok "LiveKit started"
		ADV=$(echo "$STARTED" | sed -n 's/.*"nodeIP": *"\([^"]*\)".*/\1/p')
		if [ -n "$ADV" ]; then
			info "advertised address: $ADV"
			if is_private_ip "$ADV"; then
				bad "LiveKit is advertising the PRIVATE address $ADV — browsers on the internet cannot send audio there. Calls will connect and then be silent. Set node_ip to the public address"
			else
				ok "advertised address is public"
			fi
		fi
	else
		bad "LiveKit has not logged 'starting LiveKit server' — it is not running correctly"
	fi

	code=$(http_code "http://127.0.0.1:7880/")
	[ "$code" = "200" ] && ok "LiveKit answers on :7880 (signalling)" || bad "LiveKit on :7880 returned HTTP ${code:-no response}"

	head1 "Ports"
	check_listening 7880 tcp "signalling (from the application server)"
	check_listening 7881 tcp "ICE/TCP fallback (from browsers)"
	check_listening 80   tcp "HTTP (redirects to HTTPS)"
	check_listening 443 tcp "HTTPS signalling (from browsers)"

	UDPMAP=$(d port bf-livekit 2>/dev/null | grep -c udp)
	if [ "${UDPMAP:-0}" -gt 0 ]; then
		ok "UDP media ports published by Docker ($UDPMAP mappings)"
	else
		bad "no UDP ports published for bf-livekit — audio cannot flow. The 50000-50100/udp range must be published and open in the firewall"
	fi

	head1 "TLS proxy (nginx)"
	# nginx refuses to start without a readable certificate, and the container
	# then restart-loops with the reason only in its log.
	if d logs bf-livekit-tls 2>&1 | grep -q "cannot load certificate"; then
		CERTERR=$(d logs bf-livekit-tls 2>&1 | grep "cannot load certificate" | tail -1 | cut -c1-160)
		bad "nginx cannot load the certificate, so it will not start: $CERTERR — check TLS_CERT/TLS_KEY and that the files are inside the mounted certs folder"
	fi
	LKNAME=$(d exec bf-livekit-tls nginx -T 2>/dev/null | sed -n 's/^ *server_name *//p' | tr -d ' ;' | grep -v '^_$' | head -1)
	[ -n "$LKNAME" ] && info "server_name     $LKNAME (from nginx/site.inc)" \
		|| warn "nginx has no server_name — check that nginx/site.inc was copied"
	code=$(http_code -k "https://127.0.0.1/")
	[ "$code" = "200" ] && ok "signalling answers over HTTPS locally (HTTP $code)" || bad "HTTPS on :443 returned HTTP ${code:-no response}"
	check_tls bf-livekit-tls "$LKNAME"
fi

# ----------------------------------------------------------------- egress ---
head1 "Outbound internet"
if [ "$EGRESS" = "1" ]; then
	# DNS resolving is not reachability: on a network that blocks or intercepts
	# outbound 443, the name resolves fine and the TLS handshake is then reset —
	# which surfaced in production as KB jobs dying on "SSL connection could not
	# be established ... Connection reset by peer" while a resolve-only check
	# would have said everything was fine. So actually connect, and name the
	# provider and the failure mode outright.
	#
	# The KB scraper's own source URLs are configured per knowledge base and
	# cannot be tested generically; if these fail, those fail too.
	EGRESS_TARGETS="Gemini-(chat-and-KB-indexing)=generativelanguage.googleapis.com \
ElevenLabs-(voice-synthesis)=api.elevenlabs.io \
image-registry-(updates)=crvozimages-h3a3g5dzbkamfpd8.azurecr.io"

	# Prefer testing from inside a container: that exercises Docker's DNS and the
	# same network path the services use. kb-scraper carries curl; fall back to
	# the host, whose egress verdict is the same NAT either way.
	EGRESS_EXEC=""
	if has_container bf-kb-scraper && d exec bf-kb-scraper curl --version >/dev/null 2>&1; then
		EGRESS_EXEC="container bf-kb-scraper"
	fi
	[ -n "$EGRESS_EXEC" ] && info "testing from inside $EGRESS_EXEC (same network path as the services)" \
		|| info "testing from the host (no container with curl available)"

	for pair in $EGRESS_TARGETS; do
		label=${pair%%=*}; label=$(printf '%s' "$label" | tr '-' ' ')
		target=${pair#*=}
		if [ "${EGRESS_EXEC#container }" != "$EGRESS_EXEC" ]; then
			d exec "${EGRESS_EXEC#container }" curl -s -o /dev/null --connect-timeout 8 --max-time 15 "https://$target/" >/dev/null 2>&1
			rcode=$?
		else
			curl -s -o /dev/null --connect-timeout 8 --max-time 15 "https://$target/" >/dev/null 2>&1
			rcode=$?
		fi
		# curl's exit code says exactly where it died, so the report can too.
		case "$rcode" in
			0|22) ok "can connect to $label — $target answers over TLS" ;;
			6)  bad "CANNOT CONNECT to $label — $target does not resolve. DNS is broken or filtered on this network" ;;
			7)  bad "CANNOT CONNECT to $label — connection to $target refused. Something on the path actively rejects outbound 443" ;;
			28) bad "CANNOT CONNECT to $label — connection to $target times out. Outbound 443 is silently dropped by a firewall; ask the network team to open it from this server" ;;
			35|56) bad "CANNOT CONNECT to $label — the TLS handshake to $target was reset by the network. A firewall or proxy is intercepting outbound 443; ask the network team whether egress requires a proxy (then HTTPS_PROXY must be configured) or an allowlist entry" ;;
			*)  bad "CANNOT CONNECT to $label — $target unreachable (curl exit $rcode)" ;;
		esac
	done
else
	skip "not tested (--no-egress given)"
fi

# ---------------------------------------------------------- log extracts ---
head1 "Recent errors from container logs"
LOGGED=0
for c in bf-api bf-agent bf-frontend bf-kb-scraper bf-livekit bf-livekit-tls; do
	has_container "$c" || continue
	# Excluded patterns are normal call churn: a browser closing a call always
	# tears the data channels down this way, and reporting it as an error sends
	# people chasing a problem that is not there.
	LINES=$(d logs --tail 400 "$c" 2>&1 \
		| grep -iE 'error|exception|fatal|fail|denied|refused|unauthor|timeout|cannot|could not' \
		| grep -viE 'error_code": *null|failed=0' \
		| grep -viE 'error reading data channel|dtls timeout|agent worker left the room|closing idle room|participant closed|context canceled' \
		| grep -viE 'JRP_ON_FAILURE|"level": ?"INFO"|	INFO	' \
		| grep -viE 'Executed DbCommand|CommandType=|^ *(INSERT INTO|SELECT |UPDATE |DELETE |VALUES )|ErrorMessage\]|\[ErrorMessage' \
		| grep -viE '^ *\[?(Error|Timeout)[A-Za-z]*\]? +(int|nvarchar|varchar|bit|datetime2|uniqueidentifier)|Applying migration|Shutdown timeout:' \
		| grep -viE 'SSL_do_handshake\(\) failed|invalid authorization header|"path": ?"/(manager|admin|phpmyadmin|cgi-bin)' \
		| tail -12)
	if [ -n "$LINES" ]; then
		LOGGED=1
		printf '\n--- %s ---\n' "$c"
		printf '%s\n' "$LINES" | cut -c1-300
	fi
done
[ "$LOGGED" = "0" ] && info "no error-like lines in the last 400 lines of any container log"

fi  # end SERVER_MODE

# ============================================================= OUTSIDE IN ====
if [ "$OUTSIDE_MODE" = "1" ]; then

# Seconds of clock skew, from the server's own Date header. This is the one
# thing an outside check does BETTER than the server-side run: `timedatectl`
# reports whether NTP is enabled, not whether the clock is actually right, and
# LiveKit tokens fail on the latter.
epoch_of_httpdate() {
	# GNU date and BSD date disagree on everything here, so try both.
	date -u -d "$1" +%s 2>/dev/null && return 0
	date -ju -f "%a, %d %b %Y %T %Z" "$1" +%s 2>/dev/null && return 0
	return 1
}

check_clock() {
	local host=$1 hdr remote local_now skew
	hdr=$(curl -sSI --max-time 15 "https://$host/" 2>/dev/null | tr -d '\r' | sed -n 's/^[Dd]ate: //p' | head -1)
	if [ -z "$hdr" ]; then
		skip "no Date header from $host — clock not checked"
		return
	fi
	local_now=$(date -u +%s)
	remote=$(epoch_of_httpdate "$hdr") || { skip "could not parse the Date header ($hdr)"; return; }
	skew=$((remote - local_now))
	[ "$skew" -lt 0 ] && skew=$((-skew))
	if [ "$skew" -le 5 ]; then
		ok "clock is within ${skew}s of this machine"
	elif [ "$skew" -le 60 ]; then
		warn "clock is ${skew}s off this machine — LiveKit tokens carry expiry times and drift eventually breaks call authorisation; enable NTP"
	else
		bad "clock is ${skew}s off this machine — LiveKit tokens will be rejected as expired or not-yet-valid. Run: sudo timedatectl set-ntp true"
	fi
}

# TLS as a browser sees it: negotiated live, not read off disk.
check_tls_live() {
	local host=$1 s names end
	s=$(echo | openssl s_client -connect "$host:443" -servername "$host" 2>/dev/null)
	if [ -z "$s" ] || ! printf '%s' "$s" | grep -q 'BEGIN CERTIFICATE'; then
		bad "no TLS handshake with $host:443 — nothing is terminating TLS there"
		return
	fi

	if printf '%s' "$s" | grep -q 'Verify return code: 0 (ok)'; then
		ok "certificate chain validates against this machine's trust store"
	else
		local why
		why=$(printf '%s' "$s" | sed -n 's/.*Verify return code: //p' | head -1)
		bad "certificate does NOT validate (${why:-unknown}) — browsers will show a warning and the microphone will be refused. Usually a missing intermediate: serve the FULL CHAIN file"
	fi

	local proto
	proto=$(printf '%s' "$s" | sed -n 's/^ *Protocol *: *//p' | head -1)
	[ -n "$proto" ] && info "negotiated      $proto"
	printf '%s' "$s" | sed -n 's/^subject=//p' | head -1 | while read -r l; do info "subject         $l"; done
	printf '%s' "$s" | sed -n 's/^issuer=//p'  | head -1 | while read -r l; do info "issuer          $l"; done

	names=$(printf '%s' "$s" | openssl x509 -noout -text 2>/dev/null \
		| grep -A1 'Subject Alternative Name' | tail -1 | tr -d ' ' | tr ',' '\n' | sed 's/^DNS://')
	if [ -n "$names" ]; then
		if cert_covers "$host" "$names"; then
			ok "certificate covers $host"
		else
			bad "certificate does NOT cover $host (covers: $(printf '%s' "$names" | tr '\n' ' '))"
		fi
	fi

	end=$(printf '%s' "$s" | openssl x509 -noout -enddate 2>/dev/null | cut -d= -f2)
	if printf '%s' "$s" | openssl x509 -noout -checkend 1209600 >/dev/null 2>&1; then
		ok "certificate valid (expires $end)"
	elif printf '%s' "$s" | openssl x509 -noout -checkend 0 >/dev/null 2>&1; then
		warn "certificate expires within 14 days ($end) — replace it and reload nginx"
	else
		bad "certificate EXPIRED ($end)"
	fi
}

# Resolve, and say plainly when this run is not actually coming from outside.
# A perimeter that does not hairpin lets a host check its own public address
# successfully while the internet cannot reach it at all, and a green run from
# the server itself is exactly the false confidence this section exists to end.
check_dns() {
	local host=$1 ip
	ip=$(resolve_host "$host")
	if [ -z "$ip" ]; then
		bad "$host does not resolve — no A record, so nothing else about this host can be tested"
		return 1
	fi
	ok "$host resolves to $ip"
	if host_owns_ip "$ip"; then
		warn "$ip is an address of THIS machine — these checks are not really coming from outside, and a firewall that blocks the internet would still let them through. Re-run this from a laptop off the server's network"
	elif is_private_ip "$ip"; then
		warn "$ip is a private address — this run is inside the same network, so it cannot tell you what a browser on the internet sees"
	fi
	return 0
}

# A port that must NOT be reachable. Reported as a finding when it is, because
# these carry no authentication and are meant for the compose network only.
check_closed() {
	local host=$1 port=$2 what=$3
	if tcp_open "$host" "$port"; then
		bad "$port/tcp ($what) is reachable from the internet — it should not be. Bind it to 127.0.0.1 in the compose file or block it at the perimeter (ufw will not do it: Docker's iptables rules run first)"
	else
		ok "$port/tcp ($what) is not reachable from the internet, as intended"
	fi
}

# ------------------------------------------------------------------ LIVEKIT ---
if [ -n "$LIVEKIT_HOST" ]; then
	head1 "LiveKit host ($LIVEKIT_HOST)"
	if check_dns "$LIVEKIT_HOST"; then

		for p in 80 443; do
			tcp_open "$LIVEKIT_HOST" "$p" && ok "$p/tcp reachable" || bad "$p/tcp is NOT reachable — browsers cannot open the signalling socket"
		done

		# The fallback browsers need when their network blocks UDP, which is common
		# on ministry and corporate networks. Its absence is invisible until a call
		# connects and carries no audio.
		if tcp_open "$LIVEKIT_HOST" 7881; then
			ok "7881/tcp (ICE/TCP fallback) reachable — clients behind UDP-blocking firewalls can still get media"
		else
			bad "7881/tcp (ICE/TCP fallback) is NOT reachable — any client whose network blocks UDP will join the room and then get no audio. Open it inbound"
		fi

		check_closed "$LIVEKIT_HOST" 7880 "plain signalling, meant for the app server only"

		head1 "LiveKit TLS ($LIVEKIT_HOST)"
		check_tls_live "$LIVEKIT_HOST"

		head1 "LiveKit service ($LIVEKIT_HOST)"
		code=$(http_code "http://$LIVEKIT_HOST/")
		case "$code" in
			301|302) ok "plain HTTP redirects to HTTPS (HTTP $code)" ;;
			*)       warn "plain HTTP returned HTTP ${code:-no response}, expected a redirect" ;;
		esac

		BODY=$(curl -s --max-time 15 "https://$LIVEKIT_HOST/" 2>/dev/null)
		if [ "$BODY" = "OK" ]; then
			ok "LiveKit answers its health check through the proxy (body: OK)"
		elif printf '%s' "$BODY" | grep -qi '<!doctype html\|<html'; then
			# A combined deployment serves the app at / on this same domain and
			# routes only /rtc and /twirp to LiveKit. That is a supported layout,
			# not a fault: the signalling check below is the real health signal,
			# so demanding LiveKit's "OK" at / would fail a working install.
			info "/ serves the web app, not LiveKit's health response — this host proxies both (signalling is checked next)"
		else
			# Never echo the body raw: an HTML page here buried the summary in a
			# full document. One line, truncated.
			bad "https://$LIVEKIT_HOST/ returned '$(printf '%s' "${BODY:-nothing}" | tr '\n' ' ' | cut -c1-80)', expected 'OK' — nginx is up but not reaching LiveKit"
		fi

		# 401 is the CORRECT answer here: it proves the request reached LiveKit
		# itself rather than stopping at nginx. A 502 means the proxy cannot see
		# the container; a 404 means the location block is wrong.
		code=$(http_code "https://$LIVEKIT_HOST/rtc/validate")
		case "$code" in
			401) ok "signalling endpoint reached LiveKit (HTTP 401 for a tokenless request, as expected)" ;;
			502|503|504) bad "/rtc/validate returned HTTP $code — nginx cannot reach the LiveKit container" ;;
			*)   warn "/rtc/validate returned HTTP ${code:-no response}, expected 401" ;;
		esac

		head1 "LiveKit clock ($LIVEKIT_HOST)"
		check_clock "$LIVEKIT_HOST"
	fi
fi

# ---------------------------------------------------------------------- APP ---
if [ -n "$APP_HOST" ]; then
	head1 "App host ($APP_HOST)"
	if check_dns "$APP_HOST"; then

		HTTPS_UP=0
		if tcp_open "$APP_HOST" 443; then
			ok "443/tcp reachable"; HTTPS_UP=1
		else
			bad "443/tcp is NOT reachable — without HTTPS the browser refuses microphone access (getUserMedia only works in a secure context), so no call can start"
		fi
		tcp_open "$APP_HOST" 80 && ok "80/tcp reachable" || warn "80/tcp is not reachable — visitors typing the bare hostname get nothing"

		# Published by docker-compose.stack.yml on 0.0.0.0. Docker's iptables rules
		# are evaluated before ufw, so a ufw deny does not close them.
		check_closed "$APP_HOST" 30000 "plain-HTTP debug copy of the SPA"
		check_closed "$APP_HOST" 30001 "API, unauthenticated and direct"
		check_closed "$APP_HOST" 30003 "agent health endpoint"
		check_closed "$APP_HOST" 31433 "SQL Server (sa)"

		if [ "$HTTPS_UP" = "1" ]; then
			head1 "App TLS ($APP_HOST)"
			check_tls_live "$APP_HOST"

			head1 "App service ($APP_HOST)"
			code=$(http_code "http://$APP_HOST/")
			case "$code" in
				301|302) ok "plain HTTP redirects to HTTPS (HTTP $code)" ;;
				200)     bad "plain HTTP serves the site instead of redirecting — anyone landing on http:// gets a page where the microphone will not work" ;;
				*)       warn "plain HTTP returned HTTP ${code:-no response}" ;;
			esac

			# Check the content type too, not just the status. Pointing --app at the
			# wrong host is an easy mistake, and LiveKit's plain-text "OK" health
			# response is also a 200 — without this the run would call that a
			# served SPA and every later failure would read as a puzzle.
			# A redirect loop answers 301 here, and "the SPA returned HTTP 301"
			# describes the symptom without naming the cause. Follow the chain
			# first: curl exits 47 when it gives up, which is the loop itself.
			curl -sS -o /dev/null -L --max-redirs 5 --max-time 20 "https://$APP_HOST/" 2>/dev/null
			LOOP_RC=$?
			if [ "$LOOP_RC" = "47" ]; then
				bad "https://$APP_HOST/ redirects to itself endlessly — this is what a browser shows as ERR_TOO_MANY_REDIRECTS, and the site is unusable. It happens when the proxy holding this name forwards to the app server over plain HTTP and the app server answers 'go to https://': point that proxy at the app server's :443, or have it send 'X-Forwarded-Proto: https' and update the app server's nginx/default.conf from the deployment bundle"
			fi

			read -r code CTYPE <<<"$(http_probe "https://$APP_HOST/")"
			if [ "$code" = "200" ] && printf '%s' "$CTYPE" | grep -qi 'text/html'; then
				ok "SPA served over HTTPS (HTTP $code)"
			elif [ "$code" = "200" ]; then
				bad "https://$APP_HOST/ answers 200 but serves '${CTYPE:-no content type}', not text/html — this does not look like the app server"
			else
				bad "the SPA returned HTTP ${code:-no response} over HTTPS"
			fi

			# Status alone is not enough for these two either. A single-page app
			# whose server answers unknown paths with index.html returns 200 and
			# text/html for a missing env.js and for an /api proxy that was never
			# configured — both of which then read as working while the SPA is in
			# fact getting a web page where it expects config and JSON.
			read -r code CTYPE <<<"$(http_probe "https://$APP_HOST/env.js")"
			if [ "$code" = "200" ] && printf '%s' "$CTYPE" | grep -qi 'html'; then
				bad "https://$APP_HOST/env.js answers 200 but serves HTML — that is the SPA fallback, not the runtime config. The file is missing and the app will not know where its API is"
			elif [ "$code" = "200" ]; then
				ok "env.js served (runtime config)"
			else
				bad "env.js returned HTTP ${code:-no response} — the SPA cannot find its API"
			fi

			read -r code CTYPE <<<"$(http_probe "https://$APP_HOST/api/auth/providers")"
			if [ "$code" = "200" ] && printf '%s' "$CTYPE" | grep -qi 'html'; then
				bad "/api/auth/providers answers 200 but serves HTML — nginx is falling back to index.html instead of proxying to the API. The /api location block is missing or not matching"
			elif [ "$code" = "200" ]; then
				ok "API reachable through the /api proxy (HTTP $code)"
			else
				bad "/api through nginx returned HTTP ${code:-no response} — the SPA cannot reach the backend"
			fi

			# The API builds absolute URLs from the request scheme. Behind a TLS
			# proxy that does not forward X-Forwarded-Proto it hands back http://
			# links on an https:// page, costing a redirect and a "Not secure" flash.
			LOGIN=$(curl -s --max-time 15 "https://$APP_HOST/api/auth/login?provider=fake" 2>/dev/null)
			if [ -z "$LOGIN" ] || ! printf '%s' "$LOGIN" | grep -qE 'https?://'; then
				# No URL in the body means nothing to judge. Passing here because
				# "http://" happens to be absent would turn a 404 into a green check.
				skip "the login endpoint returned no URL — X-Forwarded-Proto not checked"
			elif printf '%s' "$LOGIN" | grep -q 'http://'; then
				bad "the API returned an http:// URL on an HTTPS site — the TLS proxy is not sending X-Forwarded-Proto. Add: proxy_set_header X-Forwarded-Proto \$scheme;"
			else
				ok "the API returns https:// URLs (X-Forwarded-Proto is reaching it)"
			fi

			# Cross-server consistency. Neither host can check this alone: only a
			# client sees both the config the SPA is handed and whether the LiveKit
			# host it names actually answers.
			ENVJS=$(curl -s --max-time 15 "https://$APP_HOST/env.js" 2>/dev/null)
			LKURL=$(printf '%s' "$ENVJS" | sed -n "s/.*wss:\/\/\([A-Za-z0-9._-]*\).*/\1/p" | head -1)
			if [ -n "$LKURL" ]; then
				info "SPA is configured for LiveKit at wss://$LKURL"
				if [ -n "$LIVEKIT_HOST" ] && [ "$LKURL" != "$LIVEKIT_HOST" ]; then
					warn "env.js points at '$LKURL' but this run checked '$LIVEKIT_HOST' — make sure the one the SPA names is the one that works"
				fi
				if [ "$(curl -s --max-time 15 "https://$LKURL/" 2>/dev/null)" = "OK" ]; then
					ok "the LiveKit host the SPA names answers over HTTPS"
				else
					bad "the SPA points at wss://$LKURL, which does not answer — every call will fail to connect"
				fi
			elif printf '%s' "$ENVJS" | grep -q 'ws://'; then
				bad "env.js configures LiveKit over ws:// — browsers block plaintext WebSockets on an HTTPS page. It must be wss://"
			fi

			head1 "App clock ($APP_HOST)"
			check_clock "$APP_HOST"
		fi
	fi
fi

head1 "What this cannot tell you"
info "Media actually flowing. Port reachability is necessary but not sufficient:"
info "LiveKit allocates UDP 50000-50100 per call, so an idle server has nothing"
info "bound there and scanning it proves little. Confirm with a real call and"
info "chrome://webrtc-internals — the selected candidate pair names the path that"
info "won. udp means the media range works; tcp on 7881 means UDP is blocked"
info "somewhere and the fallback is carrying the call."
if [ "$SERVER_MODE" = "0" ]; then
	info ""
	info "Container health, .env values, migrations and the database need this same"
	info "script run with no arguments, on the servers themselves."
fi

fi  # end OUTSIDE_MODE

# ----------------------------------------------------------------- summary ---
head1 "Summary"
printf '  passed   %d\n  failed   %d\n  warnings %d\n  skipped  %d\n' "$PASS" "$FAIL" "$WARN" "$SKIP"

if [ "$FAIL" -gt 0 ]; then
	printf '\n  NOT working:\n'
	for f in "${FAILURES[@]}"; do printf '   ✗ %s\n' "$f"; done
fi
if [ "$WARN" -gt 0 ]; then
	printf '\n  Warnings:\n'
	for w in "${WARNINGS[@]}"; do printf '   ! %s\n' "$w"; done
fi
if [ "$PASS" -gt 0 ]; then
	# The working list repeats only the claim, not the explanation: each ok line
	# reads "<claim> — <why it matters>", and in a summary the second half is
	# noise. Failures above keep their full text because there the second half
	# is the fix.
	printf '\n  Working:\n'
	for o in "${OKS[@]}"; do printf '   ✓ %s\n' "${o%% — *}"; done
fi

printf '\n'
if [ "$FAIL" -gt 0 ]; then
	printf '  Result: %d check(s) failed.\n' "$FAIL"
	if [ "$SERVER_MODE" = "1" ] && [ "$OUTSIDE_MODE" = "0" ]; then
		printf '  Send this whole output to support. Run the same script on the other\n'
		printf '  server too, and once from a laptop with --app/--livekit — some problems\n'
		printf '  only show up from the other side.\n'
	fi
	exit 1
fi
if [ "$OUTSIDE_MODE" = "1" ] && [ "$SERVER_MODE" = "0" ]; then
	printf '  Result: the public surface looks correct from here.\n'
	printf '  Container health, .env and the database still need a run on the servers.\n'
	exit 0
fi
printf '  Result: everything checked is working.\n'
printf '  A call also needs the other server healthy, and UDP 50000-50100 plus\n'
printf '  TCP 7881 open from the internet to the LiveKit server. Confirm that with\n'
printf '  a run from a laptop: bash diagnose.sh --app <host> --livekit <host>\n'
exit 0
