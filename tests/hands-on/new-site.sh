#!/usr/bin/env bash
#
# new-site.sh - spin up a fresh Umbraco test site wired to Umbraco.Community.Cli.
#
#   1. Scaffolds an Umbraco project (SQLite, unattended install, admin user)
#   2. Installs Umbraco.Community.Cli as a local tool for the site
#   3. Starts the site and waits for the unattended install to finish
#   4. Signs in as the admin, creates an API user with client credentials
#   5. Runs `umbraco auth login` into a profile named after the site and checks `auth doctor`
#
# Every CLI call goes through the site's `umb` wrapper, which uses the harness's own CLI config
# (.cli/config.json next to this script), so the user's real `umbraco` profiles are never touched.
#
# Run ./new-site.sh --help for options.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_CONFIG="$ROOT/.cli/config.json"   # harness-local CLI config (profiles for test sites only)

# ---- defaults ---------------------------------------------------------------
UMBRACO_VERSION="17"          # major (latest stable in that major), exact version, or "latest"
CLI_VERSION="latest"          # exact version or "latest" (includes prereleases)
CLI_SOURCE=""                 # optional folder containing a locally built .nupkg
SITE_NAME=""
PORT=""
ADMIN_NAME="Administrator"
ADMIN_EMAIL="admin@example.com"
ADMIN_PASSWORD="Password1234!"
API_USER_EMAIL="cli@example.com"
API_CLIENT_ID="umbraco-back-office-cli"
SET_DEFAULT_PROFILE=1
STARTUP_TIMEOUT=300

usage() {
  cat <<EOF
Usage: ./new-site.sh [options]

Creates sites/<name>/ with a running Umbraco site and an authenticated CLI.

Options:
  -u, --umbraco <ver>     Umbraco version: major ("17"), exact ("17.7.0", "18.1.0-rc") or "latest".
                          Default: $UMBRACO_VERSION (latest stable 17.x)
  -c, --cli <ver>         Umbraco.Community.Cli version, exact or "latest". Default: $CLI_VERSION
      --cli-source <dir>  Install the CLI from a local folder of .nupkg files (e.g. a local build).
                          Use with --cli <ver> to pick the version in that folder.
  -n, --name <name>       Site folder / CLI profile name. Default: u<umbraco>-cli<cli>-<timestamp>
  -p, --port <port>       HTTPS port. Default: first free port from 44800
      --admin-email <e>   Default: $ADMIN_EMAIL
      --admin-password <p>
                          Default: $ADMIN_PASSWORD
      --no-default        Don't make this site's profile the default in the harness CLI config.
  -h, --help              Show this help.

Examples:
  ./new-site.sh
  ./new-site.sh --umbraco 17.6.0 --cli 0.1.0-alpha.5
  ./new-site.sh --umbraco 18 --name v18-smoke
  ./new-site.sh --cli-source nupkg --cli "$(./pack-cli.sh | tail -1)"   # this repository's CLI
EOF
}

# ---- helpers ----------------------------------------------------------------
say()  { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mwarn:\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 1; }

need() { command -v "$1" >/dev/null 2>&1 || die "'$1' is required but not installed."; }

# Latest version from a NuGet flat-container index, optionally filtered by a regex.
nuget_versions() {
  curl -fsSL "https://api.nuget.org/v3-flatcontainer/$1/index.json" | jq -r '.versions[]'
}

resolve_umbraco_version() {
  local want="$1"
  case "$want" in
    latest) nuget_versions umbraco.templates | grep -E '^[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -1 ;;
    *.*)    nuget_versions umbraco.templates | grep -qxF "$want" && echo "$want" ;;
    *)      nuget_versions umbraco.templates | grep -E "^${want}\.[0-9]+\.[0-9]+$" | sort -V | tail -1 ;;
  esac
}

