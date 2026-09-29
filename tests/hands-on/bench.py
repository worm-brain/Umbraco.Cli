#!/usr/bin/env python3
"""bench.py - time real CLI commands end to end with hyperfine, per CLI build x Umbraco version.

Runs a fixed set of scenarios against the dev site (sites/dev, see dev-site.py) for every requested CLI
build and Umbraco version, then writes one results file per run to docs/performance/results/ (JSON, plus
a markdown summary table beside it, also printed). It runs locally only, like `dev-site.py test`: CI has
no live Umbraco instance (#139, #77).

  python3 bench.py --cli local --umbraco 17
  python3 bench.py --cli 0.1.0-alpha.6,local --umbraco 17,18
  python3 bench.py --scenario get,list --runs 20 --out-dir .cache/bench/scratch
  python3 bench.py --cli local --latency 25   also time each scenario through a proxy adding 25 ms per request
  python3 bench.py report      regenerate README.md's Performance section and docs/performance.md; times nothing

CLI builds (--cli, comma-separated):
  local          pack this checkout (pack-cli.py) and install that build
  <version>      a published version, or "latest"; installed from nuget.org plus, when they exist, the repo's
                 ./nupkg (release builds from `dotnet pack -o ./nupkg`) and tests/hands-on/nupkg, plus --source
Each build is installed side by side in .cache/bench/cli/<version> (`dotnet tool install --tool-path`) and
run through its `umbraco` shim, the way a user's installed tool runs.

Umbraco versions (--umbraco, comma-separated; a major, an exact version or "latest"): the dev site is
started with `dev-site.py up` when it is already on that exact version, and rebuilt with
`dev-site.py reset --umbraco <ver>` when it isn't (or always, with --reset), so every run times the same
fixture content. A reset deletes the dev site and builds it again; it stays on the last version run.

Per scenario and build:
  1. one smoke run with --verbose; if it fails, the scenario is recorded as failed for that build and skipped
  2. hyperfine: --warmup untimed runs, then --runs timed runs, with no shell (-N) and output discarded.
     This is the end-to-end time.
  3. --http-runs separate --verbose runs give the HTTP time. VerboseHttpHandler logs each response as
     `< 200 OK (12 ms)`, timed from sending the request to its response headers. bench.py timestamps each
     of those lines as it arrives, so each request is the interval [arrival - duration, arrival], and the
     HTTP time is the length of the union of the intervals: requests the CLI sends concurrently count
     once. The logged durations are whole milliseconds, truncated, so 0.5 ms is added to each.
  CLI overhead = end-to-end mean - HTTP mean: process start, config and auth, parsing, our own CPU,
  writing output, and reading response bodies (the handler's clock stops at the headers). hyperfine never
  times a --verbose run, so the logging cannot skew the end-to-end numbers. HTTP time includes opening
  the connection (TCP, TLS) on a process's first request. Only requests the build logs count: builds
  before 0.1.0-alpha.15 don't log the OAuth token exchange, and builds before alpha.14 have no token
  cache and exchange a token on every run, so for those that round trip is in the CLI overhead.

Latency (--latency <ms>): localhost answers a request in about a millisecond, which hides what a command's
request count costs against a real host. With --latency, each API scenario is also timed through
tools/latency_proxy.py, a local TCP proxy that delays everything the CLI sends by that many milliseconds, so
each request round trip gains about that much (#428). Those runs are separate result rows with `latencyMs`
set (0 on direct rows); --latency-only skips the direct runs. They pass --host (the proxy) and --token (a
fresh client-credentials token per scenario), because the CLI's stored credentials are tied to the host.
`version` makes no requests, so it has no latency run.

The CLI runs with every UMBRACO_* variable cleared, the dev site's API user in UMBRACO_HOST /
UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET, and --config naming a file that doesn't exist, so your own
profiles, defaults and allow-lists are never read. The token cache is the normal per-user one, so after
the smoke run each command reuses a cached token, as repeated commands do in real use.

The results file is docs/performance/results/<YYYY-MM-DD>_<HHMMSS>_<machine>.json (and .md). README.md
(section "Benchmarks") documents its fields. To change what is timed, edit SCENARIOS below.

`bench.py report` (tools/perf_report.py) reads every results file there and rewrites docs/performance.md and
the block between the `<!-- perf:start -->` and `<!-- perf:end -->` lines in the repository's README.md. It
needs neither hyperfine nor a site; `bench.py report --help` says what it quotes.
"""
import argparse
import collections
import contextlib
import json
import os
import platform
import re
import shlex
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "tools"))
import harness  # noqa: E402
import perf_report  # noqa: E402
from harness import ROOT, SITES, WINDOWS, die, heading, say, warn  # noqa: E402

