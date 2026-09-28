#!/usr/bin/env bash
#
# preflight.sh - check this machine can run the hands-on harness. Prints one line per check and
# exits 1 if anything required is missing. Safe to run any time; it changes nothing.
set -uo pipefail

ok=0; bad=0
pass() { printf '\033[32mpass\033[0m  %s\n' "$*"; ok=$((ok + 1)); }
fail() { printf '\033[31mFAIL\033[0m  %s\n' "$*"; bad=$((bad + 1)); }
note() { printf '\033[33mnote\033[0m  %s\n' "$*"; }

for tool in bash curl jq openssl lsof python3 git; do
  command -v "$tool" >/dev/null 2>&1 && pass "$tool" || fail "$tool is not installed"
done

if command -v dotnet >/dev/null 2>&1; then
  sdks="$(dotnet --list-sdks 2>/dev/null)"
  runtimes="$(dotnet --list-runtimes 2>/dev/null)"
  grep -qE '^1[0-9]\.' <<<"$sdks" && pass ".NET SDK 10+ (Umbraco 17 sites)" || fail ".NET SDK 10 or later is required to build Umbraco 17 sites"
  grep -q 'Microsoft.NETCore.App 9\.' <<<"$runtimes" && pass ".NET 9 runtime (the CLI targets net9.0)" \
    || fail ".NET 9 runtime is required to run the CLI (install the .NET 9 runtime alongside SDK 10)"
  grep -q 'Microsoft.AspNetCore.App 10\.' <<<"$runtimes" && pass "ASP.NET Core 10 runtime" || fail "ASP.NET Core 10 runtime is missing"
  if dotnet dev-certs https --check --trust >/dev/null 2>&1; then
    pass "trusted HTTPS dev certificate"
  else
    fail "no trusted HTTPS dev certificate: run 'dotnet dev-certs https --trust' (on Linux see the README)"
  fi
else
  fail "dotnet is not installed"
fi

curl -fsS --max-time 10 https://api.nuget.org/v3/index.json >/dev/null 2>&1 && pass "api.nuget.org reachable" \
  || fail "api.nuget.org is not reachable (needed for Umbraco.Templates and packages)"

busy=""
for p in 44800 44801 44802 44803 44804; do lsof -nP -iTCP:"$p" -sTCP:LISTEN >/dev/null 2>&1 && busy="$busy $p"; done
[[ -z "$busy" ]] && pass "ports 44800-44804 free" || note "ports in use:$busy (new sites take the next free port from 44800)"

case "$(uname -s)" in
  Darwin|Linux) pass "OS: $(uname -s)" ;;
  *) note "OS $(uname -s) is untested; use macOS, Linux or WSL" ;;
esac

echo
echo "$ok passed, $bad failed"
[[ $bad -eq 0 ]]