resolve_cli_version() {
  local want="$1"
  if [[ -n "$CLI_SOURCE" ]]; then
    if [[ "$want" == "latest" ]]; then
      # Pick the newest nupkg in the folder.
      ls "$CLI_SOURCE"/[Uu]mbraco.[Cc]ommunity.[Cc]li.*.nupkg 2>/dev/null \
        | sed -E 's/.*[Cc]li\.(.*)\.nupkg$/\1/' | sort -V | tail -1
    else
      echo "$want"
    fi
    return
  fi
  case "$want" in
    latest) nuget_versions umbraco.community.cli | sort -V | tail -1 ;;
    *)      nuget_versions umbraco.community.cli | grep -qxF "$want" && echo "$want" ;;
  esac
}

port_in_use() { lsof -nP -iTCP:"$1" -sTCP:LISTEN >/dev/null 2>&1; }

free_port() {
  local p=44800
  while port_in_use "$p" || grep -rqs "^PORT=$p\$" "$ROOT"/sites/*/site.env; do p=$((p + 1)); done
  echo "$p"
}

# ---- args -------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    -u|--umbraco)       UMBRACO_VERSION="$2"; shift 2 ;;
    -c|--cli)           CLI_VERSION="$2"; shift 2 ;;
    --cli-source)       CLI_SOURCE="$(cd "$2" && pwd)"; shift 2 ;;
    -n|--name)          SITE_NAME="$2"; shift 2 ;;
    -p|--port)          PORT="$2"; shift 2 ;;
    --admin-email)      ADMIN_EMAIL="$2"; shift 2 ;;
    --admin-password)   ADMIN_PASSWORD="$2"; shift 2 ;;
    --no-default)       SET_DEFAULT_PROFILE=0; shift ;;
    -h|--help)          usage; exit 0 ;;
    *)                  usage; die "Unknown option: $1" ;;
  esac
done

need dotnet; need curl; need jq; need openssl; need lsof

# ---- resolve versions -------------------------------------------------------
say "Resolving versions"
UMB_VER="$(resolve_umbraco_version "$UMBRACO_VERSION" || true)"
[[ -n "$UMB_VER" ]] || die "Couldn't find Umbraco.Templates version matching '$UMBRACO_VERSION' on NuGet."
CLI_VER="$(resolve_cli_version "$CLI_VERSION" || true)"
[[ -n "$CLI_VER" ]] || die "Couldn't find Umbraco.Community.Cli version matching '$CLI_VERSION'${CLI_SOURCE:+ in $CLI_SOURCE}."
echo "    Umbraco: $UMB_VER"
echo "    CLI:     $CLI_VER${CLI_SOURCE:+ (from $CLI_SOURCE)}"

[[ -n "$SITE_NAME" ]] || SITE_NAME="u${UMB_VER}-cli${CLI_VER}-$(date +%Y%m%d-%H%M%S)"
[[ "$SITE_NAME" =~ ^[A-Za-z0-9._-]+$ ]] || die "Site name may only contain letters, digits, '.', '_' and '-'."
SITE_DIR="$ROOT/sites/$SITE_NAME"
[[ -e "$SITE_DIR" ]] && die "$SITE_DIR already exists. Pick another --name or run ./remove-site.sh $SITE_NAME"

[[ -n "$PORT" ]] || PORT="$(free_port)"
port_in_use "$PORT" && die "Port $PORT is already in use."
HOST="https://localhost:$PORT"
API="$HOST/umbraco/management/api/v1"
PROJECT="UmbracoSite"

dotnet dev-certs https --check --trust >/dev/null 2>&1 \
  || warn "No trusted HTTPS dev certificate. Run 'dotnet dev-certs https --trust' if the CLI reports TLS errors."

# ---- 1. scaffold ------------------------------------------------------------
# Templates live in a per-version custom hive so we never touch the globally installed ones.
HIVE="$ROOT/.cache/templates/$UMB_VER"
if [[ ! -d "$HIVE" ]]; then
  say "Installing Umbraco.Templates $UMB_VER (cached in .cache/templates)"
  dotnet new install "Umbraco.Templates@$UMB_VER" --debug:custom-hive "$HIVE" >/dev/null
fi

say "Scaffolding Umbraco $UMB_VER into sites/$SITE_NAME (restore can take a few minutes)"
mkdir -p "$SITE_DIR/logs"
(
  cd "$SITE_DIR"
  dotnet new umbraco -n "$PROJECT" \
    --friendly-name "$ADMIN_NAME" \
    --email "$ADMIN_EMAIL" \
    --password "$ADMIN_PASSWORD" \
    --development-database-type SQLite \
    --debug:custom-hive "$HIVE" >/dev/null
)

say "Building"
dotnet build "$SITE_DIR/$PROJECT" -nologo -v q > "$SITE_DIR/logs/build.log" 2>&1 \
  || { tail -30 "$SITE_DIR/logs/build.log"; die "Build failed (full log: sites/$SITE_NAME/logs/build.log)"; }

# ---- 2. install the CLI -----------------------------------------------------
say "Installing Umbraco.Community.Cli $CLI_VER as a local tool"
(
  cd "$SITE_DIR"
  dotnet new tool-manifest >/dev/null
  if [[ -n "$CLI_SOURCE" ]]; then
    # Local builds often reuse a version number; drop the cached copy so we get the fresh one.
    rm -rf "$HOME/.nuget/packages/umbraco.community.cli/$(echo "$CLI_VER" | tr '[:upper:]' '[:lower:]')"
    dotnet tool install Umbraco.Community.Cli --version "$CLI_VER" --add-source "$CLI_SOURCE" >/dev/null
  else
    dotnet tool install Umbraco.Community.Cli --version "$CLI_VER" >/dev/null
  fi
)
CLI_DLL="$(find "$HOME/.nuget/packages/umbraco.community.cli/$(echo "$CLI_VER" | tr '[:upper:]' '[:lower:]')" -name Umbraco.Cli.dll -path '*/tools/*' | head -1)"
[[ -f "$CLI_DLL" ]] || die "Couldn't locate Umbraco.Cli.dll for $CLI_VER in the NuGet cache."

# ---- helper scripts in the site folder --------------------------------------
cat > "$SITE_DIR/site.env" <<EOF
SITE_NAME=$SITE_NAME
UMBRACO_VERSION=$UMB_VER
CLI_VERSION=$CLI_VER
PORT=$PORT
HOST=$HOST
PROJECT=$PROJECT
EOF

cat > "$SITE_DIR/start.sh" <<'EOF'
#!/usr/bin/env bash
# Start the site in the background (logs -> logs/site.log) and wait until Umbraco is running.
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$DIR/site.env"
if [[ -f "$DIR/site.pid" ]] && kill -0 "$(cat "$DIR/site.pid")" 2>/dev/null; then
  echo "Already running at $HOST (pid $(cat "$DIR/site.pid"))"; exit 0
fi
cd "$DIR/$PROJECT"
ASPNETCORE_ENVIRONMENT=Development nohup dotnet run --no-build --no-launch-profile --urls "$HOST" \
  >> "$DIR/logs/site.log" 2>&1 &
echo $! > "$DIR/site.pid"
printf 'Starting %s ' "$HOST"
for _ in $(seq 1 "${STARTUP_TIMEOUT:-300}"); do
  status="$(curl -sk --max-time 2 "$HOST/umbraco/management/api/v1/server/status" | sed -n 's/.*"serverStatus":"\([A-Za-z]*\)".*/\1/p' || true)"
  case "$status" in
    Run) echo " running."; exit 0 ;;
    BootFailed) echo; tail -40 "$DIR/logs/site.log"; echo "Umbraco failed to boot." >&2; exit 1 ;;
  esac
  kill -0 "$(cat "$DIR/site.pid")" 2>/dev/null || { echo; tail -40 "$DIR/logs/site.log"; echo "Site process exited." >&2; exit 1; }
  printf '.'; sleep 1
