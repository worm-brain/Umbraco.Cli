#!/usr/bin/env python3
"""preflight.py - check this machine can run the hands-on harness. Prints one line per check and
exits 1 if anything required is missing. Safe to run any time; it changes nothing.
"""
import platform
import shutil
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "tools"))
import harness  # noqa: E402

counts = {"ok": 0, "bad": 0}


def show(code, label, msg):
    print(f"{harness.colour(code, label, sys.stdout)}  {msg}")


def check(ok, good, bad):
    """Record and print a required check: `pass <good>` or `FAIL <bad>`."""
    counts["ok" if ok else "bad"] += 1
    show("32", "pass", good) if ok else show("31", "FAIL", bad)


def note(msg):
    """Print an informational line that doesn't fail the preflight."""
    show("33", "note", msg)


def main():
    harness.utf8_stdio()
    check(sys.version_info >= (3, 9), f"Python {platform.python_version()}", "Python 3.9 or later is required")
    check(bool(shutil.which("git")), "git", "git is not installed")

    if shutil.which("dotnet"):
        sdks = harness.run(["dotnet", "--list-sdks"], capture_output=True).stdout
        runtimes = harness.run(["dotnet", "--list-runtimes"], capture_output=True).stdout
        sdk_majors = {int(line.split(".")[0]) for line in sdks.splitlines() if line[:1].isdigit()}
        check(any(m >= 10 for m in sdk_majors), ".NET SDK 10+ (Umbraco 17 sites)",
              ".NET SDK 10 or later is required to build Umbraco 17 sites")
        check("Microsoft.NETCore.App 9." in runtimes, ".NET 9 runtime (the CLI targets net9.0)",
              ".NET 9 runtime is required to run the CLI (install the .NET 9 runtime alongside SDK 10)")
        check("Microsoft.AspNetCore.App 10." in runtimes, "ASP.NET Core 10 runtime", "ASP.NET Core 10 runtime is missing")
        trusted = harness.run(["dotnet", "dev-certs", "https", "--check", "--trust"], capture_output=True).returncode == 0
        check(trusted, "trusted HTTPS dev certificate",
              "no trusted HTTPS dev certificate: run 'dotnet dev-certs https --trust' (on Linux see the README)")
    else:
        check(False, "", "dotnet is not installed")

    try:
        urllib.request.urlopen("https://api.nuget.org/v3/index.json", timeout=10).close()
        reachable = True
    except OSError:
        reachable = False
    check(reachable, "api.nuget.org reachable", "api.nuget.org is not reachable (needed for Umbraco.Templates and packages)")

    busy = [p for p in range(44800, 44805) if harness.port_in_use(p)]
    if busy:
        note(f"ports in use: {' '.join(map(str, busy))} (new sites take the next free port from 44800)")
    else:
        check(True, "ports 44800-44804 free", "")

    system = platform.system()
    if system in ("Darwin", "Linux", "Windows"):
        check(True, f"OS: {system}", "")
    else:
        note(f"OS {system} is untested; use macOS, Linux or Windows")

    # Not needed by the scripts, but the hand-run snippets in TEST-PLAN.md are bash + jq (+ curl).
    missing = [t for t in ("bash", "jq", "curl") if not shutil.which(t)]
    if missing:
        hint = " (on Windows: Git Bash, and 'winget install jqlang.jq')" if harness.WINDOWS else ""
        note(f"not found: {', '.join(missing)}: needed for the TEST-PLAN.md snippets, not the scripts{hint}")
    if not shutil.which("gh"):
        note("gh is not installed: only needed for re-tests (Step 3) and filing issues (Step 7)")

    print(f"\n{counts['ok']} passed, {counts['bad']} failed")
    sys.exit(1 if counts["bad"] else 0)


if __name__ == "__main__":
    main()
