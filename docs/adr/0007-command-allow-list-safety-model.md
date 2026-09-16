# ADR 0007: Command allow-list safety model

- Status: Accepted
- Date: 2026-09-16
- Issue: #83 (allow-list hardening), building on #69 (the `--readonly` + allow-list guardrails). Related: #82 (confirmation gating).

## Context

The CLI is meant to be driven by AI agents and automation as well as humans. #69 added two guardrails
so a supervisor can bound what a session may do: `--readonly` (refuse every write) and a **command
allow-list** (`UMBRACO_ALLOWED_COMMANDS`, or the config `allowedCommands` field) that restricts which
noun groups / commands may run, with the `auth` group always exempt so a session can still
authenticate and be inspected.

The allow-list is a **supervisor-set guardrail**: the value is set by the parent process that
supervises the agent, and the agent — which controls its own command-line arguments — must not be
able to escape it. The #69 review and the #83 follow-up surfaced four ways the file-based form could
be weakened or was ambiguous. Two (H2, M1) were fixed as they were found; the remaining two (M2, L3)
plus a bypass discovered in the M2 fix itself are the subject of this ADR. None of these affect the
HTTP-level `--readonly` block, which was verified sound.

The non-trivial decisions had been living only in code comments in `CommandContextFactory`, so this
ADR records them.

## Decision

The environment-variable forms (`UMBRACO_READONLY`, `UMBRACO_ALLOWED_COMMANDS`) remain the primary
enforcement boundary — a supervisor sets them in the parent process where the agent cannot change
them. The **file-based** `allowedCommands` form is hardened so it is a real guardrail rather than an
advisory default, under one principle: **a session's own arguments (`--config`, `--profile`) may only
ever *tighten* the allow-list, never loosen or bypass it.**

### 1. `auth logout` preserves the allow-list (H2)

`logout` deletes credentials, and the `auth` group is always exempt, so a restricted session could
otherwise `auth logout` to drop a file-based guardrail and then run anything. `ConfigStore.Logout`
now clears only the credential fields and **keeps** a profile's `allowedCommands`
(`LogoutOutcome.CredentialsClearedAllowListKept`).

### 2. An unreadable config fails safe and is surfaced (M1)

A config file that exists but cannot be parsed previously failed open (no profile, and crucially no
file-based allow-list) silently. `CommandContextFactory` now warns on stderr
(`FileExistsButUnreadable`) so a dropped guardrail is visible rather than silent.

### 3. `--config` / `--profile` can only tighten (M2)

The effective allow-list for a command is the **most-restrictive** of two lists: the one from the
*resolved* store/profile the command runs against, and a *baseline* taken from the trusted default
store (which also carries any `UMBRACO_ALLOWED_COMMANDS` value). A command must satisfy **both**. So
pointing `--config` at a file without an allow-list, or selecting a `--profile` that exists only in
such a file, cannot widen access.

The baseline honours the requested `--profile` only when the **default** store actually defines it; a
profile that exists solely in a `--config` file **fails closed** to the default store's default
profile rather than resolving to an empty (unrestricted) config. This closes the profile-axis variant
of the bypass — `--config attacker.json --profile ghost` — that the intersection alone did not.

### 4. Present-but-blank means explicit lockdown (L3)

Only a **truly unset** (null) allow-list means "no restriction". Any *present* value that is blank,
whitespace, or separators-only (`UMBRACO_ALLOWED_COMMANDS=" "`, or `","`) is an **explicit lockdown**:
it permits nothing but the `auth` group. This removes the earlier asymmetry where `""` meant
allow-all while `","` meant deny-all, and makes "I set the variable" reliably mean "I intend to
restrict".

## Consequences

- The file-based allow-list is now a genuine sandbox against a session's own arguments, not merely a
  convenience default. The env-var form is still recommended for the strongest boundary because it
  sits in the parent process.
- A deployment that legitimately relied on `--config`/`--profile` to *widen* an allow-list (unusual)
  will now find those paths can only tighten; the intended list must be set on the default store or
  via the env var.
- A deployment that set `UMBRACO_ALLOWED_COMMANDS` to an empty/whitespace string expecting "allow
  all" will now be locked down to `auth` only. This is the safer failure direction and is documented
  in the README "Agent guardrails" section.
- The decisions are covered by unit tests in `CommandExecutorTests` (the `AllowList_*` cases,
  including the `--config`/`--profile` bypass regressions and the whitespace-lockdown cases).
