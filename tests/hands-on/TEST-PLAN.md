# Hands-on test plan

A repeatable end-to-end test of the `umbraco` CLI against real, throwaway Umbraco sites. **Part A** builds a small
multilingual site with the CLI alone (scripted). **Part B** works through every command group (T1–T10), including
promotion to a second site. [`AGENTS.md`](AGENTS.md) says how to run a round; this file says **what** to test and what
counts as a pass.

Each test ends with a **Known** line: what was still open on the latest build tested (alpha.14, Umbraco 17.7.0).
Anything else that fails is a new finding. Update the Known lines at the end of each round.

Rules that apply to every test:

- **Use only the CLI.** When you have to leave it (Management API, SQL, site code), that's a finding: record the workaround.
- **Verify effects, not status codes.** After a write, re-read it (`get`), fetch the page (`curl`), or query the database
  (`sqlite3 -readonly sites/<s>/UmbracoSite/umbraco/Data/Umbraco.sqlite.db`). Several past bugs reported success while doing nothing.
- **Destructive tests run on `sites/staging`**, never on the source, unless the step says otherwise.

Conventions (run from `tests/hands-on`; the snippets are bash, so on Windows use Git Bash with `jq` installed and `python` for `python3`):

```bash
S=sites/source; U=$S/umb          # source site and its pinned CLI (profile 'source', harness config)
T=sites/staging/umb               # staging site's CLI
IDS=$S/work/ids.json              # written by tools/build_site.py: home, blog, years[], posts[], images[], brochures[], contact, submissions
id() { jq -r "$1" $IDS; }         # e.g. id .blog, id '.posts[3]'
```

The fixture site (`fixtures/`): Home (en + da, 3 blocks), Blog with **2025** / **2026** year folders holding 23 posts, a
Contact page (en + da) with a contact form and member sign-up, a Form Submissions root, 8 images, 3 PDF brochures (custom
`brochure` media type), 18 dictionary items under `Blog` / `Form`, member type `siteMember` + group `Subscribers`,
user group `blogEditors`, domains `localhost:<port>` (en-US) and `localhost:<port>/da` (da-DK).

---

## Part A: build the site (scripted)

`python3 setup-round.py` runs all of Part A: `tools/build_site.py`, then `tools/install_controller.py` and `tools/submit_forms.py`.
To re-run on an existing empty site: `UMB_SITE=sites/<s> python3 tools/build_site.py` (`--only publish` re-runs one step).
Each line of its output is `ok`/`FAIL`, the step time and a note. `SLOW` lines list any single CLI call over 5 s.

| Step | What it exercises | If it fails, check by hand |
|---|---|---|
| A1 homepage | `template create --id`, `document-type create` (flags), `document-type get \| jq \| update` round-trip | `$U document-type get homePage` |
| A2 blog | `data-type create --json-body --id`, `document-type create --json-body` (create response lists properties), `media folder create --id`, `media upload --id` ×8 | `$U media get <id>` (urls, values) |
| A3 multilingual | `language create --fallback`, culture-variant types, `dictionary create --value en-US=… --value da-DK=…`, `content domain set` | `$U language list`, `$U content domain get $(id .home)` |
| A4 contact + members | `member-group create --id`, `member-type create --json-body`, contact document types; controller + 8 form checks | `$U member list --group Subscribers` |
| A5 dictionary tree | `dictionary create --parent <key>` for 16 children | `$U dictionary tree --recursive` |
| A6 user group | `user-group create` with sections, language, permissions, document + media start nodes | `$U user-group get blogEditors` |
| A7 Block List | element type, Block List data type, `blocks` property, `partial-view folder create`, block partial | `curl` the home page: 3 `<h2>` blocks |
| A8 media type | `media-type create` + `get \| update` (8 properties), Folder allowed types, `media upload --media-type brochure --value …` | `$U media get <brochure>` |
| Content | 29 × `content create --json-body` with the body's own `id` and parent, then publish per snapshot state (parents first) | `$U content get <id>` (`variants[].state`, `urls`) |

