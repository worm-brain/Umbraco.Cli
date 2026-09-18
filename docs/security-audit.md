# Security audit

How we look for security holes in this tool, so real people in real environments do not hit
one we could have caught. There are **two tiers**, and they do different jobs:

| Tier | What it is | When it runs | Deterministic? | Blocks a release? |
| --- | --- | --- | --- | --- |
| **1 - Automated gate** | CodeQL, dependency-vuln, secret and package scanning | Every PR, every push to `main`, every release tag, weekly, on demand | Yes - clean pass/fail | Yes |
| **2 - Deep audit** | Threat model + adversarial whole-repo review + validated findings | Quarterly, and before any major or RC release | No - human-triaged | Only via confirmed findings promoted into Tier 1 |

The two are deliberately different. Tier 1 must be fast, reproducible and quiet when nothing
changed - so it holds only checks that give the same answer every run. Tier 2 is slow,
expensive and non-deterministic - so it never sits on the per-commit critical path. **Confirmed
Tier-2 findings become Tier-1 regressions** (a test, a query, or a fixed dependency), which is
what makes the whole thing rerunnable rather than a one-off.

Tracking issues: [#155](https://github.com/worm-brain/Umbraco.Cli/issues/155) (Tier 1),
[#156](https://github.com/worm-brain/Umbraco.Cli/issues/156) (Tier 2).

---

## Tier 1 - the automated gate

Defined in [`.github/workflows/security.yml`](../.github/workflows/security.yml). Four
independent jobs; any one failing fails the gate.

| Job | Tool | Fails when | Suppress an accepted finding via |
| --- | --- | --- | --- |
| CodeQL (C#) | `github/codeql-action`, `security-extended` | A new code-scanning alert | Dismiss the alert in the repo **Security -> Code scanning** tab |
| NuGet vulnerabilities | `dotnet list package --vulnerable` | A package at/above the threshold (default High) | Add the advisory to [`security/nuget-audit-allowlist.txt`](../security/nuget-audit-allowlist.txt) |
| Secret scan | gitleaks (pinned), full history | Any unignored secret in history | Add the fingerprint to [`.gitleaksignore`](../.gitleaksignore) |
| Package hygiene | `dotnet pack` + content inspection | A key/cert/config file or secret pattern ships in the `.nupkg` | Fix the package - do not suppress |

Every suppression path requires a **reason and a revisit date** in the file; an allow-list
entry with no expiry quietly becomes a permanent hole.

### Run it locally

The parsers are PowerShell so they behave identically in CI and on a dev box:

```powershell
dotnet restore
./scripts/ci/nuget-audit.ps1 -Threshold High      # dependency vulnerabilities
./scripts/ci/secret-scan.ps1                       # history secret scan (downloads gitleaks)
dotnet pack src/Umbraco.Cli/Umbraco.Cli.csproj -c Release -o ./nupkg
./scripts/ci/package-audit.ps1                     # package contents
```

CodeQL runs in CI only; to reproduce locally, use the CodeQL CLI with the same config file.

### Scope choices worth knowing

- The generated Kiota client (`src/Umbraco.Cli.Client/Generated/**`) is excluded from CodeQL
  ([`.github/codeql/codeql-config.yml`](../.github/codeql/codeql-config.yml)): it is a build
  artifact, not hand-written code. It is covered instead at the supply-chain level (below).
- CodeQL uses `build-mode: none` (buildless C# extraction) to avoid build flakiness. Switch to
  `build-mode: manual` if deeper dataflow extraction is ever needed.
- **Hardening backlog** (not blockers): pin actions and the gitleaks binary by immutable
  digest; add package signing / SBOM publication; raise the dependency threshold to Moderate
  once the baseline is clean.

---

## Tier 2 - the deep audit

A repeatable process, not an improvised prompt. Run it end to end; produce a report in the
schema below; file each confirmed finding as its own issue and promote it into Tier 1.

### Step 1 - Threat model (before any code review)

Write (or update from last time) a short threat model capturing:

- **Entry points** - CLI arguments and options, `UMBRACO_*` environment variables, the config
  file, stdin (content/bulk input), and every HTTP **response** from the Umbraco server (the
  server is a trust boundary in *both* directions).
- **Assets** - the OAuth client secret, the bearer token, the operator's other files and shell,
  the integrity of content/schema written to the server.
- **Trust boundaries** - operator <-> CLI, CLI <-> config/secret store, CLI <-> Umbraco server,
  repo <-> CI <-> NuGet.org (the release supply chain).
- **Deployment environments** - interactive dev machine; unattended CI with credentials in env
  vars; an AI agent driving the CLI under an allow-list guardrail.
- **Adversaries** - a co-tenant on the operator's machine; a malicious or compromised Umbraco
  server; a poisoned dependency or CI action; a prompt-injected agent trying to escape its
  allow-list.

### Step 2 - Whole-repo inspection

Give the reviewing agent permission to inspect the **whole repository**, not individual files:
trace execution paths end to end, read configuration and DI wiring, build and run the tests,
and read the CI definitions. Exclude `src/Umbraco.Cli.Client/Generated/**` from line-by-line
review (audit it at the dependency/spec level instead).

For a **diff-scoped** review between full audits, the `/security-review` skill in Claude Code
reviews just the current branch's changes - useful on a risky PR without running the whole Tier 2.

### Step 3 - Adversarial review lenses

Run these as **separate** targeted reviews, not one generic prompt - one mega-prompt gives
shallow coverage of everything. Each lens below lists this repo's current entry points so the
review starts from real coordinates. The copy-paste prompt spec is in the appendix.

1. **OAuth / token handling** (`SEC-AUTH`) - `UmbracoAuthService` (token fetch/cache/refresh,
   client_credentials POST), `CommandContextFactory` (token resolution, `--token` override).
   Look for: token logged or cached to disk, refresh race, secret in the token request error path.
2. **HTTP client & TLS** (`SEC-HTTP`) - `Program.cs` client wiring, `CommandContextFactory`
   base-address handling, `VerboseHttpHandler`. **Known gap:** `http://` hosts are accepted with
   no HTTPS enforcement (`auth doctor` only warns) - on a plaintext connection the bearer token
   is interceptable. Assess and decide: enforce HTTPS, or require an explicit opt-in for http.
3. **Credential storage** (`SEC-CRED`) - `SecretProtector`, `ConfigStore`. **Known nuance:**
   DPAPI protects the secret at rest on Windows only; on Linux/macOS the secret is stored
   **plaintext** and protected solely by file mode 600. Assess the co-tenant / backup / synced-
   home-dir exposure and whether that is clearly documented to operators.
4. **Filesystem & process execution** (`SEC-FS`) - content/schema export writes, content/bulk
   input reads, media upload, static-file commands. **Current state:** output paths come only
   from the operator's `--out`, never from server data, and there is no process execution
   anywhere - so no traversal/zip-slip/command-injection sink exists *today*. This lens is a
   **regression guard**: flag the moment a future command derives a write path or a shell
   command from server-controlled data.
5. **Untrusted server responses** (`SEC-FS`, output half) - the CLI trusts the server's JSON
   and prints it. Look for terminal-escape injection in human/rendered output, unbounded
   response handling (memory), and any future feature that writes server-named files.
6. **CI/CD & distribution** (`SEC-CICD`) - `ci.yml`, `publish.yml`, `security.yml`, the release
   OIDC/Trusted-Publishing flow, action pinning. Look for: token/permission scope creep,
   unpinned or mutable actions, artifact tampering between pack and publish.
7. **Package / supply chain** (`SEC-PKG`) - `scripts/fetch-spec.ps1` (dev-time spec fetch, has
   `-SkipCertificateCheck`), `scripts/regen-client.ps1` (kiota), `spec/management.json` (the
   committed source of truth), and what actually ships in the `.nupkg`.
8. **CMS API privilege / allow-list model** (`SEC-PRIV`) - `CommandContextFactory` allow-list
   enforcement and read-only mode, `MutationInterceptorHandler`, destructive-op confirmation.
   **Known bypass surface:** the `auth` command group is always permitted, an unreadable config
   drops a file-based allow-list with only a stderr warning (fail-open), and the interceptor is
   not wired onto the auth client. Assess each against a prompt-injected-agent adversary.

### Step 4 - Validation (the part that separates signal from noise)

**A finding is not a finding until it is validated.** For each candidate, do one of:

- **Confirm it** - construct a proof of concept: a failing test, a crafted server response, a
  reproduction command, or a script that demonstrates the impact. Record the exact steps.
- **Downgrade it** - if you cannot demonstrate it, mark it a **hypothesis**. Hypotheses go in an
  appendix, never the main report, and never block a release on their own.

Without this step a deep AI review produces an impressive pile of plausible-but-non-exploitable
findings. Do not skip it.

### Severity rubric (tuned to a CLI, not a web app)

| Severity | Meaning for this tool |
| --- | --- |
| **Critical** | Remote/unauthenticated credential theft, or arbitrary code execution on the operator's machine, with no unusual prerequisites (e.g. a crafted server response leading to RCE or silent secret exfiltration). |
| **High** | Local exposure of the stored secret/token to another user on the machine; a guardrail bypass causing unintended writes; a secret written to logs/output/the package; a TLS downgrade that exposes the token in transit. |
| **Medium** | Needs unusual prerequisites or operator misconfiguration; information disclosure short of credentials; a write confined to the invoker's own privileges. |
| **Low** | Hardening / defence-in-depth gap; negligible-impact info leak. |
| **Info** | Observation, no direct security impact. |

### Confidence rubric

| Confidence | Bar | Goes in |
| --- | --- | --- |
| **Confirmed** | A PoC / failing test demonstrates it | Main report; promote to Tier 1 |
| **Probable** | Clear code path, no PoC yet (e.g. needs a live server) | Main report; add a PoC before fixing |
| **Hypothesis** | Suspected, unproven | Appendix only |

### Finding schema

Every finding carries a **stable id** `SEC-<LENS>-<NNN>` (lenses: `AUTH`, `HTTP`, `CRED`, `FS`,
`PROC`, `CICD`, `PKG`, `PRIV`). The id is what ties a Tier-2 finding to its Tier-1 regression
test and its tracking issue, so it must not change across reruns.

```markdown
### SEC-AUTH-001 - <one-line title>
- Lens:          OAuth / token handling
- Severity:      High
- Confidence:    Confirmed
- Affected path: src/Umbraco.Cli/.../File.cs:123
- Attack scenario: <who does what, to achieve what>
- Prerequisites: <access / config / timing needed>
- Reproduction:  <exact PoC steps, command, or test name>
- Recommended fix: <the change>
- Regression test: <the Tier-1 test/query that will catch a recurrence>
- Status:        Open | Fixed (PR #) | Accepted (reason, revisit date)
- Links:         <issue #, advisory, commit>
```

### How Tier 2 feeds Tier 1

Every **confirmed** finding must leave behind a Tier-1 regression so it can never silently come
back:

- Code bug -> a unit/integration test that fails on the vulnerable behaviour.
- Vulnerable dependency -> bump it; the dependency job enforces it thereafter.
- Secret pattern -> a gitleaks rule / the fingerprint stays scanned.
- CI/config weakness -> encode the expectation in the workflow.

A finding is only "done" when its regression is green in Tier 1.

---

## Appendix - reusable agent prompt spec

Run once per lens. Fill in `{LENS}` and `{FOCUS AREAS}` from Step 3.

```
You are performing an adversarial security review of the Umbraco.Cli .NET 9 CLI, lens: {LENS}.

Context: this CLI holds Umbraco Management API OAuth client-credentials and a bearer token,
talks to a server over HTTP, and runs both interactively and unattended in CI (secrets in env
vars) and under an AI-agent allow-list guardrail. The Umbraco server is an UNTRUSTED trust
boundary in both directions. Ignore src/Umbraco.Cli.Client/Generated/** (generated code).

Focus areas for this lens: {FOCUS AREAS}

For each candidate issue:
1. Name the concrete adversary and their access level.
2. Trace the exact code path (file:line) from entry point to impact.
3. VALIDATE: build a proof of concept - a failing test, a crafted server response, or a
   reproduction command. If you cannot demonstrate impact, label it a HYPOTHESIS, not a finding.
4. Rate severity and confidence using the rubrics in docs/security-audit.md.
5. Emit each confirmed/probable finding in the SEC-{LENS}-NNN schema. Put hypotheses in a
   separate appendix. Do not pad the report - a short validated list beats a long speculative one.

Do not edit code. Output only the report.
```

Adjust cadence, threshold and scope over time; keep this document and the schema stable so
reruns stay comparable.
