#!/usr/bin/env python3
"""Test 10: guardrails + CI mode. Runs each case, compares exit code (and a check) with the documented behaviour.

Usage (from tests/hands-on): python3 tools/safety_matrix.py sites/source
(needs sites/source/work/ids.json from tools/build_site.py)
"""
import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import harness  # noqa: E402

WRITE = json.dumps({"values": [{"alias": "excerpt", "culture": "en-US", "segment": None,
                                "value": "GUARDRAIL BREACH - this write should have been blocked"}]})
counts = {"pass": 0, "fail": 0}


def first_json(text):
    """The first JSON document in some CLI output, or None."""
    text = text.lstrip()
    try:
        return json.JSONDecoder().raw_decode(text)[0] if text else None
    except ValueError:
        return None


def case(site, label, want, *args, env=None, input=None):
    """Run one CLI call and record PASS when its exit code is `want`.

    :param site: the harness Site.
    :param label: what the case checks.
    :param want: the expected exit code.
    :param args: CLI arguments.
    :param env: environment overrides (None removes a variable, like `env -u`).
    :param input: stdin text.
    """
    res = site.umb(*args, env=env, input=input)
    out = (res.stdout or "") + (res.stderr or "")
    doc = first_json(res.stdout or "") or first_json(res.stderr or "")
    msg = str(doc.get("message") or doc.get("status") or "")[:90] if isinstance(doc, dict) else out.strip()[:90]
    if res.returncode == want:
        counts["pass"] += 1
        print(f"PASS  exit {res.returncode:<3} {label:<58} {msg}")
    else:
        counts["fail"] += 1
        print(f"FAIL  exit {res.returncode:<3} (want {want}) {label:<48} {msg}")


def main():
    harness.utf8_stdio()
    if len(sys.argv) != 2:
        harness.die("usage: python3 tools/safety_matrix.py sites/<name>", 2)
    site = harness.Site(sys.argv[1])
    creds = site.credentials()
    host, cid, sec = creds["host"], creds["apiUser"]["clientId"], creds["apiUser"]["clientSecret"]
    post = json.loads((site.dir / "work" / "ids.json").read_text(encoding="utf-8"))["posts"][1]
    empty_cfg = str(Path(tempfile.mkdtemp()) / "empty-config.json")
    image = str(harness.ROOT / "fixtures" / "images" / "blog-1.jpg")

    def excerpt():
        doc = json.loads(site.umb("content", "get", post, "-o", "json").stdout)
        return next(v["value"] for v in doc["data"]["values"] if v["alias"] == "excerpt" and v["culture"] == "en-US")

    before = excerpt()
    ro = {"UMBRACO_READONLY": "1"}

    print("== read-only")
    case(site, "--readonly blocks content update", 2, "content", "update", post, "--json-body", "-", "--readonly", "-o", "json", input=WRITE)
    case(site, "UMBRACO_READONLY=1 blocks content update", 2, "content", "update", post, "--json-body", "-", "-o", "json", env=ro, input=WRITE)
    case(site, "UMBRACO_READONLY=1 blocks publish", 2, "content", "publish", post, "-o", "json", env=ro)
    case(site, "UMBRACO_READONLY=1 blocks health run (POST)", 2, "health", "run", "Security", "-o", "json", env=ro)
    case(site, "UMBRACO_READONLY=1 allows reads", 0, "content", "get", post, "-o", "json", env=ro)
    case(site, "UMBRACO_READONLY=1 allows --dry-run write", 0, "content", "update", post, "--json-body", "-", "--dry-run", "-o", "json",
         env=ro, input=WRITE)
    if excerpt() == before:
        counts["pass"] += 1
        print("PASS  excerpt unchanged after read-only cases")
    else:
        counts["fail"] += 1
        print("FAIL  excerpt CHANGED under read-only")

    print("== allow-list")
    allow = lambda v: {"UMBRACO_ALLOWED_COMMANDS": v}  # noqa: E731
    case(site, "ALLOWED=content: content list runs", 0, "content", "list", "-o", "json", env=allow("content"))
    case(site, "ALLOWED=content: media list blocked", 2, "media", "list", "-o", "json", env=allow("content"))
    case(site, "ALLOWED=content.list: content get blocked", 2, "content", "get", post, "-o", "json", env=allow("content.list"))
    case(site, "ALLOWED=content.list: content list runs", 0, "content", "list", "-o", "json", env=allow("content.list"))
    case(site, "ALLOWED='' (lockdown): content list blocked", 2, "content", "list", "-o", "json", env=allow(""))
    case(site, "ALLOWED=',' (lockdown): server info blocked", 2, "server", "info", "-o", "json", env=allow(","))
    case(site, "ALLOWED='' (lockdown): auth whoami allowed", 0, "auth", "whoami", "-o", "json", env=allow(""))
    case(site, "commands (discovery) never blocked", 0, "commands", env={**allow(""), **ro})
    case(site, "ALLOWED=media + --readonly: media upload blocked", 2, "media", "upload", image, "--readonly", "-o", "json",
         env=allow("media"))

    print("== CI mode (env-only credentials, no saved profile)")
    ci = {"UMBRACO_PROFILE": None, "UMBRACO_HOST": host, "UMBRACO_CLIENT_ID": cid, "UMBRACO_CLIENT_SECRET": sec}
    case(site, "env creds + empty --config: content list", 0, "content", "list", "--config", empty_cfg, "-o", "json", env=ci)
    case(site, "no creds + empty --config: aborts (no host)", 2, "content", "list", "--config", empty_cfg, "-o", "json",
         env={"UMBRACO_PROFILE": None, "UMBRACO_HOST": None})
    case(site, "bad secret: auth fails", 2, "content", "list", "--config", empty_cfg, "-o", "json",
         env={**ci, "UMBRACO_CLIENT_SECRET": "wrong"})

    print("== exit codes")
    case(site, "success = 0", 0, "server", "status", "-o", "json")
    case(site, "API 404 = 1", 1, "content", "get", "00000000-0000-0000-0000-000000000001", "-o", "json")
    case(site, "parse error = 1", 1, "content", "get", "not-a-guid", "-o", "json")
    case(site, "destructive without --yes = 2", 2, "content", "delete", post, "-o", "json")

    print(f"\n{counts['pass']} passed, {counts['fail']} failed")
    if excerpt() != before:
        print("WARNING: test post excerpt changed - restore it")
    sys.exit(1 if counts["fail"] else 0)


if __name__ == "__main__":
    main()