done
echo; echo "Timed out waiting for Umbraco (last status: ${status:-none}). See logs/site.log" >&2; exit 1
EOF

cat > "$SITE_DIR/stop.sh" <<'EOF'
#!/usr/bin/env bash
# Stop the site started by start.sh.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$DIR/site.env"
if [[ -f "$DIR/site.pid" ]]; then
  pid="$(cat "$DIR/site.pid")"
  pkill -P "$pid" 2>/dev/null; kill "$pid" 2>/dev/null
  rm -f "$DIR/site.pid"
fi
# `dotnet run` spawns the app as a child; make sure nothing is left on our port.
lsof -nP -tiTCP:"$PORT" -sTCP:LISTEN 2>/dev/null | xargs kill 2>/dev/null
echo "Stopped $SITE_NAME"
EOF

# Wrapper that runs this site's pinned CLI version against this site's profile, from any cwd.
# It adds --config <harness>/.cli/config.json unless the caller passes its own --config.
mkdir -p "$(dirname "$CLI_CONFIG")"
cat > "$SITE_DIR/umb" <<EOF
#!/usr/bin/env bash
# Umbraco.Community.Cli $CLI_VER, pinned to profile '$SITE_NAME' ($HOST), harness config $CLI_CONFIG.
for a in "\$@"; do [[ "\$a" == --config || "\$a" == --config=* ]] && exec env UMBRACO_PROFILE="\${UMBRACO_PROFILE:-$SITE_NAME}" dotnet "$CLI_DLL" "\$@"; done
UMBRACO_PROFILE="\${UMBRACO_PROFILE:-$SITE_NAME}" exec dotnet "$CLI_DLL" --config "$CLI_CONFIG" "\$@"
EOF
chmod +x "$SITE_DIR/start.sh" "$SITE_DIR/stop.sh" "$SITE_DIR/umb"

