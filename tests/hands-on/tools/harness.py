"""Shared, cross-platform plumbing for the hands-on harness (macOS, Linux and Windows).

Every entry point (preflight.py, setup-round.py, new-site.py, remove-site.py, pack-cli.py and the tools/*.py
scripts) imports this module, so the platform differences live in one place:

- running a site's pinned CLI (`Site.umb`), with the same rules as the generated `umb` / `umb.cmd` wrappers;
- starting a site detached from the caller and stopping its whole process tree (`Site.start`, `Site.stop`);
- port and process checks without `lsof` / `pkill` (`port_in_use`, `pid_alive`, `kill_tree`);
- HTTPS calls with cookies and no automatic redirects, without `curl` (`Http`);
- NuGet version lookups and ordering, without `jq` / `sort -V` (`nuget_versions`, `version_key`).

Standard library only, so the harness needs nothing beyond Python 3.9+ and .NET.
"""
import base64
import hashlib
import http.cookiejar
import json
import os
import re
import secrets
import shlex
import shutil
import signal
import socket
import ssl
import stat
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent  # tests/hands-on
SITES = ROOT / "sites"
CLI_CONFIG = ROOT / ".cli" / "config.json"  # harness-local CLI config: profiles for test sites only
WINDOWS = os.name == "nt"
# Test sites use the ASP.NET Core dev certificate; skip verification rather than depend on the trust store.
INSECURE = ssl._create_unverified_context()


# ---------------------------------------------------------------- console --
def utf8_stdio():
    """Make stdout/stderr UTF-8 so fixture names (Danish text) and ledger emoji print on any platform.

    On Windows, piped output otherwise uses the ANSI code page and raises UnicodeEncodeError on emoji.
    Also switches on ANSI colour handling in older Windows consoles.
    """
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    if WINDOWS:
        os.system("")  # enables virtual-terminal (ANSI) processing for this console


def colour(code, text, stream):
    return f"\033[{code}m{text}\033[0m" if stream.isatty() else text


def say(msg):
    """Print a progress line (`==> msg`)."""
    print(f"{colour('1;36', '==>', sys.stdout)} {msg}", flush=True)


def heading(msg):
    """Print a section heading (`### msg`), used by setup-round.py between phases."""
    print("\n" + colour("1;35", f"### {msg}", sys.stdout), flush=True)


def warn(msg):
    """Print a warning to stderr."""
    print(f"{colour('1;33', 'warn:', sys.stderr)} {msg}", file=sys.stderr, flush=True)


def die(msg, code=1):
    """Print an error to stderr and exit.

    :param msg: the error message.
    :param code: the process exit code (default 1).
    :raises SystemExit: always.
    """
    print(f"{colour('1;31', 'error:', sys.stderr)} {msg}", file=sys.stderr, flush=True)
    raise SystemExit(code)


def run(cmd, **kw):
    """`subprocess.run` with UTF-8 text I/O by default (the CLI reads and writes UTF-8 on every platform).

    :param cmd: the argument list.
    :param kw: passed through to `subprocess.run`.
    :returns: the `subprocess.CompletedProcess`.
    """
    if kw.get("text") or kw.get("capture_output") or "input" in kw:
        kw.setdefault("text", True)
        kw.setdefault("encoding", "utf-8")
        kw.setdefault("errors", "replace")
    return subprocess.run([str(c) for c in cmd], **kw)


# ---------------------------------------------------------- env files -----
def read_env(path):
    """Read a `KEY=value` file (site.env, round.env) written by `write_env`.

    :param path: the file to read.
    :returns: a dict of keys to (unquoted) values.
    """
    out = {}
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        if "=" in line and not line.lstrip().startswith("#"):
            key, value = line.split("=", 1)
            parts = shlex.split(value) if value.strip() else [""]
            out[key.strip()] = parts[0] if parts else ""
    return out


def write_env(path, values):
    """Write a `KEY=value` file. Values are shell-quoted, so the file can still be `source`d from bash.

    :param path: the file to write.
    :param values: a dict of keys to values (None becomes an empty value).
    """
    lines = [f"{k}={shlex.quote(str(v)) if v not in (None, '') else ''}" for k, v in values.items()]
    Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


