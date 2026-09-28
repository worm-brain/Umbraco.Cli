#!/usr/bin/env python3
"""Part A of TEST-PLAN.md (A1-A8) in one go: rebuild the multilingual test site with the CLI only.

Usage (from tests/hands-on): UMB_SITE=sites/<name> python3 tools/build_site.py [--only step,step]

Reads fixtures/schema.json and fixtures/content.json (a `schema export` + `content export` of a finished
test site) and recreates
everything with single commands (flags, --json-body, --id, get|update round-trips), keeping the
round-3 GUIDs. Prints one line per step: ok / FAIL with a short note, and exits 1 if any step failed.
Writes $UMB_SITE/work/ids.json (home, blog, years, posts, images, ...) for the Part B tests.
"""
import json
import os
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent  # tests/hands-on
SITE = Path(os.environ["UMB_SITE"]).resolve()
UMB = str(SITE / "umb")
WORK = SITE / "work"
FIX = ROOT / "fixtures"
SCHEMA = json.loads((FIX / "schema.json").read_text())
CONTENT = json.loads((FIX / "content.json").read_text())
IMAGES_DIR = FIX / "images"
PDFS_DIR = FIX / "pdfs"
ASSETS = ROOT / "assets"
PORT = json.loads((SITE / "credentials.json").read_text())["host"].rsplit(":", 1)[1]

MEDIA_FOLDER = "105a6d0b-c324-4947-bed2-df65d2fb30b1"
IMAGE_IDS = ["e3fd5d93-334c-43af-87d7-fde496ecf038", "ded8006b-d16f-4e96-b525-69b0367cbb2a",
             "df70d1af-da65-4fb2-a55a-fece7bea28ed", "d23ed7c6-db8d-4f9d-94f6-ee1713c13f46",
             "4d4db01d-1563-4ddf-be69-f6839d903e59", "c154f7d9-a87e-4c06-b778-2a4670fc6d06",
             "aa774fee-cb1c-461c-88d7-46dc2ce35ae2", "e3a15879-8e80-40c2-886b-ff0c3f1411ec"]
BROCHURE_FOLDER = "64d497d7-0415-4089-a9e7-493ab0d179c6"
BROCHURES = [("1d8d976d-97ec-4682-8f0d-79a18960170d", "getting-started-with-umbraco-cli"),
             ("47e9dfb8-e54f-4c72-9c60-5f4c4d2793b3", "membership-and-forms"),
             ("d106992a-c2a7-4f98-992e-eda5a19a7f91", "multilingual-sites-guide")]

failures = []
SLOW = []  # (seconds, args) for any single CLI call over 5 s
TMP = Path(tempfile.mkdtemp(prefix="build_site_"))


def umb(*args, body=None):
    """Run the CLI with -o json. Returns (exit code, parsed envelope from stdout or stderr)."""
    stdin = json.dumps(body) if body is not None else None
    if body is not None:
        args = (*args, "--json-body", "-")
    t0 = time.monotonic()
    res = subprocess.run([UMB, *args, "-o", "json"], input=stdin, capture_output=True, text=True)
    dt = time.monotonic() - t0
    if dt > 5:
        SLOW.append((round(dt, 1), " ".join(a for a in args if a != "-")[:90]))
    for text in (res.stdout, res.stderr):
        try:
            return res.returncode, json.loads(text)
        except json.JSONDecodeError:
            continue
    return res.returncode, {"status": "error", "message": (res.stderr or res.stdout)[:300]}


_last = [time.monotonic()]


def step(name, ok, note=""):
    now = time.monotonic()
    print(f"{'ok  ' if ok else 'FAIL'}  {now - _last[0]:5.1f}s  {name:<48} {note}"[:230], flush=True)
    _last[0] = now
    if not ok:
        failures.append(name)


def expect(name, code_env, check=None, note=""):
    code, env = code_env
    good = code == 0 and env.get("status") == "success" and (check is None or check(env.get("data")))
    step(name, good, note if good else f"exit {code}: {env.get('message') or json.dumps(env.get('data'))[:160]}")
    return env.get("data")


def fx(kind, key, value):
    return next(x for x in SCHEMA[kind] if x.get(key) == value)


def write(name, text):
    p = TMP / name
    p.write_text(text)
    return str(p)


