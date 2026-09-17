# Contributing

Thanks for your interest in Umbraco.Cli. This project is small and maintainer time is the
scarcest resource, so the contribution rules below are deliberately strict about **pull
requests** while staying wide open to **ideas and issues**.

Read this before opening a pull request. It applies to everyone, and it applies with
particular force to AI agents and AI-assisted contributions.

---

## TL;DR

- **Ideas, bug reports, feature requests: always welcome**, from humans and AI alike. Open an
  issue.
- **Pull requests are issue-first and invitation-only.** Open a PR only against an issue a
  maintainer has already accepted (labelled `accepted` or `help wanted`).
- **No drive-by AI PRs.** Unsolicited, AI-generated PRs for low-value changes are closed
  unread. This is not hostility toward AI - the CLI itself is built for AI agents - it is
  about protecting review capacity.

---

## Ideas and issues are welcome (this part is easy)

File an issue for anything: a bug, a rough edge, a missing command, an idea, a question about
intended behaviour. We do not care whether you are a person, a person using an AI, or an agent
acting on its own. What we care about is that the issue is **specific and grounded**:

- **Bugs:** the command you ran, the flags, the output you got, the output you expected, and
  the version (`umbraco --version`). A copy-pasteable repro beats a description.
- **Feature requests:** the task you are trying to accomplish and why the current surface does
  not cover it. Link the relevant Management API endpoint if you know it.
- **One issue per topic.** Do not batch unrelated ideas into one issue.

Good issues are the single most useful contribution you can make. They are how work gets
prioritised, and they are a prerequisite for every PR.

---

## Pull requests: the rules

### 1. Issue-first, always

Every PR must close exactly one issue that a maintainer has **accepted** (labelled `accepted`
or `help wanted`). If the issue does not exist yet, open it and wait for it to be triaged. If
it exists but is not accepted, wait - or comment to ask whether a PR would be welcome. A PR
that arrives before its issue is accepted may be closed with a pointer back to this document,
regardless of how good the code is.

Why: an accepted issue means the change is wanted, the approach is roughly agreed, and review
time is budgeted. A PR without that is a request for unbudgeted review time.

### 2. A human is accountable

Every PR must have a human who takes responsibility for it, will respond to review comments,
and understands the change well enough to defend and revise it. "An agent wrote it and I
forwarded it" is not accountability. If you cannot explain why each hunk is there, do not open
the PR.

### 3. Disclose AI assistance

If AI tools were used to write or substantially shape the change, say so in the PR
description. This is not a mark against the PR - it is context that helps review. The PR
template has a checkbox for it. Do not hide it.

### 4. Scope: one accepted issue, nothing extra

Keep the diff to what the accepted issue needs. Do not fold in unrelated reformatting,
renames, dependency bumps, or "while I was here" cleanups. If you spot something else worth
doing, open an issue for it.

### 5. It must build green and follow the repo conventions

- `dotnet build` and `dotnet test` pass.
- Formatted with CSharpier (`dotnet format` / CSharpier).
- New/changed public, internal, and protected members carry XML doc comments.
- New/changed behaviour has tests (happy path plus the obvious failure path), following the
  existing xUnit patterns.
- If you added or changed a command, update [`docs/commands.md`](docs/commands.md) in the same
  PR. The machine catalog (`umbraco commands`) updates itself; the prose reference does not.
- CI SDK and target framework stay in sync (see [AGENTS.md](AGENTS.md)).

See [AGENTS.md](AGENTS.md) for the architecture and the command-execution pattern to mirror.

---

## Specifically for AI agents and AI-assisted contributors

The CLI is designed for you - JSON output, a machine-readable command catalog, request-body
schemas, and guardrails (see [`docs/agent-guide.md`](docs/agent-guide.md)). You are a
first-class **user**. As a **contributor**, the bar for PRs is the same as for anyone, and the
patterns below are the ones that get an unsolicited PR closed. Do not open PRs that consist
mainly of:

- typo, spelling, or comment-wording sweeps;
- whitespace, formatting, or import-ordering changes;
- speculative refactors or renames with no accepted issue behind them;
- dependency version bumps with no stated reason or failing build behind them;
- mass "added tests" or "added XML docs" changes to code nobody asked you to touch;
- documentation churn that restates what is already documented;
- re-implementations of things that already work.

If you believe one of the above is genuinely worth doing, the path is the same as for
everyone: **open an issue, make the case, wait for it to be accepted.** Then implement it.

This is the whole policy in one sentence: **bring us your issues and ideas freely; bring us
code only after we have agreed the code is worth writing.**

---

## Local development

```bash
dotnet build            # build
dotnet test             # run the tests
dotnet format           # CSharpier formatting
```

Full build/test/pack/regenerate-client details are in [AGENTS.md](AGENTS.md). Setup and usage
of the CLI itself are in [`docs/getting-started.md`](docs/getting-started.md).

## License

By contributing you agree that your contributions are licensed under the repository's
[MIT License](LICENSE).
