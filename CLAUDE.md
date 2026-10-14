@AGENTS.md

## Claude Code

- Plan an issue in plan mode and wait for approval before building it.
- A gitignored `CLAUDE.local.md` at the checkout root, when it exists, imports the maintainer's
  private briefs and says where planning issues live. At the start of a session, read the handoff it
  points to: it holds the current state and the gotchas from the last session.
