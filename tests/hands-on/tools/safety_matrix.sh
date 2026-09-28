#!/usr/bin/env bash
# Test 10: guardrails + CI mode. Runs each case, compares exit code (and a check) with the documented behaviour.
# Usage (from tests/hands-on): tools/safety_matrix.sh sites/source   (needs sites/source/work/ids.json from tools/build_site.py)
set -uo pipefail
SITE="${1:?site dir}"
UMB="$SITE/umb"
CREDS="$SITE/credentials.json"
HOST=$(jq -r .host "$CREDS"); CID=$(jq -r .apiUser.clientId "$CREDS"); SEC=$(jq -r .apiUser.clientSecret "$CREDS")
HARNESS="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
POST=$(jq -r '.posts[1]' "$SITE/work/ids.json")
EMPTY_CFG=$(mktemp -d)/empty-config.json
pass=0; fail=0

excerpt() { "$UMB" content get "$POST" -o json | jq -r '.data.values[] | select(.alias=="excerpt" and .culture=="en-US") | .value'; }
BEFORE=$(excerpt)
WRITE='{"values":[{"alias":"excerpt","culture":"en-US","segment":null,"value":"GUARDRAIL BREACH - this write should have been blocked"}]}'

# case <label> <expected-exit> <command...>   (env vars can be passed via `env A=B`)
case_() {
  local label=$1 want=$2; shift 2
  local out; out=$("$@" 2>&1); local got=$?
  local msg; msg=$(printf '%s' "$out" | jq -rs '.[0] | (.message // .status // "") | tostring | .[0:90]' 2>/dev/null || printf '%s' "$out" | head -c 90)
  if [[ "$got" == "$want" ]]; then pass=$((pass+1)); printf 'PASS  exit %-3s %-58s %s\n' "$got" "$label" "$msg"
  else fail=$((fail+1)); printf 'FAIL  exit %-3s (want %s) %-48s %s\n' "$got" "$want" "$label" "$msg"; fi
}

echo "== read-only"
case_ "--readonly blocks content update"            2 bash -c "echo '$WRITE' | '$UMB' content update $POST --json-body - --readonly -o json"
case_ "UMBRACO_READONLY=1 blocks content update"    2 bash -c "echo '$WRITE' | UMBRACO_READONLY=1 '$UMB' content update $POST --json-body - -o json"
case_ "UMBRACO_READONLY=1 blocks publish"           2 env UMBRACO_READONLY=1 "$UMB" content publish "$POST" -o json
case_ "UMBRACO_READONLY=1 blocks health run (POST)" 2 env UMBRACO_READONLY=1 "$UMB" health run Security -o json
case_ "UMBRACO_READONLY=1 allows reads"             0 env UMBRACO_READONLY=1 "$UMB" content get "$POST" -o json
case_ "UMBRACO_READONLY=1 allows --dry-run write"   0 bash -c "echo '$WRITE' | UMBRACO_READONLY=1 '$UMB' content update $POST --json-body - --dry-run -o json"
[[ "$(excerpt)" == "$BEFORE" ]] && { pass=$((pass+1)); echo "PASS  excerpt unchanged after read-only cases"; } || { fail=$((fail+1)); echo "FAIL  excerpt CHANGED under read-only"; }

echo "== allow-list"
case_ "ALLOWED=content: content list runs"           0 env UMBRACO_ALLOWED_COMMANDS=content "$UMB" content list -o json
case_ "ALLOWED=content: media list blocked"          2 env UMBRACO_ALLOWED_COMMANDS=content "$UMB" media list -o json
case_ "ALLOWED=content.list: content get blocked"    2 env UMBRACO_ALLOWED_COMMANDS=content.list "$UMB" content get "$POST" -o json
case_ "ALLOWED=content.list: content list runs"      0 env UMBRACO_ALLOWED_COMMANDS=content.list "$UMB" content list -o json
case_ "ALLOWED='' (lockdown): content list blocked"  2 env UMBRACO_ALLOWED_COMMANDS= "$UMB" content list -o json
case_ "ALLOWED=',' (lockdown): server info blocked"  2 env UMBRACO_ALLOWED_COMMANDS=, "$UMB" server info -o json
case_ "ALLOWED='' (lockdown): auth whoami allowed"   0 env UMBRACO_ALLOWED_COMMANDS= "$UMB" auth whoami -o json
case_ "commands (discovery) never blocked"           0 env UMBRACO_ALLOWED_COMMANDS= UMBRACO_READONLY=1 "$UMB" commands
case_ "ALLOWED=media + --readonly: media upload blocked" 2 env UMBRACO_ALLOWED_COMMANDS=media "$UMB" media upload "$HARNESS/fixtures/images/blog-1.jpg" --readonly -o json

echo "== CI mode (env-only credentials, no saved profile)"
case_ "env creds + empty --config: content list"     0 env -u UMBRACO_PROFILE UMBRACO_HOST="$HOST" UMBRACO_CLIENT_ID="$CID" UMBRACO_CLIENT_SECRET="$SEC" "$UMB" content list --config "$EMPTY_CFG" -o json
case_ "no creds + empty --config: aborts (no host)"  2 env -u UMBRACO_PROFILE -u UMBRACO_HOST "$UMB" content list --config "$EMPTY_CFG" -o json
case_ "bad secret: auth fails"                       2 env -u UMBRACO_PROFILE UMBRACO_HOST="$HOST" UMBRACO_CLIENT_ID="$CID" UMBRACO_CLIENT_SECRET=wrong "$UMB" content list --config "$EMPTY_CFG" -o json

echo "== exit codes"
case_ "success = 0"                                  0 "$UMB" server status -o json
case_ "API 404 = 1"                                  1 "$UMB" content get 00000000-0000-0000-0000-000000000001 -o json
case_ "parse error = 1"                              1 "$UMB" content get not-a-guid -o json
case_ "destructive without --yes = 2"                2 "$UMB" content delete "$POST" -o json

echo
echo "$pass passed, $fail failed"
[[ "$(excerpt)" == "$BEFORE" ]] || echo "WARNING: test post excerpt changed - restore it"