# ---------------------------------------------------------------- A1..A8 --
def languages():
    expect("A3 language create da-DK --fallback", umb("language", "create", "--culture", "da-DK", "--fallback", "en-US"))


def templates():
    for t in SCHEMA["templates"]:
        f = write(f"{t['alias']}.cshtml", t["content"])
        expect(f"A1 template create {t['alias']} --id", umb("template", "create", "--name", t["name"], "--alias", t["alias"],
                                                            "--content-file", f, "--id", t["id"]),
               lambda d, i=t["id"]: d["id"] == i)


def data_types():
    for name in ("Blog Categories",):
        dt = fx("dataTypes", "name", name)
        body = {k: dt[k] for k in ("name", "editorAlias", "editorUiAlias", "values")}
        expect(f"A2 data-type create '{name}' --json-body --id", umb("data-type", "create", "--id", dt["id"], body=body),
               lambda d, i=dt["id"]: d["id"] == i)
    el = fx("documentTypes", "alias", "titleTextBlock")
    expect("A7 document-type create titleTextBlock (body id)", umb("document-type", "create", body=el),
           lambda d: d["id"] == el["id"] and len(d.get("properties") or []) == len(el["properties"]),
           "body id kept, response lists properties")
    bl = fx("dataTypes", "name", "Homepage Blocks")
    body = {k: bl[k] for k in ("id", "name", "editorAlias", "editorUiAlias", "values")}
    expect("A7 data-type create 'Homepage Blocks' (body id)", umb("data-type", "create", body=body),
           lambda d: d["id"] == bl["id"])


def document_types():
    hp = fx("documentTypes", "alias", "homePage")
    expect("A1 document-type create homePage (flags)", umb("document-type", "create", "--name", hp["name"], "--alias", "homePage",
                                                           "--allow-at-root", "--icon", "icon-home", "--id", hp["id"]),
           lambda d: d["id"] == hp["id"])
    for alias in ("blogPost", "blogYear", "blog", "contactSubmission", "formSubmissions", "contactPage"):
        t = fx("documentTypes", "alias", alias)
        expect(f"A2 document-type create {alias} --json-body", umb("document-type", "create", body=t),
               lambda d, t=t: d["id"] == t["id"] and len(d.get("properties") or []) == len(t["properties"])
               and (d.get("collection") or None) == (t.get("collection") or None),
               f"{len(t['properties'])} properties echoed")
    # Round-trip: get -> set groups/properties/templates/variance/children -> update
    code, env = umb("document-type", "get", "homePage")
    body = env["data"]
    for k in ("containers", "properties", "allowedTemplates", "defaultTemplate", "variesByCulture", "allowedDocumentTypes"):
        body[k] = hp[k]
    expect("A1 document-type get|update homePage (round-trip)", umb("document-type", "update", "homePage", body=body),
           lambda d: [p["alias"] for p in d["properties"]] == [p["alias"] for p in hp["properties"]],
           "title, bodyText, blocks")


def static_files():
    for f in ("header.cshtml", "footer.cshtml"):
        expect(f"T4 partial-view create {f}", umb("partial-view", "create", "--name", f, "--content-file", str(ASSETS / f)))
    expect("T4 partial-view folder create blocklist/Components", umb("partial-view", "folder", "create", "--name", "Components",
                                                                      "--parent", "blocklist"))
    expect("T4 partial-view create in new folder", umb("partial-view", "create", "--name", "titleTextBlock.cshtml",
                                                        "--parent", "blocklist/Components",
                                                        "--content-file", str(ASSETS / "titleTextBlock.cshtml")),
           lambda d: d["path"] == "/blocklist/Components/titleTextBlock.cshtml")
    expect("T4 stylesheet create site.css", umb("stylesheet", "create", "--name", "site.css", "--content-file", str(ASSETS / "site.css")),
           lambda d: d["path"] == "/site.css", "path has leading /")
    expect("T4 script create site.js", umb("script", "create", "--name", "site.js", "--content-file", str(ASSETS / "site.js")))