# ---- 3. start + unattended install -----------------------------------------
say "Starting site (unattended install on first boot)"
STARTUP_TIMEOUT="$STARTUP_TIMEOUT" "$SITE_DIR/start.sh"

# ---- 4. admin sign-in -> API user -------------------------------------------
say "Signing in to the Management API as $ADMIN_EMAIL"
JAR="$(mktemp)"; trap 'rm -f "$JAR"' EXIT
curl -skf -c "$JAR" -b "$JAR" -o /dev/null -H 'Content-Type: application/json' \
  -d "$(jq -n --arg u "$ADMIN_EMAIL" --arg p "$ADMIN_PASSWORD" '{username:$u,password:$p}')" \
  "$API/security/back-office/login" || die "Admin login failed."

# Authorization code + PKCE flow as the backoffice client. From v17 the code and tokens are
# kept in secure cookies and the JSON carries "[redacted]" placeholders, so we pass whatever
# we are given straight back and let the cookie jar do the rest (older versions get real values).
VERIFIER="$(openssl rand -hex 32)"
CHALLENGE="$(printf %s "$VERIFIER" | openssl dgst -sha256 -binary | openssl base64 | tr '+/' '-_' | tr -d '=\n')"
REDIRECT="$HOST/umbraco/oauth_complete"
LOCATION="$(curl -sk -c "$JAR" -b "$JAR" -o /dev/null -w '%{redirect_url}' -G "$API/security/back-office/authorize" \
  --data-urlencode client_id=umbraco-back-office --data-urlencode response_type=code \
  --data-urlencode "redirect_uri=$REDIRECT" --data-urlencode "code_challenge=$CHALLENGE" \
  --data-urlencode code_challenge_method=S256 --data-urlencode scope=offline_access)"
CODE="$(printf %s "$LOCATION" | sed -n 's/.*[?&]code=\([^&]*\).*/\1/p' | sed 's/%5B/[/g; s/%5D/]/g')"
[[ -n "$CODE" ]] || die "No authorization code returned (redirect: ${LOCATION:-none})."

ACCESS_TOKEN="$(curl -skf -c "$JAR" -b "$JAR" -d grant_type=authorization_code -d client_id=umbraco-back-office \
  --data-urlencode "redirect_uri=$REDIRECT" --data-urlencode "code=$CODE" -d "code_verifier=$VERIFIER" \
  "$API/security/back-office/token" | jq -r .access_token)"