# --------------------------------------------------------- processes -----
def port_in_use(port):
    """True when something accepts TCP connections on localhost:<port> (IPv4 or IPv6).

    :param port: the TCP port.
    :returns: whether the port is taken.
    """
    for family, addr in ((socket.AF_INET, "127.0.0.1"), (socket.AF_INET6, "::1")):
        try:
            with socket.socket(family, socket.SOCK_STREAM) as s:
                s.settimeout(0.5)
                if s.connect_ex((addr, int(port))) == 0:
                    return True
        except OSError:
            continue  # e.g. IPv6 disabled
    return False


def pid_alive(pid):
    """True when a process with this id is still running.

    On Windows `os.kill(pid, 0)` would terminate the process, so this asks the OS for its exit code instead.

    :param pid: the process id.
    :returns: whether the process is running.
    """
    if WINDOWS:
        import ctypes
        kernel32 = ctypes.windll.kernel32
        handle = kernel32.OpenProcess(0x1000, False, int(pid))  # PROCESS_QUERY_LIMITED_INFORMATION
        if not handle:
            return False
        try:
            code = ctypes.c_ulong()
            return bool(kernel32.GetExitCodeProcess(handle, ctypes.byref(code))) and code.value == 259  # STILL_ACTIVE
        finally:
            kernel32.CloseHandle(handle)
    try:
        os.kill(int(pid), 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def kill_tree(pid, timeout=10):
    """Stop a process and everything it started, waiting up to `timeout` seconds.

    Windows: `taskkill /T /F`. macOS/Linux: SIGTERM to its process group (sites start in their own session),
    then SIGKILL if it hasn't gone.

    :param pid: the root process id.
    :param timeout: seconds to wait for a graceful exit before forcing it.
    """
    pid = int(pid)
    if not pid_alive(pid):
        return
    if WINDOWS:
        run(["taskkill", "/PID", pid, "/T", "/F"], capture_output=True)
    else:
        for sig in (signal.SIGTERM, signal.SIGKILL):
            try:
                os.killpg(pid, sig)
            except (ProcessLookupError, PermissionError):
                try:
                    os.kill(pid, sig)
                except ProcessLookupError:
                    return
            deadline = time.monotonic() + timeout
            while time.monotonic() < deadline and pid_alive(pid):
                time.sleep(0.2)
            if not pid_alive(pid):
                return
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline and pid_alive(pid):
        time.sleep(0.2)


def port_owner_pids(port):
    """Process ids listening on a TCP port: a fallback for strays the pid file doesn't know about.

    Windows parses `netstat -ano`; macOS/Linux use `lsof` when it's installed (it's optional).

    :param port: the TCP port.
    :returns: a set of process ids (empty when none, or when it can't tell).
    """
    pids = set()
    if WINDOWS:
        out = run(["netstat", "-ano", "-p", "TCP"], capture_output=True).stdout
        out += run(["netstat", "-ano", "-p", "TCPv6"], capture_output=True).stdout
        for line in out.splitlines():
            cols = line.split()
            if len(cols) >= 5 and cols[3].upper() == "LISTENING" and cols[1].rsplit(":", 1)[-1] == str(port):
                pids.add(int(cols[4]))
    elif shutil.which("lsof"):
        out = run(["lsof", "-nP", f"-tiTCP:{port}", "-sTCP:LISTEN"], capture_output=True).stdout
        pids.update(int(p) for p in out.split() if p.isdigit())
    pids.discard(0)
    return pids


def spawn_detached(cmd, cwd, log, env):
    """Start a long-running process that outlives this script and the terminal that ran it.

    :param cmd: the argument list.
    :param cwd: working directory.
    :param log: path of a log file to append stdout and stderr to.
    :param env: the full environment for the process.
    :returns: the process id.
    """
    kw = {"cwd": str(cwd), "env": env, "stdin": subprocess.DEVNULL, "stderr": subprocess.STDOUT}
    with open(log, "ab") as out:
        if not WINDOWS:
            return subprocess.Popen([str(c) for c in cmd], stdout=out, start_new_session=True, **kw).pid
        # Own process group + hidden console, so Ctrl+C or closing the calling terminal doesn't stop the site.
        # Breaking away from the caller's job object keeps it alive when an agent's shell job is torn down;
        # some jobs forbid breakaway, so fall back to staying in the job.
        flags = subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW
        try:
            return subprocess.Popen([str(c) for c in cmd], stdout=out,
                                    creationflags=flags | subprocess.CREATE_BREAKAWAY_FROM_JOB, **kw).pid
        except OSError:
            return subprocess.Popen([str(c) for c in cmd], stdout=out, creationflags=flags, **kw).pid


def remove_tree(path, attempts=10):
    """Delete a directory tree, clearing read-only flags and retrying while Windows releases file locks.

    :param path: the directory to delete.
    :param attempts: how many times to try (0.5 s apart).
    :raises OSError: when it still can't be deleted after the last attempt.
    """
    def clear_readonly(func, p, *_):
        os.chmod(p, stat.S_IWRITE)
        func(p)

    for i in range(attempts):
        try:
            if sys.version_info >= (3, 12):
                shutil.rmtree(path, onexc=clear_readonly)
            else:
                shutil.rmtree(path, onerror=clear_readonly)
            return
        except FileNotFoundError:
            return
        except OSError:
            if i == attempts - 1:
                raise
            time.sleep(0.5)


# -------------------------------------------------------------- HTTP ------
class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None  # surface 3xx to the caller, who needs the Location header


class Http:
    """A tiny cookie-keeping HTTPS client (the harness's replacement for `curl -k -c jar -b jar`).

    Redirects are not followed, so the caller can read `Location` (the OAuth authorize step needs it).
    """

    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar),
                                                  urllib.request.HTTPSHandler(context=INSECURE), _NoRedirect())

    def request(self, method, url, *, json_body=None, form=None, headers=None, timeout=60):
        """Send a request and return `(status, headers, body text)`. HTTP error statuses are returned, not raised.

        :param method: the HTTP method.
        :param url: the absolute URL.
        :param json_body: an object to send as JSON.
        :param form: a dict to send as `application/x-www-form-urlencoded`.
        :param headers: extra request headers.
        :param timeout: seconds before giving up.
        :returns: a tuple of the status code, the response headers and the decoded body.
        :raises urllib.error.URLError: when the server can't be reached.
        """
        data, hdrs = None, dict(headers or {})
        if json_body is not None:
            data, hdrs["Content-Type"] = json.dumps(json_body).encode(), "application/json"
        elif form is not None:
            data, hdrs["Content-Type"] = urllib.parse.urlencode(form).encode(), "application/x-www-form-urlencoded"
        req = urllib.request.Request(url, data=data, headers=hdrs, method=method)
        try:
            with self.opener.open(req, timeout=timeout) as r:
                return r.status, r.headers, r.read().decode("utf-8", "replace")
        except urllib.error.HTTPError as e:
            return e.code, e.headers, e.read().decode("utf-8", "replace")