def media():
    expect("A2 media folder create Blog --id", umb("media", "folder", "create", "--name", "Blog", "--id", MEDIA_FOLDER))
    ok = 0
    for i, mid in enumerate(IMAGE_IDS, 1):
        code, env = umb("media", "upload", str(IMAGES_DIR / f"blog-{i}.jpg"), "--parent", MEDIA_FOLDER,
                        "--name", f"Blog Image {i}", "--id", mid)
        ok += code == 0 and env["data"]["id"] == mid and bool(env["data"].get("urls"))
    step("A2 media upload x8 --id", ok == 8, f"{ok}/8 with urls")
    br = fx("mediaTypes", "alias", "brochure")
    expect("A8 media-type create brochure (flags)", umb("media-type", "create", "--name", "Brochure", "--alias", "brochure",
                                                         "--allow-at-root", "--id", br["id"]))
    code, env = umb("media-type", "get", "brochure")
    body = env["data"]
    body["containers"], body["properties"] = br["containers"], br["properties"]
    expect("A8 media-type get|update brochure", umb("media-type", "update", "brochure", body=body),
           lambda d: len(d["properties"]) == len(br["properties"]))
    code, env = umb("media-type", "get", "Folder")
    body = env["data"]
    body["allowedMediaTypes"] = fx("mediaTypes", "alias", "Folder")["allowedMediaTypes"]
    expect("A8 media-type update Folder (allow brochure)", umb("media-type", "update", "Folder", body=body))
    expect("A8 media folder create Brochures --id", umb("media", "folder", "create", "--name", "Brochures", "--id", BROCHURE_FOLDER))
    ok = 0
    for i, (mid, name) in enumerate(BROCHURES, 1):
        code, env = umb("media", "upload", str(PDFS_DIR / f"{name}.pdf"), "--media-type", "brochure", "--parent", BROCHURE_FOLDER,
                        "--id", mid, "--value", f"title=Brochure {i}: {name}", "--value", f"summary=A PDF about {name}",
                        "--value", "department=Docs", "--value", f"pageCount={i * 4}", "--value", f"publishedOn=2026-09-0{i}")
        ok += code == 0 and env["data"]["mediaType"]["alias"] == "brochure"
    step("A8 media upload x3 brochure --value", ok == 3, f"{ok}/3")


def members():
    g = SCHEMA["memberGroups"][0]
    expect("A4 member-group create Subscribers --id", umb("member-group", "create", "--name", g["name"], "--id", g["id"]))
    mt = fx("memberTypes", "alias", "siteMember")
    expect("A4 member-type create siteMember --json-body", umb("member-type", "create", body=mt),
           lambda d: len(d.get("properties") or []) == 3, "response lists 3 properties")


def dictionary():
    items = SCHEMA["dictionaryItems"]
    by_id = {d["id"]: d for d in items}
    ok = 0
    for d in sorted(items, key=lambda d: d["parent"] is not None):
        args = ["dictionary", "create", "--key", d["name"], "--id", d["id"]]
        if d["parent"]:
            args += ["--parent", by_id[d["parent"]["id"]]["name"]]
        for t in d["translations"]:
            args += ["--value", f"{t['isoCode']}={t['translation']}"]
        code, env = umb(*args)
        ok += code == 0 and env["data"]["id"] == d["id"]
    step("A3/A5 dictionary create x18 (--parent <key>)", ok == len(items), f"{ok}/{len(items)}")


def content():
    docs = [d for d in CONTENT["documents"] if d["body"]["documentType"]["id"] != fx("documentTypes", "alias", "contactSubmission")["id"]]
    created, ok_ids, order = set(), 0, []
    pending = list(docs)
    while pending:
        progress = False
        for d in list(pending):
            if d.get("parent") and d["parent"] not in created:
                continue
            b = d["body"]
            body = {"id": d["id"], "documentType": {"id": b["documentType"]["id"]}, "template": b["template"],
                    "parent": {"id": d["parent"]} if d.get("parent") else None,
                    "variants": [{"culture": v["culture"], "segment": v.get("segment"), "name": v["name"]} for v in b["variants"]],
                    "values": [{k: v[k] for k in ("alias", "culture", "segment", "value")} for v in b["values"]]}
            code, env = umb("content", "create", body=body)
            if code == 0:
                ok_ids += env["data"]["id"] == d["id"]
                created.add(d["id"])
                order.append(d)
            else:
                step(f"A content create {b['variants'][0]['name'][:30]}", False, env.get("message", ""))
            pending.remove(d)
            progress = True
        if not progress:
            break
    step("A content create (body ids kept)", ok_ids == len(docs), f"{ok_ids}/{len(docs)} kept their id")
    publish(order)
    return finish_content(docs)


