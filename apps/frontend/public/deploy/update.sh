#!/usr/bin/env bash
# Bot Factory — update.sh has been absorbed into diagnose.sh.
#
# The update flow is now:  bash diagnose.sh --update [--dry-run] [--external]
# It pulls the latest images, restarts what changed, and then runs the FULL
# diagnosis against the post-update state — strictly more verification than
# this script used to do.
#
# This shim keeps old runbooks working: it runs diagnose.sh from its own
# directory when present, and otherwise says what to run instead.

if [ -z "${BASH_VERSION:-}" ]; then
	if [ -f "$0" ]; then exec bash "$0" "$@"; fi
	echo "this script needs bash: pipe it to 'bash', not 'sh'" >&2
	exit 2
fi

HERE=$(cd "$(dirname "$0")" 2>/dev/null && pwd)
if [ -n "$HERE" ] && [ -f "$HERE/diagnose.sh" ]; then
	echo "update.sh is now part of diagnose.sh — running: diagnose.sh --update $*"
	exec bash "$HERE/diagnose.sh" --update "$@"
fi

echo "update.sh has been absorbed into diagnose.sh. Run instead:"
echo
echo "  bash diagnose.sh --update"
echo
echo "or, piped:"
echo
echo "  curl -fsSL https://<script-host>/deploy/diagnose.sh | bash -s -- --update"
exit 2
