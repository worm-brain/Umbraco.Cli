#!/usr/bin/env python3
"""dev-site.py - a persistent throwaway Umbraco site for day-to-day CLI development (sites/dev).

It replaces a hand-built instance (such as https://localhost:45000) for the live integration suite
and scripts/fetch-spec.ps1. The first `up` creates the site and fills it with the Part A fixture
site (tools/build_site.py), so the integration tests find content, languages and document types
to work with. After that, `up` and `down` just start and stop it.

  up [--umbraco <ver>] [--nuget <ver>]   create sites/dev (first time) or start it
  down                                  stop it
  test [dotnet test options...]         run tests/Umbraco.Cli.IntegrationTests against it (starts it first)
  env [--shell bash|pwsh]               print its host, credentials and test config
  reset [--umbraco <ver>] [--nuget <ver>]
                                        delete it and create it again

The site has its own CLI config, sites/dev/test-config.json, holding a single default profile
'dev'. `test` points the suite at it (UMBRACO_TEST_CONFIG), so the suite never reads or writes
your own `umbraco` profiles.

By default the site's CLI (used to build the fixture site) is packed from this checkout; pass
--nuget <version|latest> to use a published build. --umbraco pins the Umbraco version (major,
exact or "latest"; default 17), e.g. to match the version spec/management.json was fetched from.
"""
import argparse
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "tools"))
import harness  # noqa: E402
from harness import ROOT, SITES, die, heading, say, warn  # noqa: E402

NAME = "dev"
SITE_DIR = SITES / NAME
TEST_CONFIG = SITE_DIR / "test-config.json"
REPO = ROOT.parent.parent
SUITE = REPO / "tests" / "Umbraco.Cli.IntegrationTests" / "Umbraco.Cli.IntegrationTests.csproj"
PY = sys.executable

# A stand-in package that declares CLI support the way a real package would (docs/extensions.md), so
# the live suite can check that the CLI reads `umbracoCli` declarations from the site's manifest. It
# has no code: Umbraco serves any App_Plugins/*/umbraco-package.json in the web root as a package.
CLI_FIXTURE_ID = "Umbraco.Cli.Fixture"
CLI_FIXTURE = {
    "id": CLI_FIXTURE_ID,
    "name": "Umbraco CLI fixture",
    "version": "1.0.0",
    "extensions": [
        {
            "type": "umbracoCli",
            "alias": "Umbraco.Cli.Fixture.Cli",
            "name": "Umbraco CLI fixture",
            "meta": {"dictionaryValueFormat": "markdown"},
        }
    ],
}


def script(*args, env=None, capture=False):
    """Run another harness script with this Python, from tests/hands-on."""
    kw = {"capture_output": True} if capture else {}
    return harness.run([PY, *args], cwd=ROOT, env={**os.environ, **(env or {})}, **kw)


def write_test_config(site):
    """Log the site's API user into sites/dev/test-config.json as its only (and default) profile.

    :param site: the dev Site.
    :raises SystemExit: when the CLI can't log in.
    """
    creds = site.credentials()
    cfg = ["--config", str(TEST_CONFIG)]
    login = site.umb(*cfg, "auth", "login", "--profile", NAME, "--host", site.host, "--client-id",
                     creds["apiUser"]["clientId"], "--client-secret", creds["apiUser"]["clientSecret"], "-o", "json")
    if login.returncode or site.umb(*cfg, "auth", "profile", "use", NAME, "-o", "json").returncode:
        die(f"Couldn't write {TEST_CONFIG}: {login.stderr.strip() or login.stdout.strip()}")


def create(umbraco, nuget):
    """Create sites/dev: the site itself, the Part A fixture content, and its test config.

    :param umbraco: the Umbraco version to install.
    :param nuget: a published CLI version, or None to pack this checkout.
    :raises SystemExit: when any step fails (a Part A failure only warns: the site is still usable).
    """
    heading("CLI build")
    if nuget:
        cli_args = ["--cli", nuget]
    else:
        res = script("pack-cli.py", capture=True)
        sys.stderr.write(res.stderr)
        if res.returncode:
            sys.exit(1)
        cli_args = ["--cli-source", "nupkg", "--cli", res.stdout.strip().splitlines()[-1]]

    heading("Dev site")
    # --no-default: rounds and single sites keep whichever harness profile they already use.
    if script("new-site.py", "--name", NAME, "--umbraco", umbraco, *cli_args, "--no-default").returncode:
        sys.exit(1)

    heading("Fixture content (tools/build_site.py)")
    if script("tools/build_site.py", env={"UMB_SITE": f"sites/{NAME}"}).returncode:
        warn("Part A reported failures (above). The site works, but some integration tests may skip or fail.")

    write_test_config(harness.Site(NAME))