def get_text(url, timeout=30):
    """GET a URL (TLS unverified) and return `(status, body)`; HTTP errors are returned, not raised.

    :param url: the absolute URL.
    :param timeout: seconds before giving up.
    :returns: the status code and the decoded body.
    :raises urllib.error.URLError: when the server can't be reached.
    """
    status, _, body = Http().request("GET", url, timeout=timeout)
    return status, body


def pkce_pair():
    """Make an OAuth PKCE verifier and its S256 challenge.

    :returns: `(verifier, challenge)`.
    """
    verifier = secrets.token_hex(32)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
    return verifier, challenge


# -------------------------------------------------------------- NuGet -----
def version_key(v):
    """Sort key for NuGet/SemVer versions: numeric parts numerically, and a release after its prereleases.

    `0.1.0-alpha.9 < 0.1.0-alpha.10 < 0.1.0`, which plain string sorting gets wrong.

    :param v: a version string such as `17.7.0` or `0.1.0-alpha.13`.
    :returns: a tuple that orders versions correctly.
    """
    core, _, pre = v.split("+", 1)[0].partition("-")
    nums = tuple(int(x) if x.isdigit() else 0 for x in core.split("."))
    if not pre:
        return nums, (1,)
    return nums, (0, tuple((0, int(p), "") if p.isdigit() else (1, 0, p.lower()) for p in pre.split(".")))


def nuget_versions(package):
    """Every published version of a package, from the nuget.org flat-container index.

    :param package: the package id (any case).
    :returns: a list of version strings (lower-case, as nuget.org stores them).
    :raises urllib.error.URLError: when nuget.org can't be reached.
    """
    url = f"https://api.nuget.org/v3-flatcontainer/{package.lower()}/index.json"
    with urllib.request.urlopen(url, timeout=30) as r:
        return json.loads(r.read())["versions"]


def nuget_global_packages():
    """The NuGet global-packages folder (`~/.nuget/packages` unless NUGET_PACKAGES or config moves it).

    :returns: the folder as a Path.
    """
    res = run(["dotnet", "nuget", "locals", "global-packages", "--list"], capture_output=True)
    m = re.search(r"global-packages:\s*(.+)", res.stdout or "")
    if res.returncode == 0 and m:
        return Path(m.group(1).strip())
    return Path(os.environ.get("NUGET_PACKAGES") or Path.home() / ".nuget" / "packages")


