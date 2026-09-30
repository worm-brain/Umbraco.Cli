# Hands-on test ledger

Every bug, gap and improvement found while running the harness, **written down as it is found**, one round per section.
`tools/file_issues.py` turns unfiled rows into GitHub issues and writes the numbers back here, and into
`ledger-history.json`.

This is a local working file: copy `LEDGER.template.md` to `LEDGER.md` when you start a round (`LEDGER.md` is
git-ignored). Filed findings live on as GitHub issues, and `ledger-history.json` maps every filed id to its issue, so
`L-NNN` references in new entries still become links. **New finding ids continue from one past the highest id in
`ledger-history.json`.**

## Format (the filing script depends on it)

Start each round with this heading and metadata line (copy the values from `round.env`):

```markdown
# Round 5 · 2026-10-05 · CLI 0.1.0-local.20261005.101500 (abc1234) · Umbraco 17.7.0
<!-- round: label=test-round:2026-10-05 cli=0.1.0-local.20261005.101500 commit=abc1234 umbraco=17.7.0 -->
```

`label` becomes a GitHub label on every issue filed from the round. Make it unique per round (add `b`, `c` for a second round on the same day).

Then, in this order:

1. **Re-test table:** one row per issue under test (the previous round's filed issues, plus still-open `cli-testing` issues):
   `| Issue | Ledger | Result | Notes |` with ✅ fixed / 🟡 partly / ❌ still failing and one line of evidence.
2. **Test log:** `| Test | Result | Notes |`, one row per A-step group and T1–T10, ✅ / 🟡 / 🔴, naming the findings it produced.
3. **Findings table**, exactly these 7 columns:
   `| ID | Type | Sev | Labels | Command | Title | Issue |`
   - **Type:** `Bug`, `Gap` or `Improvement`.
   - **Sev:** 🔴 data loss / blocks a headline feature (P0), 🟠 high value (P1), 🟡 nice to have (P2).
   - **Labels:** area labels that exist on the repo, comma-separated: `content`, `media`, `schema`, `members`, `languages`,
     `webhooks`, `auth`, `safety`, `error-handling`, `agent-dx`, `dx`, `content-workflow`, `coverage`, `documentation`,
     `api-consistency`, `infrastructure`.
   - **Command:** the command(s) involved, in backticks. **Title:** one line; escape `|` as `\|`.
   - **Issue:** leave **empty** until filed. Anything in it (`#123`, `covered by #196`, `not a bug`) stops the script filing the row.
4. **One entry per finding** (becomes the issue body):

```markdown
### L-103 · Bug 🟠 · Short title

- **Found:** <date> · CLI <version> · <test, e.g. T1>
- **Repro:** exact commands and their output (trim to the relevant part).
- **Expected:** what should happen, and why (docs, help text, consistency with other commands).
- **Workaround:** if any.
- **Suggestion:** optional.
```

---