def ensure_cli_fixture(site):
    """Write the fixture package's manifest into the site's web root, unless it is already there.

    :param site: the dev Site.
    :returns: True when the file was written or changed; a running site must restart to see it,
        because Umbraco reads package manifests once and caches them.
    """
    path = site.project / "wwwroot" / "App_Plugins" / CLI_FIXTURE_ID / "umbraco-package.json"
    text = json.dumps(CLI_FIXTURE, indent=2) + "\n"
    if path.exists() and path.read_text(encoding="utf-8") == text:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return True


def up(a):
    """Create the dev site if it doesn't exist, otherwise start it. Prints how to use it."""
    if not (SITE_DIR / "site.env").exists():
        create(a.umbraco, a.nuget)
    site = harness.Site(NAME)
    # Sites created before the fixture existed get it here; restart so Umbraco reads the new manifest.
    if ensure_cli_fixture(site) and site.is_running():
        say("Restarting to load the CLI fixture package")
        site.stop()
    if not site.is_running():
        say(f"Starting {site.name}")
        site.start()
    if not TEST_CONFIG.exists():
        write_test_config(site)
    print(f"\nDev site running: {site.host}/umbraco  (Umbraco {site.env['UMBRACO_VERSION']})")
    print("  python3 dev-site.py test    run the live integration suite against it")
    print("  python3 dev-site.py env     host and credentials for other tools (e.g. scripts/fetch-spec.ps1)")
    print("  python3 dev-site.py down    stop it")
    return site


def down(_):
    """Stop the dev site (it keeps its content for the next `up`)."""
    if not (SITE_DIR / "site.env").exists():
        die("There is no dev site. Create one with: python3 dev-site.py up")
    harness.Site(NAME).stop()


def env(a):
    """Print the dev site's host, API-user credentials and test config, optionally as shell assignments."""
    if not (SITE_DIR / "site.env").exists():
        die("There is no dev site. Create one with: python3 dev-site.py up")
    site = harness.Site(NAME)
    creds = site.credentials()
    values = {"UMBRACO_HOST": site.host, "UMBRACO_CLIENT_ID": creds["apiUser"]["clientId"],
              "UMBRACO_CLIENT_SECRET": creds["apiUser"]["clientSecret"], "UMBRACO_TEST_CONFIG": str(TEST_CONFIG)}
    for key, value in values.items():
        if a.shell == "pwsh":
            print(f"$env:{key} = '{value}'")
        elif a.shell == "bash":
            print(f"export {key}='{value}'")
        else:
            print(f"{key}={value}")
    if not site.is_running():
        warn("The dev site is stopped: python3 dev-site.py up")


def test(a, extra):
    """Run the live integration suite against the dev site and exit with its result.

    Every UMBRACO_* variable is cleared for the run, so a host, credentials, profile, allow-list or
    read-only setting in your shell can't point the suite anywhere else or change what it can do.
    """
    up(a)
    run_env = {k: v for k, v in os.environ.items() if not k.upper().startswith("UMBRACO_")}
    run_env["UMBRACO_TEST_CONFIG"] = str(TEST_CONFIG)
    heading("Integration tests (tests/Umbraco.Cli.IntegrationTests)")
    sys.exit(harness.run(["dotnet", "test", SUITE, *extra], cwd=REPO, env=run_env).returncode)


def reset(a):
    """Delete the dev site and create it again from scratch."""
    if (SITE_DIR / "site.env").exists():
        if script("remove-site.py", NAME).returncode:
            sys.exit(1)
    up(a)


def main():
    harness.utf8_stdio()
    p = argparse.ArgumentParser(prog="dev-site.py", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="command", required=True, metavar="{up,down,test,env,reset}")
    for name in ("up", "test", "reset"):
        s = sub.add_parser(name)
        s.add_argument("--umbraco", default="17", help="Umbraco version when creating the site. Default: 17")
        s.add_argument("--nuget", help="Use a published CLI build (version or 'latest') instead of packing this checkout")
    sub.add_parser("down")
    e = sub.add_parser("env")
    e.add_argument("--shell", choices=("bash", "pwsh"), help="Print assignments for this shell")
    # `test` passes anything it doesn't recognise straight to `dotnet test` (e.g. --filter ...).
    a, extra = p.parse_known_args()
    if extra and a.command != "test":
        p.error(f"unrecognised arguments: {' '.join(extra)}")
    if a.command == "test":
        test(a, extra)
    else:
        {"up": up, "down": down, "env": env, "reset": reset}[a.command](a)


if __name__ == "__main__":
    main()