PY = sys.executable
REPO = ROOT.parent.parent
PACKAGE = "Umbraco.Community.Cli"
SITE_DIR = SITES / "dev"
BENCH = ROOT / ".cache" / "bench"  # git-ignored: installed builds, raw hyperfine exports, scratch output
TOOLS = BENCH / "cli"
RESULTS = REPO / "docs" / "performance" / "results"
SCHEMA_VERSION = 1

# The default scenarios: the ones #410 names for the profiling spike (#407). Edit this table to change what
# is timed. argv follows `umbraco`; {home} (and the other ids in sites/dev/work/ids.json) name fixture items,
# {fixtures} is tests/hands-on/fixtures, {work} a scratch folder and {config} the empty CLI config. `http`
# is False for a command that makes no requests, so it has no --verbose run (--version must stand alone).
Scenario = collections.namedtuple("Scenario", "name about argv http")
API = ["--output", "json", "--config", "{config}"]
SCENARIOS = [
    Scenario("version", "startup only: no config, no HTTP", ["--version"], False),
    Scenario("get", "content get of the fixture home page", ["content", "get", "{home}", *API], True),
    Scenario("list", "a large list: every data type, each read by id", ["data-type", "list", "--all", *API], True),
    Scenario("content-export", "the whole fixture content tree to a file",
             ["content", "export", "--out", "{work}/content-export.json", *API], True),
    Scenario("schema-diff", "the fixture schema snapshot against the site", ["schema", "diff", "{fixtures}/schema.json", *API],
             True),
]

# The two VerboseHttpHandler lines bench.py reads: a request (`> GET https://...`) and a response
# (`< 200 OK (12 ms)`). Headers and bodies are logged on other `> ` / `< ` lines, which neither matches.
REQUEST_LINE = re.compile(r"^> (GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS) ")
RESPONSE_LINE = re.compile(r"^< \d{3}\b.*\((\d+(?:\.\d+)?) ms\)$")

HYPERFINE_HINT = """hyperfine was not found{where}. Install it, then run bench.py again:
  Windows:  winget install --id sharkdp.hyperfine -e    (or: scoop install hyperfine, choco install hyperfine)
  macOS:    brew install hyperfine
  Linux:    apt install hyperfine, dnf install hyperfine, pacman -S hyperfine, or cargo install hyperfine
  Other:    https://github.com/sharkdp/hyperfine#installation
Or point bench.py at it with --hyperfine <path> or HYPERFINE=<path>. Nothing was run."""

Cli = collections.namedtuple("Cli", "label version commit exe dotnet")


def parse_args():
    p = argparse.ArgumentParser(prog="bench.py", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter,
                                epilog="scenarios:\n" + "\n".join(f"  {s.name:<15} {s.about}" for s in SCENARIOS))
    p.add_argument("--cli", default="local", help='CLI builds, comma-separated: "local", versions or "latest". Default: local')
    p.add_argument("--umbraco", default="17", help='Umbraco versions, comma-separated: majors, exact versions or "latest". Default: 17')
    p.add_argument("--scenario", help="Only these scenarios, comma-separated. Default: all")
    p.add_argument("--warmup", type=int, default=3, help="hyperfine warm-up runs per command. Default: 3")
    p.add_argument("--runs", type=int, default=10, help="hyperfine timed runs per command. Default: 10")
    p.add_argument("--http-runs", type=int, default=5, help="Separate --verbose runs per command for the HTTP time. Default: 5")
    p.add_argument("--reset", action="store_true", help="Rebuild the dev site even when it is already on the Umbraco version")
    p.add_argument("--source", action="append", default=[], help="Extra NuGet source for published builds (repeatable)")
    p.add_argument("--hyperfine", help="Path to hyperfine. Default: $HYPERFINE, then PATH")
    p.add_argument("--machine", help="Machine label for the results file name. Default: OS and CPU model, e.g. windows-amd-ryzen-9-7950x")
    p.add_argument("--out-dir", type=Path, default=RESULTS, help="Where to write the results. Default: docs/performance/results")
    p.add_argument("--latency", type=int, metavar="MS",
                   help="Also time each API scenario through a local proxy that adds MS milliseconds to every request. "
                        "Default: direct runs only")
    p.add_argument("--latency-only", action="store_true", help="With --latency: skip the direct runs")
    p.add_argument("--note", help='A note stored with the results and shown on docs/performance.md, such as "busy machine: '
                                  'a build ran alongside". Default: none')
    a = p.parse_args()
    if a.runs < 2 or a.warmup < 0 or a.http_runs < 1:
        p.error("--runs must be at least 2 (for a spread), --warmup at least 0 and --http-runs at least 1")
    if a.latency is not None and a.latency < 1:
        p.error("--latency must be at least 1 ms (leave it out for direct runs only)")
    if a.latency_only and a.latency is None:
        p.error("--latency-only needs --latency <ms>")
    return a


