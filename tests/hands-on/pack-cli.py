#!/usr/bin/env python3
"""pack-cli.py - pack the CLI from this repository checkout into nupkg/ with a unique local version,
and print that version (last line of output). Used by setup-round.py --from-repo.

  python3 pack-cli.py      -> e.g. 0.1.0-local.20260928.140501
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "tools"))
import harness  # noqa: E402

REPO = harness.ROOT.parent.parent
PROJ = REPO / "src" / "Umbraco.Cli" / "Umbraco.Cli.csproj"


def main():
    harness.utf8_stdio()
    if not PROJ.is_file():
        harness.die(f"{PROJ} not found (is the harness still in tests/hands-on of the repo?)")
    version = f"0.1.0-local.{time.strftime('%Y%m%d.%H%M%S')}"
    commit = harness.run(["git", "-C", REPO, "rev-parse", "--short", "HEAD"], capture_output=True).stdout.strip() or "unknown"
    dirty = harness.run(["git", "-C", REPO, "status", "--porcelain", "--", "src"], capture_output=True).stdout.strip()
    print(f"Packing {PROJ} as {version} (commit {commit}{', with uncommitted changes in src/' if dirty else ''})", file=sys.stderr)

    out = harness.ROOT / "nupkg"
    out.mkdir(exist_ok=True)
    log = out / "pack.log"
    with open(log, "wb") as f:
        res = harness.run(["dotnet", "pack", PROJ, "-c", "Release", "-o", out, f"-p:Version={version}", "-nologo", "-v", "q"],
                          stdout=f, stderr=f)
    if res.returncode:
        print("\n".join(log.read_text(encoding="utf-8", errors="replace").splitlines()[-30:]), file=sys.stderr)
        harness.die("dotnet pack failed (full log: nupkg/pack.log)")
    print(version)


if __name__ == "__main__":
    main()