[[ -n "$ACCESS_TOKEN" && "$ACCESS_TOKEN" != null ]] || die "Token exchange failed."

mapi() { curl -sk -b "$JAR" -H "Authorization: Bearer $ACCESS_TOKEN" -H 'Content-Type: application/json' "$@"; }

say "Creating API user $API_USER_EMAIL (Administrators + Sensitive data)"
GROUP_IDS="$(mapi "$API/user-group?take=100" \
  | jq -c '[.items[] | select(.alias == "admin" or .alias == "sensitiveData") | {id}]')"
[[ "$GROUP_IDS" != "[]" ]] || die "Couldn't read user groups."

USER_ID="$(mapi -D - -o /dev/null -X POST "$API/user" \
  -d "$(jq -n --arg e "$API_USER_EMAIL" --argjson g "$GROUP_IDS" \
        '{email:$e,userName:$e,name:"CLI API User",kind:"Api",userGroupIds:$g}')" \
  | tr -d '\r' | awk -F': ' 'tolower($1)=="umb-generated-resource"{print $2}')"
[[ -n "$USER_ID" ]] || die "Creating the API user failed (API users need Umbraco 15+)."

API_CLIENT_SECRET="$(openssl rand -hex 24)"
STATUS="$(mapi -o /dev/null -w '%{http_code}' -X POST "$API/user/$USER_ID/client-credentials" \
  -d "$(jq -n --arg c "$API_CLIENT_ID" --arg s "$API_CLIENT_SECRET" '{clientId:$c,clientSecret:$s}')")"
[[ "$STATUS" == 200 ]] || die "Adding client credentials failed (HTTP $STATUS)."

# Local throwaway test site, so plaintext is fine - handy when you need to redo `auth login`.
jq -n --arg host "$HOST" --arg ae "$ADMIN_EMAIL" --arg ap "$ADMIN_PASSWORD" \
      --arg ci "$API_CLIENT_ID" --arg cs "$API_CLIENT_SECRET" --arg ui "$USER_ID" \
  '{host:$host, admin:{email:$ae,password:$ap}, apiUser:{id:$ui,clientId:$ci,clientSecret:$cs}}' \
  > "$SITE_DIR/credentials.json"
chmod 600 "$SITE_DIR/credentials.json"

# ---- 5. hook up the CLI -----------------------------------------------------
say "Logging the CLI in to profile '$SITE_NAME'"
"$SITE_DIR/umb" auth login --profile "$SITE_NAME" --host "$HOST" --client-id "$API_CLIENT_ID" --client-secret "$API_CLIENT_SECRET" \
  --output json >/dev/null || die "umbraco auth login failed."
if [[ "$SET_DEFAULT_PROFILE" == 1 ]]; then
  # alpha.12+ renamed 'auth use' to 'auth profile use'; try the new verb first.
  if ! "$SITE_DIR/umb" auth profile use "$SITE_NAME" --output json >/dev/null 2>&1 &&
    ! "$SITE_DIR/umb" auth use "$SITE_NAME" --output json >/dev/null 2>&1; then
    warn "Couldn't set '$SITE_NAME' as the default profile."
    SET_DEFAULT_PROFILE=0
  fi
fi

say "Running umbraco auth doctor"
if ! "$SITE_DIR/umb" auth doctor --output human; then
  die "auth doctor reported a failure - see above."
fi

cat <<EOF

$(printf '\033[1;32m')Ready!$(printf '\033[0m')  sites/$SITE_NAME

  Site:        $HOST/umbraco   ($ADMIN_EMAIL / $ADMIN_PASSWORD)
  Umbraco:     $UMB_VER
  CLI:         $CLI_VER  (profile '$SITE_NAME'$([[ $SET_DEFAULT_PROFILE == 1 ]] && echo ", now the default"))

  sites/$SITE_NAME/umb <command>    run the CLI against this site, e.g. ./umb content list
  sites/$SITE_NAME/stop.sh          stop the site
  sites/$SITE_NAME/start.sh         start it again
  ./remove-site.sh $SITE_NAME       stop, log out the profile and delete the folder
EOF
