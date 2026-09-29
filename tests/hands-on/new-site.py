#!/usr/bin/env python3
"""new-site.py - spin up a fresh Umbraco test site wired to Umbraco.Community.Cli.

  1. Scaffolds an Umbraco project (SQLite, unattended install, admin user)
  2. Installs Umbraco.Community.Cli as a local tool for the site
  3. Starts the site and waits for the unattended install to finish
  4. Signs in as the admin, creates an API user with client credentials
  5. Runs `umbraco auth login` into a profile named after the site and checks `auth doctor`

Every CLI call goes through the site's pinned CLI with the harness's own config (.cli/config.json next to
this script), so the user's real `umbraco` profiles are never touched.

Run `python3 new-site.py --help` for options.
"""
import argparse
import json
import os
import re
import secrets
import shutil
import subprocess
import sys
import time
import urllib.parse
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "tools"))
import harness  # noqa: E402
from harness import CLI_CONFIG, ROOT, SITES, WINDOWS, die, say, warn  # noqa: E402

PROJECT = "UmbracoSite"
ADMIN_NAME = "Administrator"
API_USER_EMAIL = "cli@example.com"
API_CLIENT_ID = "umbraco-back-office-cli"

EXAMPLES = """examples:
  python3 new-site.py
  python3 new-site.py --umbraco 17.6.0 --cli 0.1.0-alpha.5
  python3 new-site.py --umbraco 18 --name v18-smoke
  python3 new-site.py --cli-source nupkg --cli <version printed by pack-cli.py>   # this repository's CLI
"""


def parse_args():
    p = argparse.ArgumentParser(prog="new-site.py", formatter_class=argparse.RawDescriptionHelpFormatter,
                                description="Creates sites/<name>/ with a running Umbraco site and an authenticated CLI.",
                                epilog=EXAMPLES)
    p.add_argument("-u", "--umbraco", default="17",
                   help='Umbraco version: major ("17"), exact ("17.7.0", "18.1.0-rc") or "latest". Default: 17 (latest stable 17.x)')
    p.add_argument("-c", "--cli", default="latest", help='Umbraco.Community.Cli version, exact or "latest" (includes prereleases). Default: latest')
    p.add_argument("--cli-source", help="Install the CLI from a local folder of .nupkg files (e.g. a local build). "
                                        "Use with --cli <ver> to pick the version in that folder.")
    p.add_argument("-n", "--name", help="Site folder / CLI profile name. Default: u<umbraco>-cli<cli>-<timestamp>")
    p.add_argument("-p", "--port", type=int, help="HTTPS port. Default: first free port from 44800")
    p.add_argument("--admin-email", default="admin@example.com")
    p.add_argument("--admin-password", default="Password1234!")
    p.add_argument("--no-default", action="store_true", help="Don't make this site's profile the default in the harness CLI config.")
    return p.parse_args()


# ---- versions ---------------------------------------------------------------
def resolve_cli_version(want, source):
    """Resolve the CLI version: from a local folder of .nupkg files, or from nuget.org."""
    if source:
        if want != "latest":
            return want
        found = [m.group(1) for f in source.glob("*.nupkg")
                 if (m := re.fullmatch(r"umbraco\.community\.cli\.(.+)\.nupkg", f.name, re.I))]
        return max(found, key=harness.version_key, default=None)
    versions = harness.nuget_versions("umbraco.community.cli")
    if want == "latest":
        return max(versions, key=harness.version_key, default=None)
    return want if want.lower() in versions else None


def free_port():
    """The first port from 44800 that is neither listening nor claimed by another site's site.env."""
    claimed = {s.port for s in harness.list_sites()}
    port = 44800
    while harness.port_in_use(port) or port in claimed:
        port += 1
    return port


