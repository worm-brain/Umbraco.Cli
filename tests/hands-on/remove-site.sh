#!/usr/bin/env bash
#
# remove-site.sh <name> - stop a test site, remove its CLI profile and delete its folder.
# remove-site.sh --list  - list test sites and whether they're running.

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ "${1:-}" == "--list" || -z "${1:-}" ]]; then
  shopt -s nullglob
  for env in "$ROOT"/sites/*/site.env; do
    ( source "$env"
      dir="$(dirname "$env")"
      state="stopped"
      [[ -f "$dir/site.pid" ]] && kill -0 "$(cat "$dir/site.pid")" 2>/dev/null && state="running"
      printf '%-50s umbraco %-12s cli %-16s %-26s %s\n' "$SITE_NAME" "$UMBRACO_VERSION" "$CLI_VERSION" "$HOST" "$state" )
  done
  [[ -z "${1:-}" ]] && echo && echo "Usage: ./remove-site.sh <name>"
  exit 0
fi

SITE_DIR="$ROOT/sites/$1"
[[ -f "$SITE_DIR/site.env" ]] || { echo "No site at sites/$1" >&2; exit 1; }

"$SITE_DIR/stop.sh"
"$SITE_DIR/umb" auth logout --profile "$1" --output json >/dev/null 2>&1 \
  && echo "Removed CLI profile '$1'" \
  || echo "CLI profile '$1' not removed (may not exist)"
rm -rf "$SITE_DIR"
echo "Deleted sites/$1"