# -------------------------------------------------------------- sites -----
class Site:
    """A throwaway test site under sites/<name>, created by new-site.py.

    :param where: a site name (`source`), or a path to its folder (`sites/source`).
    :raises SystemExit: when there's no site there.
    """

    def __init__(self, where):
        p = Path(where)
        self.dir = (p if (p / "site.env").exists() or len(p.parts) > 1 else SITES / str(where)).resolve()
        if not (self.dir / "site.env").exists():
            die(f"No site at {self.dir} (expected a site.env there)")
        self.env = read_env(self.dir / "site.env")
        self.name = self.env["SITE_NAME"]
        self.host = self.env["HOST"]
        self.port = int(self.env["PORT"])
        self.project = self.dir / self.env["PROJECT"]
        self.pid_file = self.dir / "site.pid"
        self.log = self.dir / "logs" / "site.log"

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

    # --- process -------------------------------------------------------
    def pid(self):
        """The running site's process id from site.pid, or None when it isn't running."""
        try:
            pid = int(self.pid_file.read_text().strip())
        except (FileNotFoundError, ValueError):
            return None
        return pid if pid_alive(pid) else None

    def is_running(self):
        """True when the site's process is alive."""
        return self.pid() is not None

    def server_status(self):
        """Umbraco's `serverStatus` (`Install`, `Run`, `BootFailed`, ...), or None if it doesn't answer yet."""
        try:
            status, body = get_text(f"{self.host}/umbraco/management/api/v1/server/status", timeout=3)
            return json.loads(body).get("serverStatus") if status == 200 else None
        except (OSError, ValueError):
            return None

    def app_dll(self):
        """The site's built application DLL (bin/Debug/<tfm>/<project>.dll).

        :raises SystemExit: when the site hasn't been built.
        """
        dlls = sorted(self.project.glob(f"bin/Debug/*/{self.env['PROJECT']}.dll"))
        if not dlls:
            die(f"{self.name} isn't built: run dotnet build {self.project}")
        return dlls[-1]

    def start(self, timeout=None):
        """Start the site in the background (logs -> logs/site.log) and wait until Umbraco reports `Run`.

        Runs the built DLL directly rather than `dotnet run`, so the pid file holds the real server process.

        :param timeout: seconds to wait (default: STARTUP_TIMEOUT or 300).
        :raises SystemExit: when Umbraco fails to boot, the process exits or it times out.
        """
        pid = self.pid()
        if pid:
            print(f"Already running at {self.host} (pid {pid})")
            return
        timeout = int(timeout or os.environ.get("STARTUP_TIMEOUT") or 300)
        self.log.parent.mkdir(parents=True, exist_ok=True)
        env = {**os.environ, "ASPNETCORE_ENVIRONMENT": "Development"}
        pid = spawn_detached(["dotnet", self.app_dll(), "--urls", self.host], self.project, self.log, env)
        self.pid_file.write_text(str(pid))
        print(f"Starting {self.host} ", end="", flush=True)
        status = None
        for _ in range(timeout):
            status = self.server_status()
            if status == "Run":
                print(" running.")
                return
            if status == "BootFailed" or not pid_alive(pid):
                print()
                print(self.tail_log())
                die("Umbraco failed to boot." if status == "BootFailed" else "Site process exited.")
            print(".", end="", flush=True)
            time.sleep(1)
        print()
        die(f"Timed out waiting for Umbraco (last status: {status or 'none'}). See {self.log}")

    def stop(self):
        """Stop the site: its process tree, then anything still listening on its port."""
        try:
            pid = int(self.pid_file.read_text().strip())
            kill_tree(pid)
        except (FileNotFoundError, ValueError):
            pass
        self.pid_file.unlink(missing_ok=True)
        for stray in port_owner_pids(self.port):
            kill_tree(stray)
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline and port_in_use(self.port):
            time.sleep(0.2)
        print(f"Stopped {self.name}")

    def tail_log(self, lines=40):
        """The last lines of logs/site.log."""
        try:
            return "\n".join(self.log.read_text(encoding="utf-8", errors="replace").splitlines()[-lines:])
        except FileNotFoundError:
            return "(no log yet)"


def list_sites():
    """Every site under sites/ (folders with a site.env), sorted by name."""
    return [Site(p.parent) for p in sorted(SITES.glob("*/site.env"))]
