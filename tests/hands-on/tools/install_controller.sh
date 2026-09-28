#!/usr/bin/env bash
#
# install_controller.sh <site-dir> - add the contact/sign-up form controller (site code, not CLI) to a
# test site, rebuild it and restart it. Needed before tools/submit_forms.py can pass.
set -euo pipefail
SITE="${1:?usage: tools/install_controller.sh sites/<name>}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$SITE/site.env"
mkdir -p "$SITE/$PROJECT/Controllers"
cp "$HERE/assets/ContactSurfaceController.cs" "$SITE/$PROJECT/Controllers/"
"$SITE/stop.sh" >/dev/null
dotnet build "$SITE/$PROJECT" -nologo -v q > "$SITE/logs/build.log" 2>&1 || { tail -30 "$SITE/logs/build.log"; exit 1; }
"$SITE/start.sh"
