#!/usr/bin/env python3
"""setup-round.py - one command to get a test round ready:
  preflight -> CLI build -> source + staging sites -> Part A (tools/build_site.py) -> form controller -> form checks.
Writes round.env (versions, commit, site paths) for the agent and the ledger header.

CLI build, pick one:
  --from-repo            pack this repository checkout (default)
  --nupkg <file.nupkg>   a locally built package someone handed you
  --nuget <version>      a published version from nuget.org ("latest" = newest, incl. prereleases)

Other options:
  --umbraco <ver>        Umbraco version for the sites (major, exact or "latest"). Default: 17
  --replace              remove existing 'source' / 'staging' sites first
  --no-staging           skip the staging site (T1 promotion needs it)
"""
import argparse
import os
import re
import shutil
import sys
import time
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "tools"))
import harness  # noqa: E402
from harness import ROOT, SITES, heading  # noqa: E402

PY = sys.executable


def step(*args, env=None, capture=False):
    """Run another harness script with this Python, from tests/hands-on.

    :param args: the script and its arguments.
    :param env: extra environment variables.
    :param capture: capture stdout (returned) instead of streaming it.
    :returns: the `subprocess.CompletedProcess`.
    """
    kw = {"capture_output": True} if capture else {}
    return harness.run([PY, *args], cwd=ROOT, env={**os.environ, **(env or {})}, **kw)


def nupkg_commit(path):
    """The short commit recorded in a .nupkg's nuspec (`<repository commit="..."/>`), or `unknown`."""
    try:
        with zipfile.ZipFile(path) as z:
            nuspec = next(n for n in z.namelist() if n.endswith(".nuspec"))
            m = re.search(r'commit="([^"]+)"', z.read(nuspec).decode("utf-8", "replace"))
            return m.group(1)[:7] if m else "unknown"
    except (OSError, StopIteration, zipfile.BadZipFile):
        return "unknown"


def main():
    harness.utf8_stdio()
    p = argparse.ArgumentParser(prog="setup-round.py", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter,
                                usage="python3 setup-round.py [--from-repo | --nupkg FILE | --nuget VERSION] [options]")
    build = p.add_mutually_exclusive_group()
    build.add_argument("--from-repo", action="store_true", help=argparse.SUPPRESS)
    build.add_argument("--nupkg", help=argparse.SUPPRESS)
    build.add_argument("--nuget", help=argparse.SUPPRESS)
    p.add_argument("--umbraco", default="17", help=argparse.SUPPRESS)
    p.add_argument("--replace", action="store_true", help=argparse.SUPPRESS)
    p.add_argument("--no-staging", action="store_true", help=argparse.SUPPRESS)
    a = p.parse_args()
    mode = "nupkg" if a.nupkg else "nuget" if a.nuget else "repo"

    heading("Preflight")
    if step("preflight.py").returncode:
        sys.exit(1)

    heading("CLI build")
    commit = "unknown"
    if mode == "repo":
        res = step("pack-cli.py", capture=True)
        sys.stderr.write(res.stderr)
        if res.returncode:
            sys.exit(1)
        cli_ver = res.stdout.strip().splitlines()[-1]
        cli_args = ["--cli-source", "nupkg", "--cli", cli_ver]
        commit = harness.run(["git", "rev-parse", "--short", "HEAD"], cwd=ROOT, capture_output=True).stdout.strip()
        if harness.run(["git", "status", "--porcelain", "--", "../../src"], cwd=ROOT, capture_output=True).stdout.strip():
            commit += "+dirty"
    elif mode == "nupkg":
        src = Path(a.nupkg).expanduser()
        src.is_file() or harness.die(f"No such file: {a.nupkg}", 2)
        (ROOT / "nupkg").mkdir(exist_ok=True)
        shutil.copy2(src, ROOT / "nupkg" / src.name)
        m = re.fullmatch(r"umbraco\.community\.cli\.(.+)\.nupkg", src.name, re.I)
        m or harness.die(f"{src.name} doesn't look like Umbraco.Community.Cli.<version>.nupkg", 2)
        cli_ver, commit = m.group(1), nupkg_commit(src)
        cli_args = ["--cli-source", "nupkg", "--cli", cli_ver]
    else:
        cli_ver, cli_args = a.nuget, ["--cli", a.nuget]
    print(f"CLI {cli_ver} (commit {commit})")

    names = ["source"] + ([] if a.no_staging else ["staging"])
    if a.replace:
        for s in ("source", "staging"):
            if (SITES / s).is_dir():
                step("remove-site.py", s)
    for s in names:
        if (SITES / s).is_dir():
            harness.die(f"sites/{s} already exists: re-run with --replace, or python3 remove-site.py {s}")

    heading("Source site")
    if step("new-site.py", "--name", "source", "--umbraco", a.umbraco, *cli_args).returncode:
        sys.exit(1)
    if not a.no_staging:
        heading("Staging site (T1 promotion target)")
        if step("new-site.py", "--name", "staging", "--umbraco", a.umbraco, *cli_args, "--no-default").returncode:
            sys.exit(1)

    source = harness.Site("source")
    staging = None if a.no_staging else harness.Site("staging")
    round_env = {"ROUND_DATE": time.strftime("%Y-%m-%d"), "CLI_VERSION": source.env["CLI_VERSION"], "CLI_COMMIT": commit,
                 "CLI_FROM": mode, "UMBRACO_VERSION": source.env["UMBRACO_VERSION"], "SOURCE": "sites/source",
                 "SOURCE_HOST": source.host, "STAGING": "sites/staging" if staging else "",
                 "STAGING_HOST": staging.host if staging else ""}
    harness.write_env(ROOT / "round.env", round_env)

    heading("Part A: build the site with the CLI (tools/build_site.py)")
    build_ok = step("tools/build_site.py", env={"UMB_SITE": "sites/source"}).returncode == 0

    heading("Form controller + form checks")
    if step("tools/install_controller.py", "sites/source").returncode:
        sys.exit(1)
    forms_ok = step("tools/submit_forms.py", env={"UMB_SITE": "sites/source"}).returncode == 0

    heading("Ready")
    print((ROOT / "round.env").read_text(encoding="utf-8"))
    print(f"Part A build: {'PASS' if build_ok else 'FAIL (see the FAIL lines above)'}   Forms: {'PASS' if forms_ok else 'FAIL'}")
    print("Next: follow AGENTS.md from 'Step 3' (re-tests, then TEST-PLAN.md Part B).")


if __name__ == "__main__":
    main()