def split_list(text):
    """Split a comma-separated option into its distinct, non-empty values, keeping their order.

    :param text: the option value.
    :returns: the values.
    """
    return list(dict.fromkeys(v.strip() for v in text.split(",") if v.strip()))


# ---------------------------------------------------------------- tools ------
def find_hyperfine(explicit):
    """Locate a working hyperfine: --hyperfine, then $HYPERFINE, then PATH (and winget's Links folder on Windows).

    :param explicit: the --hyperfine value, or None.
    :returns: `(path, version)`.
    :raises SystemExit: with install instructions when there is no working hyperfine.
    """
    given = explicit or os.environ.get("HYPERFINE")
    candidates = [given] if given else [shutil.which("hyperfine")]
    if not given and WINDOWS:
        # winget adds this folder to PATH, but a shell opened before the install doesn't have it yet.
        candidates.append(str(Path(os.environ.get("LOCALAPPDATA", "")) / "Microsoft" / "WinGet" / "Links" / "hyperfine.exe"))
    for path in filter(None, candidates):
        try:
            res = harness.run([path, "--version"], capture_output=True)
        except OSError:
            continue
        if res.returncode == 0 and res.stdout.startswith("hyperfine"):
            return path, res.stdout.split()[-1]
    die(HYPERFINE_HINT.format(where=f" at {given}" if given else " on PATH"))


def dotnet_runtimes():
    """Installed shared runtimes, from `dotnet --list-runtimes`.

    :returns: a dict of framework name (`Microsoft.NETCore.App`) to its installed versions.
    """
    out = collections.defaultdict(list)
    for line in harness.run(["dotnet", "--list-runtimes"], capture_output=True).stdout.splitlines():
        parts = line.split()
        if len(parts) >= 2:
            out[parts[0]].append(parts[1])
    return out


def runtime_for(tool_dir, runtimes):
    """The .NET runtime an installed CLI build runs on.

    Reads the build's runtimeconfig.json and applies the host's default roll-forward rule (Minor): the
    lowest installed major.minor at or above the target, same major, at its latest patch. A build that sets
    another rollForward policy is reported as its target plus that policy rather than guessed.

    :param tool_dir: the build's --tool-path folder.
    :param runtimes: the result of `dotnet_runtimes()`.
    :returns: a version such as "9.0.20", or a description when it can't be resolved.
    """
    config = next(iter(sorted(tool_dir.glob(".store/**/Umbraco.Cli.runtimeconfig.json"))), None)
    if config is None:
        return None
    options = json.loads(config.read_text(encoding="utf-8"))["runtimeOptions"]
    framework = options.get("framework") or next(iter(options.get("frameworks", [])), {})
    want, policy = framework.get("version", "0.0.0"), options.get("rollForward", "Minor")
    if policy.lower() != "minor":
        return f"{want} (rollForward {policy})"
    major = want.split(".")[0]
    fits = [v for v in runtimes.get(framework.get("name"), [])
            if "-" not in v and v.split(".")[0] == major and harness.version_key(v) >= harness.version_key(want)]
    if not fits:
        return f"{want} (not installed)"
    lowest_minor = min(int(v.split(".")[1]) for v in fits)
    return max((v for v in fits if int(v.split(".")[1]) == lowest_minor), key=harness.version_key)


def git_commit():
    """This checkout's short commit, with `-dirty` when src/ has uncommitted changes (as pack-cli.py reports).

    :returns: e.g. "7d1a43a" or "7d1a43a-dirty".
    """
    commit = harness.run(["git", "-C", REPO, "rev-parse", "--short", "HEAD"], capture_output=True).stdout.strip() or "unknown"
    dirty = harness.run(["git", "-C", REPO, "status", "--porcelain", "--", "src"], capture_output=True).stdout.strip()
    return f"{commit}-dirty" if dirty else commit


def installed_version(tool_dir):
    """The Umbraco.Community.Cli version installed in a --tool-path folder, or None.

    :param tool_dir: the folder.
    :returns: the version string as `dotnet tool list` prints it.
    """
    if not tool_dir.is_dir():
        return None
    out = harness.run(["dotnet", "tool", "list", "--tool-path", tool_dir], capture_output=True).stdout
    for line in out.splitlines():
        cols = line.split()
        if len(cols) >= 2 and cols[0].lower() == PACKAGE.lower():
            return cols[1]
    return None


