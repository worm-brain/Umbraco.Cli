#!/usr/bin/env python3
"""File unfiled LEDGER.md findings as GitHub issues, then write the issue numbers back into the ledger
and into ledger-history.json.

Usage (from tests/hands-on):
    python3 tools/file_issues.py --dry-run            # show what would be filed (always do this first)
    python3 tools/file_issues.py                      # file every row whose Issue column is empty
    python3 tools/file_issues.py --round 5            # only round 5
    python3 tools/file_issues.py --round 5 --epic summary.md
                                                      # also open a tracking issue for round 5, with
                                                      # summary.md as its opening section

Everything the script needs is in LEDGER.md (see the format at the top of that file):
- each round heading is followed by `<!-- round: label=... cli=... commit=... umbraco=... -->`;
- each summary row is `| ID | Type | Sev | Labels | Command | Title | Issue |`;
- each finding has a `### L-NNN · <Type> <Sev> · <title>` entry, whose text becomes the issue body.
A row with anything in its Issue column (an issue number, "covered by #N", "not a bug") is skipped.
`L-NNN` references in bodies become issue links, including earlier rounds via ledger-history.json.
LEDGER.md is a local, git-ignored working file (start it from LEDGER.template.md); ledger-history.json is
the committed record of every filed id, so ids and links carry on across machines and rounds.
"""
import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
LEDGER = HERE / "LEDGER.md"
HISTORY = HERE / "ledger-history.json"
REPO = "worm-brain/Umbraco.Cli"
COMMON = ["cli-testing"]
PRIORITY = {"🔴": "priority:P0", "🟠": "priority:P1", "🟡": "priority:P2"}
TYPE = {"Bug": ["bug"], "Gap": ["enhancement", "gap"], "Improvement": ["enhancement"]}
ROW_LINE = re.compile(r"^\|\s*L-\d{3}\s*\|.*\|\s*$", re.M)


def row_cells(line):
    """Split a summary-table line on unescaped pipes -> [ID, Type, Sev, Labels, Command, Title, Issue]."""
    cells = [c.strip() for c in re.split(r"(?<!\\)\|", line.strip())[1:-1]]
    if len(cells) != 7:
        raise SystemExit(f"Summary row needs 7 columns (ID|Type|Sev|Labels|Command|Title|Issue): {line.strip()}")
    return cells


def rows(text):
    return [(m, row_cells(m.group(0))) for m in ROW_LINE.finditer(text)]
ENTRY = re.compile(r"^### (L-\d{3}) · (\w+) (🔴|🟠|🟡) · .*?\n(.*?)(?=^### L-|^# |^---\s*$|\Z)", re.S | re.M)


def args(argv=None):
    """Parse the command line. argparse, not a hand scan, so `--help` prints usage and an unknown
    or mistyped flag stops the script: this one files real issues, so it must never fall through
    to a live run on input it didn't understand (round 5 filed a whole round from `--help`)."""
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--dry-run", action="store_true", help="show what would be filed, file nothing")
    p.add_argument("--round", help="only this round's rows (needed with --epic)")
    p.add_argument("--epic", metavar="SUMMARY.md", help="also open a tracking issue, with this file as its opening section")
    p.add_argument("--repo", default=REPO, help=f"target repository (default {REPO})")
    o = p.parse_args(argv)
    return {"dry": o.dry_run, "round": o.round, "epic": o.epic, "repo": o.repo}