# ---- site folder -------------------------------------------------------------
def write_site_files(site_dir, env):
    """Write site.env plus the helpers: the `umb` wrapper(s) and the start/stop scripts."""
    harness.write_env(site_dir / "site.env", env)
    name, dll, cfg = env["SITE_NAME"], Path(env["CLI_DLL"]).as_posix(), CLI_CONFIG.as_posix()

    # `umb` (bash): for macOS/Linux, and for Git Bash on Windows (the TEST-PLAN snippets are bash).
    # It pins this site's profile and adds the harness config unless the caller passes their own --config.
    sh = site_dir / "umb"
    harness.write_text(sh, f"""#!/usr/bin/env bash
# Umbraco.Community.Cli {env['CLI_VERSION']}, pinned to profile '{name}' ({env['HOST']}), harness config {cfg}.
for a in "$@"; do [[ "$a" == --config || "$a" == --config=* ]] && exec env UMBRACO_PROFILE="${{UMBRACO_PROFILE:-{name}}}" dotnet "{dll}" "$@"; done
UMBRACO_PROFILE="${{UMBRACO_PROFILE:-{name}}}" exec dotnet "{dll}" --config "{cfg}" "$@"
""")

    if WINDOWS:
        # `umb.cmd`: the same wrapper for PowerShell and cmd. (`=` is a delimiter in a for-set, so
        # `--config=x` arrives as `--config` and is still detected.)
        harness.write_text(site_dir / "umb.cmd", f"""@echo off
rem Umbraco.Community.Cli {env['CLI_VERSION']}, pinned to profile '{name}' ({env['HOST']}), harness config {cfg}.
setlocal
if not defined UMBRACO_PROFILE set "UMBRACO_PROFILE={name}"
for %%a in (%*) do if /i "%%~a"=="--config" goto own
dotnet "{Path(dll)}" --config "{CLI_CONFIG}" %*
exit /b %ERRORLEVEL%
:own
dotnet "{Path(dll)}" %*
exit /b %ERRORLEVEL%
""", newline="\r\n")

    # start.py / stop.py: thin stubs over harness.Site, so `python3 sites/<name>/start.py` works from anywhere.
    for verb, doc in (("start", "Start the site in the background (logs -> logs/site.log) and wait until Umbraco is running."),
                      ("stop", "Stop the site and anything left listening on its port.")):
        harness.write_text(site_dir / f"{verb}.py", f'''#!/usr/bin/env python3
"""{doc}"""
import sys
from pathlib import Path
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent.parent / "tools"))
import harness
harness.utf8_stdio()
harness.Site(HERE).{verb}()
''')

    if not WINDOWS:
        for f in ("umb", "start.py", "stop.py"):
            (site_dir / f).chmod(0o755)


# ---- API user ----------------------------------------------------------------
def create_api_user(host, admin_email, admin_password):
    """Sign in as the admin, then create an API user with client credentials.

    Uses the backoffice's authorization-code + PKCE flow. From v17 the code and tokens are kept in secure
    cookies and the JSON carries "[redacted]" placeholders, so we pass whatever we are given straight back
    and let the cookie jar do the rest (older versions get real values).

    :returns: `(user id, client secret)`.
    :raises SystemExit: when any step fails.
    """
    api = f"{host}/umbraco/management/api/v1"
    web = harness.Http()

    say(f"Signing in to the Management API as {admin_email}")
    status, _, _ = web.request("POST", f"{api}/security/back-office/login",
                               json_body={"username": admin_email, "password": admin_password})
    if status >= 400:
        die(f"Admin login failed (HTTP {status}).")

    verifier, challenge = harness.pkce_pair()
    redirect = f"{host}/umbraco/oauth_complete"
    query = urllib.parse.urlencode({"client_id": "umbraco-back-office", "response_type": "code", "redirect_uri": redirect,
                                    "code_challenge": challenge, "code_challenge_method": "S256", "scope": "offline_access"})
    status, headers, _ = web.request("GET", f"{api}/security/back-office/authorize?{query}")
    location = headers.get("Location") or ""
    code = urllib.parse.parse_qs(urllib.parse.urlsplit(location).query).get("code", [""])[0]
    if not code:
        die(f"No authorization code returned (HTTP {status}, redirect: {location or 'none'}).")

    status, _, body = web.request("POST", f"{api}/security/back-office/token", form={
        "grant_type": "authorization_code", "client_id": "umbraco-back-office", "redirect_uri": redirect,
        "code": code, "code_verifier": verifier})
    token = json.loads(body).get("access_token") if status == 200 else None
    if not token:
        die(f"Token exchange failed (HTTP {status}).")
    auth = {"Authorization": f"Bearer {token}"}

    say(f"Creating API user {API_USER_EMAIL} (Administrators + Sensitive data)")
    status, _, body = web.request("GET", f"{api}/user-group?take=100", headers=auth)
    groups = [{"id": g["id"]} for g in json.loads(body).get("items", []) if g.get("alias") in ("admin", "sensitiveData")] \
        if status == 200 else []
    if not groups:
        die("Couldn't read user groups.")

    status, headers, _ = web.request("POST", f"{api}/user", headers=auth, json_body={
        "email": API_USER_EMAIL, "userName": API_USER_EMAIL, "name": "CLI API User", "kind": "Api", "userGroupIds": groups})
    user_id = headers.get("Umb-Generated-Resource")
    if not user_id:
        die(f"Creating the API user failed (HTTP {status}; API users need Umbraco 15+).")

    secret = secrets.token_hex(24)
    status, _, _ = web.request("POST", f"{api}/user/{user_id}/client-credentials", headers=auth,
                               json_body={"clientId": API_CLIENT_ID, "clientSecret": secret})
    if status != 200:
        die(f"Adding client credentials failed (HTTP {status}).")
    return user_id, secret