def publish(docs):
    """Publish each document's snapshot-Published cultures, parents first."""
    pub = want = 0
    for d in docs:
        cultures = [v["culture"] for v in d["body"]["variants"] if v.get("state") == "Published"]
        if not cultures:
            continue
        want += 1
        args = ["content", "publish", d["id"]] + (["--culture", ",".join(c for c in cultures if c)] if any(cultures) else [])
        code, env = umb(*args)
        pub += code == 0
        if code:
            print(f"      publish {d['id']}: {env.get('message')}", flush=True)
    step("A content publish per snapshot state", pub == want, f"{pub}/{want}")


def finish_content(docs):
    home = next(d["id"] for d in docs if not d.get("parent") and d["body"]["documentType"]["id"] == fx("documentTypes", "alias", "homePage")["id"])
    expect("A3 content domain set", umb("content", "domain", "set", home, "--default-culture", "en-US",
                                         "--domain", f"localhost:{PORT}=en-US", "--domain", f"localhost:{PORT}/da=da-DK"))
    return docs, home


def user_group(blog):
    ug = fx("userGroups", "alias", "blogEditors")
    expect("A6 user-group create blogEditors (start nodes)", umb(
        "user-group", "create", "--alias", "blogEditors", "--name", "Blog Editors", "--id", ug["id"],
        "--section", "Umb.Section.Content", "--section", "Umb.Section.Media", "--culture", "da-DK",
        "--fallback-permission", "Umb.Document.Read", "--fallback-permission", "Umb.Document.Update",
        "--fallback-permission", "Umb.Document.Publish", "--document-start-node", blog, "--media-start-node", MEDIA_FOLDER),
        lambda d: d.get("documentStartNode") == blog)


def snapshot_docs_in_parent_order():
    docs = [d for d in CONTENT["documents"] if d["body"]["documentType"]["id"] != fx("documentTypes", "alias", "contactSubmission")["id"]]
    out, seen = [], set()
    while len(out) < len(docs):
        for d in docs:
            if d["id"] not in seen and (not d.get("parent") or d["parent"] in seen):
                out.append(d); seen.add(d["id"])
    return out


def main():
    WORK.mkdir(exist_ok=True)
    only = sys.argv[sys.argv.index("--only") + 1].split(",") if "--only" in sys.argv else None
    if only:  # re-run selected steps on an already-built site, e.g. --only publish
        for name in only:
            publish(snapshot_docs_in_parent_order()) if name == "publish" else globals()[name]()
        report()
    languages(); templates(); data_types(); document_types(); static_files(); media(); members(); dictionary()
    docs, home = content()
    blog_type = fx("documentTypes", "alias", "blog")["id"]
    blog = next(d["id"] for d in docs if d["body"]["documentType"]["id"] == blog_type)
    user_group(blog)
    years = [d["id"] for d in docs if d["body"]["documentType"]["id"] == fx("documentTypes", "alias", "blogYear")["id"]]
    posts = [d["id"] for d in docs if d["body"]["documentType"]["id"] == fx("documentTypes", "alias", "blogPost")["id"]]
    ids = {"home": home, "blog": blog, "years": years, "posts": posts, "images": IMAGE_IDS, "mediaFolder": MEDIA_FOLDER,
           "brochures": [b[0] for b in BROCHURES], "brochureFolder": BROCHURE_FOLDER,
           "contact": next(d["id"] for d in docs if d["body"]["documentType"]["id"] == fx("documentTypes", "alias", "contactPage")["id"]),
           "submissions": next(d["id"] for d in docs if d["body"]["documentType"]["id"] == fx("documentTypes", "alias", "formSubmissions")["id"])}
    (WORK / "ids.json").write_text(json.dumps(ids, indent=2))
    print(f"ids -> {WORK / 'ids.json'}")
    report()


def report():
    for dt, cmd in SLOW:
        print(f"SLOW  {dt:6.1f}s  {cmd}")
    print(f"\n{'All steps passed' if not failures else f'{len(failures)} step(s) failed: ' + ', '.join(failures)}.")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
