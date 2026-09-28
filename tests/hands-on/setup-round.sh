#!/usr/bin/env bash
#
# setup-round.sh - one command to get a test round ready:
#   preflight -> CLI build -> source + staging sites -> Part A (tools/build_site.py) -> form controller -> form checks.
# Writes round.env (versions, commit, site paths) for the agent and the ledger header.
#
# CLI build, pick one:
#   --from-repo            pack this repository checkout (default)
#   --nupkg <file.nupkg>   a locally built package someone handed you
#   --nuget <version>      a published version from nuget.org ("latest" = newest, incl. prereleases)
#
# Other options:
#   --umbraco <ver>        Umbraco version for the sites (major, exact or "latest"). Default: 17
#   --replace              remove existing 'source' / 'staging' sites first
#   --no-staging           skip the staging site (T1 promotion needs it)
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

MODE=repo; NUPKG=""; NUGET_VER=""; UMBRACO=17; REPLACE=0; STAGING=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --from-repo) MODE=repo; shift ;;
    --nupkg) MODE=nupkg; NUPKG="$2"; shift 2 ;;
    --nuget) MODE=nuget; NUGET_VER="$2"; shift 2 ;;
    --umbraco) UMBRACO="$2"; shift 2 ;;
    --replace) REPLACE=1; shift ;;
    --no-staging) STAGING=0; shift ;;
    -h|--help) sed -n '2,/^set -e/p' "$0" | sed '$d; s/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
done
say() { printf '\n\033[1;35m### %s\033[0m\n' "$*"; }

say "Preflight"
./preflight.sh

say "CLI build"
COMMIT=unknown
case "$MODE" in
  repo)
    CLI_VER="$(./pack-cli.sh | tail -1)"; CLI_ARGS=(--cli-source nupkg --cli "$CLI_VER")
    COMMIT="$(git rev-parse --short HEAD)"
    [[ -z "$(git status --porcelain -- ../../src)" ]] || COMMIT="$COMMIT+dirty" ;;
  nupkg)
    [[ -f "$NUPKG" ]] || { echo "No such file: $NUPKG" >&2; exit 2; }
    mkdir -p nupkg; cp "$NUPKG" nupkg/
    CLI_VER="$(basename "$NUPKG" | sed -E 's/^[Uu]mbraco\.[Cc]ommunity\.[Cc]li\.(.*)\.nupkg$/\1/')"
    COMMIT="$(unzip -p "$NUPKG" '*.nuspec' | grep -o 'commit="[^"]*"' | cut -d'"' -f2 | cut -c1-7 || echo unknown)"
    CLI_ARGS=(--cli-source nupkg --cli "$CLI_VER") ;;
  nuget)
    CLI_VER="$NUGET_VER"; CLI_ARGS=(--cli "$CLI_VER") ;;
esac
echo "CLI $CLI_VER (commit $COMMIT)"

if [[ $REPLACE == 1 ]]; then
  for s in source staging; do [[ -d sites/$s ]] && ./remove-site.sh "$s"; done
fi
for s in source staging; do
  [[ -d sites/$s ]] && { echo "sites/$s already exists: re-run with --replace, or ./remove-site.sh $s" >&2; exit 1; }
done

say "Source site"
./new-site.sh --name source --umbraco "$UMBRACO" "${CLI_ARGS[@]}"
if [[ $STAGING == 1 ]]; then
  say "Staging site (T1 promotion target)"
  ./new-site.sh --name staging --umbraco "$UMBRACO" "${CLI_ARGS[@]}" --no-default
fi

source sites/source/site.env
UMB_VER="$UMBRACO_VERSION"; CLI_VER="$CLI_VERSION"
cat > round.env <<ENV
ROUND_DATE=$(date +%Y-%m-%d)
CLI_VERSION=$CLI_VER
CLI_COMMIT=$COMMIT
CLI_FROM=$MODE
UMBRACO_VERSION=$UMB_VER
SOURCE=sites/source
SOURCE_HOST=$HOST
STAGING=$([[ $STAGING == 1 ]] && echo sites/staging)
STAGING_HOST=$([[ $STAGING == 1 ]] && (source sites/staging/site.env; echo "$HOST"))
ENV

say "Part A: build the site with the CLI (tools/build_site.py)"
set +e
UMB_SITE=sites/source python3 tools/build_site.py; BUILD=$?
set -e

say "Form controller + form checks"
tools/install_controller.sh sites/source
set +e
UMB_SITE=sites/source python3 tools/submit_forms.py; FORMS=$?
set -e

say "Ready"
cat round.env
echo
echo "Part A build: $([[ $BUILD == 0 ]] && echo PASS || echo "FAIL (see the FAIL lines above)")   Forms: $([[ $FORMS == 0 ]] && echo PASS || echo FAIL)"
echo "Next: follow AGENTS.md from 'Step 3' (re-tests, then TEST-PLAN.md Part B)."
