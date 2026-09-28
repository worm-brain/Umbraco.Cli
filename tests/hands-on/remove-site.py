#!/usr/bin/env python3
"""remove-site.py <name> - stop a test site, remove its CLI profile and delete its folder.
remove-site.py --list  - list test sites and whether they're running.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "tools"))
import harness  # noqa: E402


def list_sites():
    """Print one line per site: name, Umbraco and CLI versions, host, running or stopped."""
    for s in harness.list_sites():
        state = "running" if s.is_running() else "stopped"
        print(f"{s.name:<50} umbraco {s.env['UMBRACO_VERSION']:<12} cli {s.env['CLI_VERSION']:<16} {s.host:<26} {state}")


def remove(name):
    """Stop the site, log its profile out of the harness config and delete sites/<name>."""
    site = harness.Site(harness.SITES / name) if (harness.SITES / name / "site.env").exists() else None
    if site is None:
        harness.die(f"No site at sites/{name}")
    site.stop()
    ok = site.umb("auth", "logout", "--profile", name, "--output", "json").returncode == 0
    print(f"Removed CLI profile '{name}'" if ok else f"CLI profile '{name}' not removed (may not exist)")
    harness.remove_tree(site.dir)
    print(f"Deleted sites/{name}")


def main():
    harness.utf8_stdio()
    arg = sys.argv[1] if len(sys.argv) > 1 else ""
    if arg in ("-h", "--help"):
        print(__doc__.strip())
    elif arg in ("", "--list"):
        list_sites()
        if not arg:
            print("\nUsage: python3 remove-site.py <name>")
    else:
        remove(arg)


if __name__ == "__main__":
    main()
