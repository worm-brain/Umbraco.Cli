# ADR 0002: Live integration test harness

- Status: Accepted
- Date: 2026-07-24
- Issue: #51

## Context

Every bug in the alpha.1 live test was a real endpoint/shape/param mismatch that the
fake-client unit tests could not catch (and the `members list` `take=0` -> 500 regression
slipped through even after the rewrite). We need end-to-end tests that drive the real CLI
against a real Umbraco Management API.

The acceptance criterion asks for a CI job that *spins up* Umbraco. Standing up Umbraco 17
in CI is a genuine spike (no official Docker image; needs a database, an unattended
install, and a seeded client-credentials API user) and is split into its own follow-up.
This ADR covers the part that delivers regression-catching value immediately: the harness
itself.

## Decision

Add a separate test project `tests/Umbraco.Cli.IntegrationTests` that:

- **Drives the real CLI as a subprocess** (`dotnet exec Umbraco.Cli.dll <args>`), not the
  client library directly, so the full path is exercised: arg parsing, DI, auth, output
  writer. Output is JSON (the CLI emits JSON when stdout is redirected), which the harness
  parses and asserts on.
- **Resolves the instance from the CLI's own config resolution** — environment variables
  (`UMBRACO_HOST`/`UMBRACO_CLIENT_ID`/`UMBRACO_CLIENT_SECRET`) or the saved config file.
  No credentials live in the test code.
- **Skips, rather than fails, when no instance is reachable.** A shared fixture runs
  `auth whoami` once; if it does not succeed, every test is skipped via `SkippableFact` /
  `Skip.IfNot`. This keeps the solution-wide `dotnet test` green in CI (where no instance
  exists yet) while running fully on a developer machine or a provisioned CI instance.
- Covers list/get/create/delete across the command surface, including a self-cleaning
  create->list->delete round-trip (webhooks) and the `members list` default-paging case
  that regressed.

A dedicated CI job that provisions Umbraco and sets the instance env will land with the
provisioning follow-up; until then the integration tests run (and skip) inside the existing
`dotnet test`.

## Consequences

- Real end-to-end coverage now, catching endpoint/shape/paging drift that unit tests miss.
- Zero credentials in the repo; the harness uses whatever auth the CLI already resolves.
- Tests are environment-gated: they are a no-op (skipped) without a reachable instance, so
  they never make CI red on their own but also do not run in CI until provisioning exists.
- Subprocess invocation is slower than in-process calls, but the suite is small and the
  fidelity (true end-to-end) is the point.
