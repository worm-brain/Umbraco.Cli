#!/usr/bin/env bash
#
# pack-cli.sh - pack the CLI from this repository checkout into nupkg/ with a unique local version,
# and print that version (last line of output). Used by setup-round.sh --from-repo.
#
#   ./pack-cli.sh            -> e.g. 0.1.0-local.20260928.140501
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
PROJ="$REPO/src/Umbraco.Cli/Umbraco.Cli.csproj"
[[ -f "$PROJ" ]] || { echo "error: $PROJ not found (is the harness still in tests/hands-on of the repo?)" >&2; exit 1; }
VERSION="0.1.0-local.$(date +%Y%m%d.%H%M%S)"
COMMIT="$(git -C "$REPO" rev-parse --short HEAD 2>/dev/null || echo unknown)"
DIRTY="$(git -C "$REPO" status --porcelain -- src 2>/dev/null | head -1)"
echo "Packing $PROJ as $VERSION (commit $COMMIT${DIRTY:+, with uncommitted changes in src/})" >&2
mkdir -p "$HERE/nupkg"
dotnet pack "$PROJ" -c Release -o "$HERE/nupkg" -p:Version="$VERSION" -nologo -v q > "$HERE/nupkg/pack.log" 2>&1 \
  || { tail -30 "$HERE/nupkg/pack.log" >&2; echo "error: dotnet pack failed (full log: nupkg/pack.log)" >&2; exit 1; }
echo "$VERSION"