def install_cli(label, sources, runtimes):
    """Install one CLI build side by side in .cache/bench/cli/<version> (reusing an earlier install).

    :param label: "local", "latest" or a version.
    :param sources: extra NuGet sources for published builds.
    :param runtimes: the result of `dotnet_runtimes()`.
    :returns: a `Cli`.
    :raises SystemExit: when packing, resolving or installing fails.
    """
    if label == "local":
        res = harness.run([PY, "pack-cli.py"], cwd=ROOT, capture_output=True)
        sys.stderr.write(res.stderr)
        if res.returncode:
            sys.exit(1)
        version, feeds, commit = res.stdout.strip().splitlines()[-1], [ROOT / "nupkg"], git_commit()
        # Every pack has a new version, so earlier local installs are dead weight.
        for old in TOOLS.glob("0.1.0-local.*"):
            if old.name != version:
                harness.remove_tree(old)
    else:
        version = label
        if label == "latest":
            version = max(harness.nuget_versions(PACKAGE), key=harness.version_key, default=None) or \
                die(f"No {PACKAGE} versions on nuget.org.")
        feeds = [d for d in (REPO / "nupkg", ROOT / "nupkg") if d.is_dir()] + sources
        commit = None

    tool_dir = TOOLS / version
    exe = tool_dir / ("umbraco.exe" if WINDOWS else "umbraco")
    if exe.exists() and (installed_version(tool_dir) or "").lower() == version.lower():
        say(f"CLI {version}: already installed")
    else:
        say(f"CLI {version}: installing into {tool_dir.relative_to(ROOT).as_posix()}")
        harness.remove_tree(tool_dir)
        cmd = ["dotnet", "tool", "install", PACKAGE, "--tool-path", tool_dir, "--version", version]
        for feed in feeds:
            cmd += ["--add-source", feed]
        res = harness.run(cmd, capture_output=True)
        if res.returncode or not exe.exists():
            die(f"Installing {PACKAGE} {version} failed:\n{(res.stdout + res.stderr).strip()}")
    return Cli(label, version, commit, exe, runtime_for(tool_dir, runtimes))


# ---------------------------------------------------------------- machine ----
def cpu_name():
    """The CPU model name, e.g. "AMD Ryzen 9 7950X 16-Core Processor"."""
    try:
        if WINDOWS:
            import winreg
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"HARDWARE\DESCRIPTION\System\CentralProcessor\0") as key:
                return str(winreg.QueryValueEx(key, "ProcessorNameString")[0]).strip()
        if sys.platform == "darwin":
            return harness.run(["sysctl", "-n", "machdep.cpu.brand_string"], capture_output=True).stdout.strip()
        for line in Path("/proc/cpuinfo").read_text(encoding="utf-8").splitlines():
            if line.startswith("model name"):
                return line.split(":", 1)[1].strip()
    except OSError:
        pass
    return platform.processor() or "unknown"


def memory_gib():
    """Physical memory in GiB (one decimal), or None when the OS won't say."""
    try:
        if WINDOWS:
            import ctypes

            class MemoryStatus(ctypes.Structure):  # MEMORYSTATUSEX
                _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong)] + \
                           [(f, ctypes.c_ulonglong) for f in ("total", "avail", "pageTotal", "pageAvail", "virtTotal",
                                                              "virtAvail", "extAvail")]
            status = MemoryStatus()
            status.dwLength = ctypes.sizeof(status)
            if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
                return None
            total = status.total
        else:
            total = os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES")
    except (OSError, ValueError, AttributeError):
        return None
    return round(total / 2 ** 30, 1)


def os_name():
    """The OS and its version, e.g. "Windows 11 (10.0.26200)", "macOS 14.5" or "Ubuntu 24.04 LTS (kernel 6.8.0)"."""
    if WINDOWS:
        return f"Windows {platform.release()} ({platform.version()})"
    if sys.platform == "darwin":
        return f"macOS {platform.mac_ver()[0]}"
    try:
        pretty = platform.freedesktop_os_release().get("PRETTY_NAME")  # Python 3.10+
    except (OSError, AttributeError):
        pretty = None
    return f"{pretty or 'Linux'} (kernel {platform.release()})"