**Pass:**
- [ ] `build_site.py` → `All steps passed`, no `SLOW` lines
- [ ] 8/8 form checks
- [ ] If a reference site built from the same fixtures is running: `python3 tools/compare_sites.py <its port> <source port>` → all pages identical

**Extra manual checks** (quick, on the source):
- Merge update: `echo '{"values":[{"alias":"excerpt","culture":"en-US","segment":null,"value":"X"}]}' | $U content update $(id '.posts[2]') --json-body -`
  changes only that value; `--replace` is the only way to replace everything.
- `content copy <post> --parent <year>` returns the new id; `content unpublish <post> --yes` → Draft; republish.
- `member create --email … --name … --member-type siteMember --password …`, `member update <id> --group Subscribers --value company=Acme
  --username … --new-password … --approved false`, re-read with `member get`, then `member delete <id> --yes`.
- `dictionary update Blog.Tags --value da-DK=…` keeps en-US; `--value en=…` is rejected ("no language with the ISO code 'en'").

**Known (alpha.14):**
- L-113: `user get` has no content languages. L-107/L-108: a hand-written schema snapshot applies correctly but never re-diffs clean.
- `content list` rows have no `updateDate` (by design: the tree endpoint doesn't return it).

---

## Part B: command-group tests

### T1 · Promote the source to staging

1. `$U schema export --out $S/work/schema.json` (types, data types, templates, languages, dictionary, member/user groups,
   **partial views, stylesheets, scripts**). On staging: `$T schema diff …` → `$T schema apply … --dry-run` → `$T schema apply …`
   → `$T schema diff …` (expect `[]`).
2. `$U media export --out $S/work/media` (a directory) → `$T media diff …` → `$T media apply …` → `$T media diff … --verify-files` (expect `[]`).
3. `$U content export --out $S/work/content.json` → `$T content apply … --dry-run` (rows name each document; Label properties get a
   one-line warning) → `$T content apply …` → `$T content diff …` (expect `[]`).
4. Per-environment bits: `$T content domain set $(id .home) --default-culture en-US --domain localhost:<staging port>=en-US
   --domain localhost:<staging port>/da=da-DK`, and `python3 tools/install_controller.py sites/staging` (site code).
5. `python3 tools/compare_sites.py <source port> <staging port>` and `UMB_SITE=sites/staging python3 tools/submit_forms.py`.
6. **Drift:** change a document type on the source (`get | jq '.data.icon="icon-rss"' | update`) → `schema diff` on staging shows exactly that
   field. Add a post on each site → `content diff`. Prune only with a **subtree** snapshot: `$U content export --root $(id .blog) --out …`
   then `$T content apply … --prune --yes` creates the source post, deletes the staging-only one, and leaves form submissions alone.
   A whole-tree `--prune` must refuse without `--yes` and warn about content created on the target.

**Pass:**
- [ ] Every re-diff is empty; all pages identical; 8/8 forms on staging
- [ ] Drift shows exactly what changed; the subtree prune touches exactly the right items

**Known (alpha.14):** domains are per-environment by design; the form controller is site code (copy + rebuild). L-103: run `schema apply` before
`content diff`, or a document type change marks every document of that type Changed.

### T2 · Archive moves, redirects, sort, recycle bin, rollback

1. Record the URLs of three posts (`$U content get <id> | jq .data.urls`). `content move <post> --parent <other year>` for each →
   `redirect list --content-item <id>` → `curl -o /dev/null -w '%{http_code} %{redirect_url}'` on every old URL: 301 to the new one. Move them back.
2. `content sort --parent <year> --order <id>,<id>,…` (commas), and `--by name|createDate|updateDate|publishDate [--desc]`.
3. Rename a post (merge-update the en-US variant `name`) + publish → the original URL 301s straight to the newest (no chain).
4. `content trash <post>` → 404; `content restore <post> --publish` → back under its original parent, live. Trash a copy, then
   `content empty-recycle-bin` (refuses without `--yes`).
5. Bad edit + publish → `content version list <post> --culture en-US` → `content version rollback <version> --culture en-US --publish`.
   Pick the version **after** the one marked `isCurrentPublishedVersion` (publishing turns the edited draft into the published version).
6. `content find --name docker`, `content find --path Home/Blog/2025`, `content tree --recursive`.

**Pass:**
- [ ] Every old URL 301s; sort, restore and rollback put things back

**Known (alpha.14):** none.

### T3 · Webhooks

1. `python3 tools/webhook_listener.py 8765 &` (writes `webhook-requests.jsonl`).
2. `$U webhook event list`; `$U webhook create --name … --url http://127.0.0.1:8765/hook --event Umbraco.ContentPublish,Umbraco.ContentUnpublish`.
   An unknown event (`ContentPublished`) must be rejected with a suggestion.
3. Publish → `Umb-Webhook-Event: Umbraco.ContentPublish` with id/name/route/properties; `content unpublish <id> --yes` → `Umbraco.ContentUnpublish`.
   Delivery is asynchronous: allow ~20 s.
4. `$U webhook list`, `$U webhook delete <id> --yes`; stop the listener.

**Pass:**
- [ ] Both events received, with a matching payload

**Known (alpha.14):** L-106: `-v` logs `Authorization`/`X-Api-Key` header values in clear. L-124: no way to remove one header.

### T4 · Stylesheets, scripts, partial views

1. `stylesheet|script|partial-view list/get/create/update/delete`, including `partial-view folder create --name X --parent blocklist`
   and creating a file in it. `create` returns `path` with a leading `/`.
2. Every page (en + da) renders the stylesheet, header, footer and block partial; a deleted stylesheet 404s.

**Pass:**
- [ ] CRUD round-trips; pages render

**Known (alpha.14):** none. (Razor: don't name a variable `page`; compile errors show only as `UmbracoCompilationException`.)

### T5 · Scheduled publishing

1. Unpublish a post, then `content publish <id> --publish-at <now+2m, UTC ISO>` → response `published: false, publishAt`;
   `content get` shows `scheduledPublishDate` on each variant.
2. Poll `content get` every 10 s: Published within ~60 s of the time (the scheduler runs about every minute); page 200.
3. `content publish <id> --unpublish-at <now+90s>` → Draft / 404 on time.
4. `content publish-descendants $(id .blog) --wait` → `{taskId, isComplete: true}`, and it must not publish a scheduled draft early.

**Pass:**
- [ ] Publish and expire both happen within ~60 s of the time

**Known (alpha.14):** **Umbraco:** scheduled publishes don't fire webhooks. **Umbraco/SQLite:** under concurrent load (parallel agent lanes) a site can
hang ~10 min on `database table is locked`, and scheduled jobs fire late while it does.

### T6 · Document blueprints

1. `document-blueprint folder create --name Blog`; `document-blueprint create --from-document <post> --name "Blog post starter" --parent <folder>`
   (both variants named; `get` shows `name` + `parent`); `document-blueprint move <id> --parent <other folder>`.
2. `document-blueprint update <id> --json-body …` merges (placeholders, a da-DK name).
3. `document-blueprint scaffold <id> | jq '.data | .parent={id:"<year>"} | .variants=[…names…]' | $U content create --json-body -` → publish → 200 in en + da.

**Pass:**
- [ ] The new post carries the blueprint's defaults and is live in both languages

**Known (alpha.14):** none.

### T7 · Search, tags, imaging

1. `indexer list` (`healthStatus: Healthy`), `indexer rebuild ExternalIndex`.
2. `searcher query ExternalIndex --term Docker` (or `ExternalSearcher`): the Docker post first.
3. `tag list` (16 tags with counts), `--group default`.
4. `imaging resize-urls $(id '.images[0]') --width 300 --height 200 --mode Crop --format webp` → the URL serves `image/webp`, 300×200.

**Pass:**
- [ ] Relevant hits, 16 tags, correct crop and type

**Known (alpha.14):** `searcher list` is empty on Umbraco 17.7.0 (Umbraco).

### T8 · Data type housekeeping and delete guards

1. `data-type folder create --name Site`; `data-type move <id> --parent <folder>` ×2; `data-type list --parent <folder>`.
2. `data-type copy <id> --parent <folder>` (returns the id), `is-used`, `referenced-by`, `property-type is-used --document-type blogPost --alias categories`.
3. Delete the unused copy with `--yes`.
4. **On staging:** `$T data-type delete 531e54e3-6652-4fc4-8097-39701348ea76 --yes` (Blog Categories, in use) → exit 2, `refused`,
   names the property; nothing changes. Same for `document-type delete blogYear --yes` (counts its items) and
   `schema apply <snapshot without the type> --prune --yes`.

**Pass:**
- [ ] Housekeeping correct; every in-use delete refused without `--force`

**Known (alpha.14):** none.

### T9 · Diagnostics and ops

- `server status|info|configuration|troubleshooting`, `health list`, `health run Security`
- `log-viewer levels|level-count|list --level Error|message-templates` (count "token request was successfully validated": tokens should be reused)
- `models-builder status|dashboard|build` (428 in InMemoryAuto, with a clear message)
- `manifest list`, `culture list`, `relation-type list`, `relation list --relation-type relateDocumentOnCopy` after `content copy --relate`
- `user-data create/list/get/update/delete`
- `redirect tracking status|enable|disable`, `redirect delete <id> --yes`

**Pass:**
- [ ] Every command returns meaningful data; errors in `log-viewer` are explained by the round's own tests

**Known (alpha.14):** `redirect tracking disable` fails with a configuration hint (Umbraco 17 keeps tracking on). L-105: `-o human` on any
object command prints only "✓ Done" (use `-o json`). Umbraco sometimes logs the same event twice.

### T10 · Safety and CI mode

1. `python3 tools/safety_matrix.py sites/source` → `23 passed, 0 failed` (read-only mode, allow-list, env-only credentials, bad credentials, exit codes).
2. Profiles: `$U auth profile list`; `sites/staging/umb content list` vs `$U content list` hit different sites.
3. **Auth commands in an isolated config only:** `$U --config $S/work/auth-test.json auth login --profile A …` etc. (`--config` stops the wrapper
   adding the harness config). Check `UMBRACO_PROFILE` is honoured by `login` and `logout`, and logging out the default doesn't promote another profile.
4. Output: `-o csv`, `--fields a,b`, `-o human`, `-q`.

**Pass:**
- [ ] 23/23; the right site per profile; auth never touches a profile it wasn't asked to

**Known (alpha.14):** L-131: `-v` logs nothing for auth commands. L-109: `--dry-run` prints passwords in clear.

---

## History

| Round | Build | Findings | Tracking issue |
|---|---|---|---|
| 1 · 2026-09-23 | alpha.6 (NuGet) | 31 (L-001 → L-031) | #187 |
| 2 · 2026-09-24 | alpha.11 | 51 (L-032 → L-082) | #250 |
| 3 · 2026-09-28 | alpha.12 | 19 (L-083 → L-101); both round-2 P0s fixed | #302 |
| 4 · 2026-09-28 | alpha.13 | 1 (L-102 → #320); 28/28 re-tests pass; Part A scripted | comment on #302 |
| 5 · 2026-09-28 | alpha.14 (local pack) | 35 (L-103 → L-137), 4 🟠; T1-T10 pass; 5 parallel lanes + scratch site | |
