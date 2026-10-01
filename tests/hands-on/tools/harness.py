"""The hands-on harness's CLI layer over umbraco-spawn-harness.

The generic part - scaffolding a site of any Umbraco version with SQLite and an unattended admin,
starting and stopping it, ports, processes, HTTP and NuGet lookups - lives in the umbraco-spawn-harness
package (https://github.com/worm-brain/umbraco-spawn-harness), pinned in ../pyproject.toml. This module
re-exports it, so every entry point keeps using `harness.<name>`, and adds what only this repository needs:

- `Site.umb`: running a site's pinned CLI with the harness's own config, like the generated `umb` wrappers;
- `Site.credentials`: the API user's client credentials (credentials.json), next to the base's admin.json;
- `SITES` / `CLI_CONFIG`: where this harness keeps its sites and its CLI profiles.

A site's CLI pin lives in cli.env (CLI_VERSION, CLI_DLL), the base's site.env stays the base's. Sites made
before the split have everything in site.env; both are read, so they keep working.

Run the scripts with plain `python3`: when the package isn't importable, this module re-runs the script
through `uv run --project tests/hands-on`, which installs the pinned version on first use.
"""
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent  # tests/hands-on

try:
    import umbraco_spawn_harness as spawn
except ImportError:
    # Not inside the harness's uv environment: re-run this script there. The marker stops a loop when the
    # environment exists but lacks the package.
    if os.environ.get("UMBRACO_HANDS_ON_UV"):
        raise SystemExit("error: umbraco-spawn-harness is missing from the uv environment; run: uv sync --project tests/hands-on")
    uv = shutil.which("uv")
    if not uv:
        raise SystemExit("error: the hands-on harness needs uv (https://docs.astral.sh/uv/) to install umbraco-spawn-harness")
    env = {**os.environ, "UMBRACO_HANDS_ON_UV": "1"}
    try:
        code = subprocess.run([uv, "run", "--quiet", "--project", str(ROOT), "python", sys.argv[0], *sys.argv[1:]],
                              env=env).returncode
    except KeyboardInterrupt:
        code = 130
    sys.exit(code)

from umbraco_spawn_harness import (  # noqa: E402,F401  (re-exported for the scripts)
    INSECURE, WINDOWS, Http, ManagementApi, SpawnError, claim_port, colour, die, free_port, get_text, heading, kill_tree,
    nuget_global_packages, nuget_versions, pid_alive, pkce_pair, port_in_use, port_owner_pids, read_env,
    release_port, remove_tree, resolve_umbraco_version, run, say, scaffold, spawn_detached, utf8_stdio,
    version_key, warn, write_env, write_text,
)

SITES = ROOT / "sites"
CLI_CONFIG = ROOT / ".cli" / "config.json"  # harness-local CLI config: profiles for test sites only


def _report_spawn_error(kind, error, tb):
    """Print a SpawnError from the base package as `error: <message>`, like `die`, instead of a traceback."""
    if issubclass(kind, SpawnError):
        print(f"{colour('1;31', 'error:', sys.stderr)} {error}", file=sys.stderr, flush=True)
        sys.exit(1)
    sys.__excepthook__(kind, error, tb)


sys.excepthook = _report_spawn_error


class Site(spawn.Site):
    """A throwaway test site under sites/<name>, created by new-site.py: a spawned site plus its CLI.

    :param where: a site name (`source`), or a path to its folder (`sites/source`).
    :raises SpawnError: when there's no site there.
    """

    def __init__(self, where):
        p = Path(where)
        super().__init__(p if (p / "site.env").exists() or len(p.parts) > 1 else SITES / str(where))
        cli_env = self.dir / "cli.env"
        if cli_env.exists():
            self.env.update(read_env(cli_env))

    def credentials(self):
        """The site's credentials.json: host, admin and API-user client credentials.

        :returns: the parsed JSON.
        """
        return json.loads((self.dir / "credentials.json").read_text(encoding="utf-8"))

    # --- CLI -----------------------------------------------------------
    def umb_argv(self, args):
        """The command line the `umb` wrapper runs: this site's pinned CLI plus the harness config.

        `--config <harness>/.cli/config.json` is added unless the caller passes their own `--config`.

        :param args: CLI arguments.
        :returns: the full argument list.
        """
        own = any(a == "--config" or str(a).startswith("--config=") for a in args)
        return ["dotnet", self.env["CLI_DLL"], *([] if own else ["--config", str(CLI_CONFIG)]), *args]

    def umb_env(self, overrides=None):
        """The environment the `umb` wrapper runs with.

        :param overrides: variables to set; a value of None removes the variable.
        :returns: the full environment. UMBRACO_PROFILE defaults to this site when unset or empty,
                  exactly like the wrapper's `${UMBRACO_PROFILE:-<site>}`.
        """
        env = dict(os.environ)
        for k, v in (overrides or {}).items():
            if v is None:
                env.pop(k, None)
            else:
                env[k] = v
        if not env.get("UMBRACO_PROFILE"):
            env["UMBRACO_PROFILE"] = self.name
        return env

    def umb(self, *args, input=None, env=None, capture=True):
        """Run this site's CLI, like `sites/<name>/umb <args>`.

        :param args: CLI arguments.
        :param input: text to pipe to stdin (e.g. a `--json-body -` document).
        :param env: environment overrides (see `umb_env`).
        :param capture: capture stdout/stderr (default) or let them through to the console.
        :returns: the `subprocess.CompletedProcess`.
        """
        kw = {"capture_output": True} if capture else {}
        if input is not None:
            kw["input"] = input
        return run(self.umb_argv([str(a) for a in args]), env=self.umb_env(env), **kw)


def list_sites():
    """Every site under sites/ (folders with a site.env), sorted by name."""
    return [Site(p.parent) for p in sorted(SITES.glob("*/site.env"))]