def machine_info(label):
    """The machine stamp every results file carries.

    :param label: --machine, or None for one made from the OS and CPU model.
    :returns: a dict: label, cpu, logicalCores, memoryGiB, os, arch.
    """
    cpu = cpu_name()
    if not label:
        system = {"Windows": "windows", "Darwin": "macos"}.get(platform.system(), platform.system().lower())
        # "AMD Ryzen 9 7950X 16-Core Processor" -> "amd-ryzen-9-7950x"; "Intel(R) Core(TM) i7-8700 CPU @ 3.20GHz" -> "intel-core-i7-8700".
        model = re.sub(r"\((r|tm)\)|\bcpu\b|\bprocessor\b|\b\d+-core\b|@.*$", " ", cpu, flags=re.I)
        label = f"{system} {model}"
    label = re.sub(r"[^a-z0-9]+", "-", label.lower()).strip("-")
    return {"label": label, "cpu": cpu, "logicalCores": os.cpu_count(), "memoryGiB": memory_gib(), "os": os_name(),
            "arch": platform.machine()}


# ---------------------------------------------------------------- site -------
def prepare_site(version, reset):
    """Get the dev site running on an exact Umbraco version with the fixture content.

    :param version: the exact Umbraco version.
    :param reset: rebuild the site even when it is already on that version.
    :returns: the dev `harness.Site`.
    :raises SystemExit: when dev-site.py fails.
    """
    env_file = SITE_DIR / "site.env"
    current = harness.read_env(env_file).get("UMBRACO_VERSION") if env_file.exists() else None
    verb = "reset" if reset or (current and current.lower() != version.lower()) else "up"
    heading(f"Dev site on Umbraco {version} (dev-site.py {verb})")
    if harness.run([PY, "dev-site.py", verb, "--umbraco", version], cwd=ROOT).returncode:
        sys.exit(1)
    return harness.Site("dev")


def placeholders(site):
    """The values the scenario argv placeholders stand for on this site.

    :param site: the dev Site.
    :returns: a dict of placeholder name to text.
    :raises SystemExit: when the site has no fixture ids (tools/build_site.py didn't finish).
    """
    ids_file = site.dir / "work" / "ids.json"
    if not ids_file.exists():
        die(f"{ids_file} is missing: the fixture build didn't finish. Rebuild with: python3 bench.py --reset ...")
    work = BENCH / "work"
    work.mkdir(parents=True, exist_ok=True)
    config = work / "no-config.json"  # never created: the CLI finds no profiles, so the UMBRACO_* credentials apply
    config.unlink(missing_ok=True)
    ids = json.loads(ids_file.read_text(encoding="utf-8"))
    fill = {k: v for k, v in ids.items() if isinstance(v, str)}
    fill.update(fixtures=(ROOT / "fixtures").as_posix(), work=work.as_posix(), config=config.as_posix())
    return fill


def cli_env(site):
    """The environment every CLI run gets: no UMBRACO_* from your shell, and the dev site's API user.

    :param site: the dev Site.
    :returns: the full environment.
    """
    creds = site.credentials()["apiUser"]
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith("UMBRACO_")}
    env.update(UMBRACO_HOST=site.host, UMBRACO_CLIENT_ID=creds["clientId"], UMBRACO_CLIENT_SECRET=creds["clientSecret"])
    return env


# ---------------------------------------------------------------- latency ----
class LatencyProxy:
    """tools/latency_proxy.py in front of the dev site, for the --latency runs; stopped when the `with` ends.

    :param site: the dev Site (its port is the proxy's target).
    :param delay_ms: milliseconds added to every chunk the CLI sends.
    """

    def __init__(self, site, delay_ms):
        self.site, self.delay_ms, self.proc, self.host = site, delay_ms, None, None

    def __enter__(self):
        cmd = [PY, str(ROOT / "tools" / "latency_proxy.py"), "--target-port", str(self.site.port),
               "--delay", str(self.delay_ms)]
        self.proc = subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, text=True)
        ready = self.proc.stdout.readline().split()  # "listening <port>", or nothing when it failed to start
        if ready[:1] != ["listening"]:
            self.proc.kill()
            die(f"The latency proxy didn't start (exit {self.proc.wait()}).")
        # Same scheme and host name as the site, so its localhost certificate matches; only the port differs.
        self.host = re.sub(r":\d+$", f":{ready[1]}", self.site.host.rstrip("/"))
        say(f"Latency proxy: {self.host} -> port {self.site.port}, +{self.delay_ms} ms per request")
        return self

    def __exit__(self, *_):
        self.proc.kill()
        self.proc.wait()

    def via(self):
        """The options that send one CLI run through the proxy: --host plus a fresh bearer token.

        A token from the site itself, because the CLI's credentials (and its token cache) are tied to the host
        they are for. The dev site's tokens live 300 s, so each scenario asks for its own.

        :returns: the extra argv.
        :raises SystemExit: when the site doesn't issue a token.
        """
        creds = self.site.credentials()["apiUser"]
        status, _, body = harness.Http().request(
            "POST", f"{self.site.host.rstrip('/')}/umbraco/management/api/v1/security/back-office/token",
            form={"grant_type": "client_credentials", "client_id": creds["clientId"], "client_secret": creds["clientSecret"]})
        token = json.loads(body).get("access_token") if status == 200 else None
        return ["--host", self.host, "--token", token or die(f"The dev site didn't issue a token (HTTP {status}).")]


