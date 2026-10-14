# Security policy

## Supported versions

Security fixes go into the newest release of `Umbraco.Community.Cli`, including pre-releases while
the CLI is below 1.0. Update with `dotnet tool update -g Umbraco.Community.Cli --prerelease`.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately through GitHub: on the repository's **Security** tab, choose **Report a
vulnerability**. Include what you found, how to reproduce it, the CLI version
(`umbraco --version`) and the Umbraco version you used.

You will get an acknowledgement, and the fix will be coordinated with you before any details are
published.

## Scope

The CLI holds credentials for Umbraco sites and changes their content, so reports about these are
especially welcome:

- a client secret or token being written, logged or printed where it should not be (the config
  file, the token cache, `--verbose` output, error messages);
- credentials being sent to a host they do not belong to, or over plain `http://`;
- a write getting past `--readonly`, `--dry-run`, the command allow-list or a confirmation prompt;
- server responses or files that make the CLI run code, write outside the path it was given, or
  inject terminal escapes into its output;
- an extension command being launched from somewhere it should not be.

[docs/security-audit.md](docs/security-audit.md) describes how the CLI is audited, and
[docs/agent-guide.md](docs/agent-guide.md) the guardrails it is designed to enforce.