def gh(*cmd):
    res = subprocess.run(["gh", *cmd], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if res.returncode:
        raise SystemExit(f"gh {' '.join(cmd[:3])} failed: {res.stderr.strip()}")
    return res.stdout.strip()


def rounds(text):
    """Yield (number, heading, meta, section text) for every '# Round N' section."""
    heads = list(re.finditer(r"^# Round (\d+) · (.*)$", text, re.M))
    for i, h in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(text)
        section = text[h.start():end]
        m = re.search(r"<!-- round: (.*?) -->", section)
        meta = dict(kv.split("=", 1) for kv in m.group(1).split()) if m else {}
        yield int(h.group(1)), h.group(2).strip(), meta, section


def clean(cell):
    return cell.replace("`", "").replace("**", "").replace("\\|", "|").strip()


def main():
    opt = args()
    for stream in (sys.stdout, sys.stderr):  # ledger titles carry emoji; Windows pipes default to the ANSI code page
        stream.reconfigure(encoding="utf-8", errors="replace")
    if not LEDGER.exists():
        raise SystemExit("No LEDGER.md: copy LEDGER.template.md to LEDGER.md and record the round in it first.")
    text = LEDGER.read_text(encoding="utf-8")
    links = {k: f"#{v}" for k, v in json.loads(HISTORY.read_text(encoding="utf-8")).items()} if HISTORY.exists() else {}
    for _, c in rows(text):
        if re.fullmatch(r"#\d+", c[6]):
            links[c[0]] = c[6]

    todo = []
    for num, heading, meta, section in rounds(text):
        if opt["round"] and str(num) != opt["round"]:
            continue
        if "label" not in meta:
            raise SystemExit(f"Round {num} has no '<!-- round: label=... -->' line under its heading.")
        entries = {m.group(1): m.group(4).strip() for m in ENTRY.finditer(section)}
        for _, (lid, typ, sev, labels, command, title, issue) in rows(section):
            if issue:
                continue
            if lid not in entries:
                raise SystemExit(f"{lid} has a summary row but no '### {lid} · …' entry.")
            if typ not in TYPE or sev not in PRIORITY:
                raise SystemExit(f"{lid}: type must be one of {list(TYPE)} and severity one of {list(PRIORITY)}.")
            area = [l for l in re.split(r"[,\s]+", clean(labels)) if l]
            todo.append({"id": lid, "round": num, "meta": meta, "heading": heading,
                         "title": f"{clean(command)}: {clean(title)}" if clean(command) else clean(title),
                         "labels": TYPE[typ] + [PRIORITY[sev]] + area + COMMON + [meta["label"]],
                         "sev": sev, "type": typ, "body": entries[lid]})

    if not todo:
        print("Nothing to file: every summary row already has something in its Issue column.")
    for t in todo:
        print(f"{t['id']} [{', '.join(t['labels'])}] {t['title']}")
    if opt["dry"] or not todo:
        return

    for label in sorted({t["meta"]["label"] for t in todo}):
        subprocess.run(["gh", "label", "create", label, "-R", opt["repo"], "--color", "C2E0C6",
                        "--description", "Found in a hands-on test round (tests/hands-on)"], capture_output=True)

    def body(t):
        m = t["meta"]
        head = (f"> Found in hands-on test round {t['round']} ({t['heading']}): Umbraco.Community.Cli **{m.get('cli', '?')}** "
                f"(commit `{m.get('commit', '?')}`) against Umbraco **{m.get('umbraco', '?')}**. Ledger ref: `{t['id']}`. "
                "Reproduce with the matching test in `tests/hands-on/TEST-PLAN.md`.")
        text = re.sub(r"\bL-\d{3}\b", lambda r: links.get(r.group(0), r.group(0)), t["body"])
        return f"{head}\n\n{text}"

    for t in todo:
        url = gh("issue", "create", "-R", opt["repo"], "--title", t["title"], "--label", ",".join(t["labels"]), "--body", body(t))
        t["number"] = int(url.rsplit("/", 1)[1])
        links[t["id"]] = f"#{t['number']}"
        print(f"{t['id']} -> {url}")
    for t in todo:  # second pass: references to findings filed in this same run
        if re.search(r"\bL-\d{3}\b", t["body"]):
            gh("issue", "edit", str(t["number"]), "-R", opt["repo"], "--body", body(t))

    numbers = {t["id"]: t["number"] for t in todo}
    def fill(m):
        lid = row_cells(m.group(0))[0]
        return re.sub(r"\|\s*$", f"#{numbers[lid]} |", m.group(0).rstrip()[:-1].rstrip() + " |") if lid in numbers else m.group(0)
    LEDGER.write_text(ROW_LINE.sub(fill, text), encoding="utf-8")
    record_history(numbers)

    if opt["epic"]:
        make_epic(opt, todo)


def record_history(numbers):
    """Merge newly filed ids into ledger-history.json, the committed id -> issue map.

    LEDGER.md is not committed, so this file is what keeps later rounds' `L-NNN` links working and
    tells the next round where its ids start. Keys stay sorted by id so the diff is one line per finding.

    :param numbers: the ids filed in this run, mapped to their issue numbers.
    """
    history = json.loads(HISTORY.read_text(encoding="utf-8")) if HISTORY.exists() else {}
    history.update(numbers)
    ordered = dict(sorted(history.items(), key=lambda kv: int(kv[0][2:])))
    HISTORY.write_text(json.dumps(ordered, indent=2) + "\n", encoding="utf-8")


def make_epic(opt, todo):
    rnd = {t["round"] for t in todo}
    if len(rnd) != 1:
        raise SystemExit("--epic needs --round, so the tracking issue covers one round.")
    first = todo[0]
    m = first["meta"]
    kind = {"Bug": "bug", "Gap": "gap", "Improvement": "improvement"}
    sections = []
    for sev, name in (("🔴", "P0 · data loss / blocks a headline feature"), ("🟠", "P1 · high value"), ("🟡", "P2 · nice to have")):
        items = [f"- [ ] #{t['number']} ({kind[t['type']]}) {t['title']}" for t in todo if t["sev"] == sev]
        if items:
            sections.append(f"## {name} ({len(items)})\n\n" + "\n".join(items))
    summary = Path(opt["epic"]).read_text(encoding="utf-8").strip()
    body = (f"Tracking issue for **hands-on test round {first['round']}** ({first['heading']}), run with `tests/hands-on`. "
            f"All issues are labelled `cli-testing` + `{m['label']}`.\n\n{summary}\n\n" + "\n\n".join(sections))
    url = gh("issue", "create", "-R", opt["repo"], "--title",
             f"Tracking: findings from hands-on test round {first['round']} (CLI {m.get('cli', '?')} on Umbraco {m.get('umbraco', '?')})",
             "--label", f"epic,cli-testing,{m['label']}", "--body", body)
    print(f"epic -> {url}")


if __name__ == "__main__":
    main()