# ---- main ----------------------------------------------------------------------
def main():
    harness.utf8_stdio()
    a = parse_args()
    shutil.which("dotnet") or die("'dotnet' is required but not installed.")
    cli_source = Path(a.cli_source).resolve() if a.cli_source else None
    if cli_source and not cli_source.is_dir():
        die(f"--cli-source {a.cli_source} is not a folder.")

    say("Resolving versions")
    umb_ver = harness.resolve_umbraco_version(a.umbraco) or die(f"Couldn't find Umbraco.Templates version matching '{a.umbraco}' on NuGet.")
    cli_ver = resolve_cli_version(a.cli, cli_source) or \
        die(f"Couldn't find Umbraco.Community.Cli version matching '{a.cli}'{f' in {cli_source}' if cli_source else ''}.")
    print(f"    Umbraco: {umb_ver}")
    print(f"    CLI:     {cli_ver}{f' (from {cli_source})' if cli_source else ''}")

    name = a.name or f"u{umb_ver}-cli{cli_ver}-{time.strftime('%Y%m%d-%H%M%S')}"
    re.fullmatch(r"[A-Za-z0-9._-]+", name) or die("Site name may only contain letters, digits, '.', '_' and '-'.")
    site_dir = SITES / name
    if site_dir.exists():
        die(f"{site_dir} already exists. Pick another --name or run python3 remove-site.py {name}")
    port = a.port or free_port()
    if harness.port_in_use(port):
        die(f"Port {port} is already in use.")
    host = f"https://localhost:{port}"

    if harness.run(["dotnet", "dev-certs", "https", "--check", "--trust"], capture_output=True).returncode:
        warn("No trusted HTTPS dev certificate. Run 'dotnet dev-certs https --trust' if the CLI reports TLS errors.")

    # ---- 1. scaffold -------------------------------------------------------
    # Templates live in a per-version custom hive so we never touch the globally installed ones.
    hive = ROOT / ".cache" / "templates" / umb_ver
    if not hive.is_dir():
        say(f"Installing Umbraco.Templates {umb_ver} (cached in .cache/templates)")
        if harness.run(["dotnet", "new", "install", f"Umbraco.Templates@{umb_ver}", "--debug:custom-hive", hive],
                       stdout=subprocess.DEVNULL).returncode:
            harness.remove_tree(hive)
            die("Installing Umbraco.Templates failed.")

    say(f"Scaffolding Umbraco {umb_ver} into sites/{name} (restore can take a few minutes)")
    (site_dir / "logs").mkdir(parents=True)
    if harness.run(["dotnet", "new", "umbraco", "-n", PROJECT, "--friendly-name", ADMIN_NAME, "--email", a.admin_email,
                    "--password", a.admin_password, "--development-database-type", "SQLite", "--debug:custom-hive", hive],
                   cwd=site_dir, stdout=subprocess.DEVNULL).returncode:
        die("dotnet new umbraco failed.")

    say("Building")
    build_log = site_dir / "logs" / "build.log"
    with open(build_log, "wb") as log:
        built = harness.run(["dotnet", "build", site_dir / PROJECT, "-nologo", "-v", "q"], stdout=log, stderr=log).returncode == 0
    if not built:
        print("\n".join(build_log.read_text(encoding="utf-8", errors="replace").splitlines()[-30:]))
        die(f"Build failed (full log: sites/{name}/logs/build.log)")

    # ---- 2. install the CLI -------------------------------------------------
    say(f"Installing Umbraco.Community.Cli {cli_ver} as a local tool")
    packages = harness.nuget_global_packages()
    cached = packages / "umbraco.community.cli" / cli_ver.lower()
    harness.run(["dotnet", "new", "tool-manifest"], cwd=site_dir, stdout=subprocess.DEVNULL)
    install = ["dotnet", "tool", "install", "Umbraco.Community.Cli", "--version", cli_ver]
    if cli_source:
        # Local builds often reuse a version number; drop the cached copy so we get the fresh one.
        harness.remove_tree(cached)
        install += ["--add-source", cli_source]
    if harness.run(install, cwd=site_dir, stdout=subprocess.DEVNULL).returncode:
        die(f"Installing Umbraco.Community.Cli {cli_ver} failed.")
    dll = next(iter(sorted(cached.glob("tools/**/Umbraco.Cli.dll"))), None) or \
        die(f"Couldn't locate Umbraco.Cli.dll for {cli_ver} in {packages}.")

    env = {"SITE_NAME": name, "UMBRACO_VERSION": umb_ver, "CLI_VERSION": cli_ver, "PORT": port, "HOST": host,
           "PROJECT": PROJECT, "CLI_DLL": dll}
    CLI_CONFIG.parent.mkdir(parents=True, exist_ok=True)
    write_site_files(site_dir, env)
    site = harness.Site(site_dir)

    # ---- 3. start + unattended install ------------------------------------
    say("Starting site (unattended install on first boot)")
    site.start()

    # ---- 4. admin sign-in -> API user ---------------------------------------
    user_id, secret = create_api_user(host, a.admin_email, a.admin_password)
    # Local throwaway test site, so plaintext is fine - handy when you need to redo `auth login`.
    creds = site_dir / "credentials.json"
    creds.write_text(json.dumps({"host": host, "admin": {"email": a.admin_email, "password": a.admin_password},
                                 "apiUser": {"id": user_id, "clientId": API_CLIENT_ID, "clientSecret": secret}}, indent=2),
                     encoding="utf-8")
    if not WINDOWS:
        os.chmod(creds, 0o600)

    # ---- 5. hook up the CLI -------------------------------------------------
    say(f"Logging the CLI in to profile '{name}'")
    if site.umb("auth", "login", "--profile", name, "--host", host, "--client-id", API_CLIENT_ID,
                "--client-secret", secret, "--output", "json").returncode:
        die("umbraco auth login failed.")
    default = not a.no_default
    if default:
        # alpha.12+ renamed 'auth use' to 'auth profile use'; try the new verb first.
        if site.umb("auth", "profile", "use", name, "--output", "json").returncode and \
                site.umb("auth", "use", name, "--output", "json").returncode:
            warn(f"Couldn't set '{name}' as the default profile.")
            default = False

    say("Running umbraco auth doctor")
    if site.umb("auth", "doctor", "--output", "human", capture=False).returncode:
        die("auth doctor reported a failure - see above.")

    umb = f"sites\\{name}\\umb.cmd" if WINDOWS else f"sites/{name}/umb"
    print(f"""
Ready!  sites/{name}

  Site:        {host}/umbraco   ({a.admin_email} / {a.admin_password})
  Umbraco:     {umb_ver}
  CLI:         {cli_ver}  (profile '{name}'{", now the default" if default else ""})

  {umb} <command>{' ' * max(1, 22 - len(umb))}run the CLI against this site, e.g. {umb} content list
  python3 sites/{name}/stop.py     stop the site
  python3 sites/{name}/start.py    start it again
  python3 remove-site.py {name}     stop, log out the profile and delete the folder""")


if __name__ == "__main__":
    main()