def expand(argv, fill):
    """Fill a scenario's {placeholders}.

    :param argv: the scenario argv.
    :param fill: the result of `placeholders()`.
    :returns: the argv with every placeholder replaced.
    :raises SystemExit: for an unknown placeholder.
    """
    def one(m):
        if m.group(1) not in fill:
            die(f"Scenario placeholder {m.group(0)} is not one of: {', '.join(sorted(fill))}")
        return fill[m.group(1)]
    return [re.sub(r"\{(\w+)\}", one, arg) for arg in argv]


# ---------------------------------------------------------------- timing -----
def union_ms(spans):
    """Total length of a set of time intervals, counting overlaps once.

    :param spans: `(start, end)` pairs in seconds.
    :returns: the covered time in milliseconds.
    """
    total, reach = 0.0, None
    for start, end in sorted(spans):
        if reach is None or start >= reach:
            total, reach = total + end - start, end
        elif end > reach:
            total, reach = total + end - reach, end
    return total * 1000


def verbose_run(argv, env, http):
    """Run a command once, with --verbose when it makes requests, and measure its HTTP time from the log.

    :param argv: the full command line.
    :param env: its environment.
    :param http: whether the command makes requests (add --verbose and read the log).
    :returns: `(exit code, HTTP ms or None when requests were logged but no timings, request count,
              the error: the CLI's error message, or its last stderr lines that aren't request logging)`.
    """
    cmd = [str(a) for a in argv] + (["--verbose"] if http else [])
    proc = subprocess.Popen(cmd, env=env, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    spans, requests, other = [], 0, collections.deque(maxlen=40)
    for raw in proc.stderr:  # the handler logs each response line as it happens, so its arrival time is its end
        arrived = time.perf_counter()
        line = raw.decode("utf-8", "replace").rstrip()
        response = RESPONSE_LINE.match(line)
        if response:
            logged = response.group(1)
            # Whole milliseconds are truncated, so add the expected half; a build that logs fractions is taken as is.
            took = (float(logged) + (0.5 if "." not in logged else 0)) / 1000
            spans.append((arrived - took, arrived))
        elif REQUEST_LINE.match(line):
            requests += 1
        elif line and not line.startswith(("> ", "< ")):
            other.append(line)
    code = proc.wait()
    http_ms = union_ms(spans) if spans or not requests else None
    # A JSON error envelope spans several lines; its message says more than the last of them.
    message = re.search(r'"message":\s*("(?:[^"\\]|\\.)*")', "\n".join(other))
    error = json.loads(message.group(1)) if message else " ".join(list(other)[-3:])
    return code, http_ms, requests, error[:300]


def hyperfine_run(hyperfine, name, argv, env, a, export):
    """Time a command with hyperfine and read back its exported statistics.

    :param hyperfine: the hyperfine path.
    :param name: the scenario name (hyperfine's --command-name).
    :param argv: the full command line.
    :param env: its environment.
    :param a: the parsed options (warmup, runs).
    :param export: where hyperfine writes its JSON.
    :returns: a dict of mean, stddev, median, min, max and times, in milliseconds; None when hyperfine fails.
    """
    # -N runs the command without a shell; hyperfine splits the string with POSIX rules, which shlex.join
    # quotes for (Windows backslashes survive inside the single quotes).
    cmd = [hyperfine, "-N", "--warmup", a.warmup, "--runs", a.runs, "--export-json", export, "--command-name", name,
           shlex.join(str(x) for x in argv)]
    if harness.run(cmd, env=env).returncode:
        return None
    stats = json.loads(Path(export).read_text(encoding="utf-8"))["results"][0]

    def ms(v):
        return None if v is None else round(v * 1000, 2)
    return {k: ms(stats.get(k)) for k in ("mean", "stddev", "median", "min", "max")} | \
        {"times": [ms(t) for t in stats.get("times", [])]}


def bench(scenario, cli, umbraco, fill, env, hyperfine, a, raw_dir, proxy=None):
    """Run one scenario for one CLI build on the current site: smoke run, hyperfine, then the HTTP runs.

    :param scenario: the Scenario.
    :param cli: the Cli build.
    :param umbraco: the site's exact Umbraco version.
    :param fill: the placeholder values.
    :param env: the CLI environment.
    :param hyperfine: the hyperfine path.
    :param a: the parsed options.
    :param raw_dir: where hyperfine's own exports go.
    :param proxy: a running LatencyProxy to time the command through, or None for a direct run.
    :returns: one result row (see README.md, "Benchmarks").
    """
    latency = proxy.delay_ms if proxy else 0
    row = {"scenario": scenario.name, "command": " ".join(["umbraco", *scenario.argv]),
           "cli": {"label": cli.label, "version": cli.version, "commit": cli.commit, "dotnet": cli.dotnet},
           "umbraco": umbraco, "latencyMs": latency, "status": "ok"}
    argv = [cli.exe, *expand(scenario.argv, fill), *(proxy.via() if proxy else [])]
    say(f"{scenario.name}: CLI {cli.label}{f' ({cli.version})' if cli.label != cli.version else ''}, Umbraco {umbraco}"
        f"{f', +{latency} ms latency' if latency else ''}")

    code, _, _, error = verbose_run(argv, env, scenario.http)  # smoke run: also warms the token cache
    if code:
        warn(f"{scenario.name} failed on CLI {cli.version} (exit {code}), so it is skipped: {error}")
        return {**row, "status": "failed", "error": f"exit {code}: {error}"}

    export = raw_dir / f"umbraco-{umbraco}_cli-{cli.version}_{scenario.name}{f'_latency-{latency}' if latency else ''}.json"
    timing = hyperfine_run(hyperfine, scenario.name, argv, env, a, export)
    if timing is None:
        return {**row, "status": "failed", "error": "hyperfine reported a failure (see its output above)"}

    samples = [verbose_run(argv, env, scenario.http) for _ in range(a.http_runs)] if scenario.http else []
    if any(s[0] for s in samples):
        return {**row, "status": "failed", "error": "a --verbose run failed after the timed runs"}
    http = [s[1] for s in samples]
    http_ms = 0.0 if not scenario.http else (None if None in http else round(sum(http) / len(http), 2))
    return {**row, "endToEndMs": timing, "httpMs": http_ms,
            "cliOverheadMs": None if http_ms is None else round(timing["mean"] - http_ms, 2),
            "requests": round(sum(s[2] for s in samples) / len(samples)) if samples else 0}


# ---------------------------------------------------------------- report -----
def fmt(v):
    """A millisecond value for the table: one decimal, or "n/a"."""
    return "n/a" if v is None else f"{v:.1f}"


def summary_markdown(doc):
    """The markdown summary of a results document: its stamp, then one table row per result.

    :param doc: the results document.
    :returns: the markdown text.
    """
    m, h = doc["machine"], doc["hyperfine"]
    builds = {}
    for r in doc["results"]:
        c = r["cli"]
        builds.setdefault(c["version"], f"`{c['version']}`" + (f" (commit {c['commit']})" if c["commit"] else "") +
                          f", .NET {c['dotnet'] or 'unknown'}")
    lines = [f"# CLI benchmark, {doc['date'][:10]}", "",
             f"- **Date:** {doc['date']}",
             f"- **Machine:** {m['cpu']}, {m['logicalCores']} logical cores, {m['memoryGiB']} GiB RAM; {m['os']}, {m['arch']}",
             f"- **CLI builds:** {'; '.join(builds.values())}",
             f"- **Method:** hyperfine {h['version']} ({h['warmup']} warm-up + {h['runs']} timed runs per command) against "
             f"the tests/hands-on dev site and its fixture content. HTTP time is the mean of {doc['httpRuns']} separate "
             "`--verbose` runs, and CLI overhead is end-to-end minus HTTP (tests/hands-on/README.md, Benchmarks).",
             *([f"- **Note:** {doc['note']}"] if doc.get("note") else [])]
    # Direct rows first, then one table per injected latency, so the two are never read as one series.
    for latency in sorted({r.get("latencyMs", 0) for r in doc["results"]}):
        if latency:
            lines += ["", f"Through the latency proxy, +{latency} ms per request:"]
        lines += ["",
                  "| Scenario | CLI | Umbraco | End-to-end ms (mean +/- sd) | HTTP ms | CLI overhead ms | Requests |",
                  "|---|---|---|--:|--:|--:|--:|"]
        for r in (r for r in doc["results"] if r.get("latencyMs", 0) == latency):
            cli = r["cli"]["version"]
            if r["status"] != "ok":
                error = r["error"] if len(r["error"]) <= 100 else r["error"][:97] + "..."
                lines.append(f"| {r['scenario']} | {cli} | {r['umbraco']} | failed ({error.replace('|', '/')}) | | | |")
                continue
            e2e = r["endToEndMs"]
            lines.append(f"| {r['scenario']} | {cli} | {r['umbraco']} | {fmt(e2e['mean'])} +/- {fmt(e2e['stddev'])} | "
                         f"{fmt(r['httpMs'])} | {fmt(r['cliOverheadMs'])} | {r['requests']} |")
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------- main -------
def main():
    harness.utf8_stdio()
    if sys.argv[1:2] == ["report"]:  # the one subcommand: render the committed results; it times nothing
        perf_report.main(sys.argv[2:], SCENARIOS)
        return
    a = parse_args()

    # ---- check everything before running anything --------------------------
    hyperfine, hyperfine_version = find_hyperfine(a.hyperfine)
    shutil.which("dotnet") or die("'dotnet' is required but not installed.")
    known = {s.name: s for s in SCENARIOS}
    wanted = split_list(a.scenario) if a.scenario else list(known)
    unknown = [n for n in wanted if n not in known]
    if unknown:
        die(f"Unknown scenario(s): {', '.join(unknown)}. Known: {', '.join(known)}")
    scenarios = [known[n] for n in wanted]
    say("Resolving Umbraco versions")
    umbracos = []
    for want in split_list(a.umbraco):
        exact = harness.resolve_umbraco_version(want) or die(f"No Umbraco.Templates version matches '{want}' on nuget.org.")
        print(f"    {want} -> {exact}")
        umbracos.append(exact)

    started = datetime.now(timezone.utc).replace(microsecond=0)
    machine = machine_info(a.machine)
    stem = f"{started:%Y-%m-%d_%H%M%S}_{machine['label']}"
    raw_dir = BENCH / "runs" / stem
    raw_dir.mkdir(parents=True, exist_ok=True)

    heading("CLI builds")
    runtimes = dotnet_runtimes()
    clis = [install_cli(label, a.source, runtimes) for label in split_list(a.cli)]

    # ---- the matrix: each Umbraco version (one site rebuild each), then every build and scenario ----
    rows = []
    for umbraco in umbracos:
        site = prepare_site(umbraco, a.reset)
        fill, env = placeholders(site), cli_env(site)
        heading(f"Timing on Umbraco {umbraco}")
        with contextlib.ExitStack() as stack:
            proxy = stack.enter_context(LatencyProxy(site, a.latency)) if a.latency else None
            for cli in clis:
                for scenario in scenarios:
                    if not a.latency_only:
                        rows.append(bench(scenario, cli, umbraco, fill, env, hyperfine, a, raw_dir))
                    if proxy and scenario.http:  # a command with no requests has nothing to delay
                        rows.append(bench(scenario, cli, umbraco, fill, env, hyperfine, a, raw_dir, proxy))

    # ---- results: JSON for tools (#411), markdown for people ----------------
    doc = {"schemaVersion": SCHEMA_VERSION, "date": started.isoformat().replace("+00:00", "Z"), "machine": machine,
           "hyperfine": {"version": hyperfine_version, "warmup": a.warmup, "runs": a.runs}, "httpRuns": a.http_runs,
           "note": a.note, "results": rows}
    out_dir = a.out_dir.resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    # One line per `times` array rather than one per number, so the file stays short and diffs stay readable.
    text = re.sub(r"\[\s+([-\d.,\s]+?)\s+\]", lambda m: f"[{' '.join(m.group(1).split())}]", json.dumps(doc, indent=2))
    harness.write_text(out_dir / f"{stem}.json", text + "\n")
    markdown = summary_markdown(doc)
    harness.write_text(out_dir / f"{stem}.md", markdown)

    heading("Summary")
    print(markdown)
    shown = out_dir.relative_to(REPO).as_posix() if out_dir.is_relative_to(REPO) else out_dir.as_posix()
    print(f"Results: {shown}/{stem}.json (+ .md). The dev site is still running: python3 dev-site.py down")
    failed = sum(r["status"] != "ok" for r in rows)
    if failed:
        # Usually an older build without that command or snapshot format; the other rows are still valid.
        warn(f"{failed} of {len(rows)} results failed (marked in the table); the results were still written.")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
