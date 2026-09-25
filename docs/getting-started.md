# Getting started

Install the CLI, point it at an Umbraco instance, authenticate, and confirm it works. If you
are an AI agent driving the tool rather than a person setting it up, read
[agent-guide.md](agent-guide.md) instead - it covers non-interactive auth and the output
contract.

## 1. Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later.
- An Umbraco **14+** instance with the Management API enabled (it is on by default in 14+).
- An **API User** on that instance (created in step 3).

## 2. Install

The package is `Umbraco.Community.Cli` on NuGet.org; the command it installs is `umbraco`.
While the tool is in alpha, pass `--prerelease`.

```bash
# Global tool (available everywhere as `umbraco`)
dotnet tool install -g Umbraco.Community.Cli --prerelease
```

Or pin it to a repository as a local tool:

```bash
dotnet new tool-manifest        # once per repo
dotnet tool install Umbraco.Community.Cli --prerelease
```

Confirm it is on your PATH:

```bash
umbraco --version
```

## 3. Create an API User (get a client id and secret)

Umbraco.Cli authenticates as an
[API User](https://docs.umbraco.com/umbraco-cms/manage-and-publish-content/users-and-members/users/api-users)
using the OAuth2 Client Credentials flow.

1. Sign in to the Umbraco backoffice as an administrator.
2. Go to **Users** and open the **API Users** area.
3. Create a new API user (or open an existing one) and assign the permissions your CLI usage
   needs. The CLI can only do what this user is allowed to do - if a command later returns
   `403`, widen this user's permissions.
4. Save, then copy the generated **Client ID** and **Client Secret**.
5. Store the secret somewhere safe immediately (password manager or CI secret store) - Umbraco
   shows the secret only once.

## 4. Authenticate

Pick the method that matches how you will run the CLI.

### Interactive (a person at a terminal)

```bash
umbraco auth login
# Prompts for host, client id, and client secret, then saves them.
```

or supply the values as flags:

```bash
umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>
```

The credentials are written to the OS config file (see step 7). The secret is **DPAPI-encrypted
at rest on Windows**; on macOS/Linux the file is restricted to your user (mode 600).

### Non-interactive (CI/CD, scripts, agents)

Provide credentials by environment variable - nothing is written to disk, and these override
the config file per field:

```bash
export UMBRACO_HOST=https://mysite.com
export UMBRACO_CLIENT_ID=umbraco-back-office-my-api-user
export UMBRACO_CLIENT_SECRET=<secret>

umbraco content list --output json
```

### One-off with a raw bearer token

```bash
umbraco content list --token <bearer-token> --host https://mysite.com
```

## 5. Confirm it works

Run the built-in diagnostic before anything else. It checks host resolution, connectivity and
TLS, credentials, authentication, the resolved identity, and the instance version - each as a
pass/fail with a remediation hint - and exits non-zero if any hard check fails:

```bash
umbraco auth doctor
```

Then confirm who you are authenticated as:

```bash
umbraco auth whoami
```

## 6. Multiple environments (profiles)

Log in to several instances and switch between them without re-authenticating:

```bash
umbraco auth login --profile prod  --host https://prod.example.com  --client-id <id> --client-secret <secret>
umbraco auth login --profile stage --host https://stage.example.com --client-id <id> --client-secret <secret>

umbraco auth profiles                     # list profiles; * marks the default
umbraco auth use prod                      # make 'prod' the default
umbraco content list --profile stage       # use a profile for one command
UMBRACO_PROFILE=stage umbraco content list  # or via env
umbraco auth logout --profile stage        # remove one profile
```

Profiles live in the same (owner-only, secret-encrypted) config file. The `UMBRACO_HOST` /
`UMBRACO_CLIENT_ID` / `UMBRACO_CLIENT_SECRET` env vars override the selected profile's fields,
so CI can still run on pure environment credentials. A pre-profiles (flat) config is migrated
to a `default` profile automatically.

## 7. Where configuration lives

`umbraco auth login` writes to an OS-specific config file:

- **Windows:** `%APPDATA%\Umbraco\config.json`
- **macOS:** `~/Library/Application Support/Umbraco/config.json`
- **Linux:** `~/.config/Umbraco/config.json`

Override the location with `--config <path>`. Environment variables (`UMBRACO_HOST` /
`UMBRACO_CLIENT_ID` / `UMBRACO_CLIENT_SECRET`) take precedence over the file, per field, so you
can keep a saved default and override one value for a single run.

### The token cache

Access tokens (valid for about five minutes) are cached between runs, so a script running many
commands exchanges its credentials once rather than per command. The cache is a separate,
owner-only file in the per-user local data folder:

- **Windows:** `%LOCALAPPDATA%\Umbraco\token-cache.json`
- **macOS:** `~/Library/Application Support/Umbraco/token-cache.json`
- **Linux:** `~/.local/share/Umbraco/token-cache.json`

It is keyed by host, client id and a fingerprint of the secret (never the secret itself), so a
changed secret always re-authenticates. A token the server rejects is dropped and renewed
automatically. Set `UMBRACO_NO_TOKEN_CACHE=1` to turn the cache off (for example on a shared
build agent). `auth login` and `auth doctor` always exchange the credentials afresh.

## 8. First commands

```bash
umbraco content list                                   # list content (human table in a terminal)
umbraco content list --output json | jq '.data[].name' # JSON for scripting
umbraco content get <id>                               # one item
umbraco content create --content-type blogPost --name "Hello World"
umbraco commands                                       # the entire command tree as JSON
```

### Querying JSON output

Several examples pipe JSON into [`jq`](https://jqlang.github.io/jq/) - a small, popular
command-line JSON processor. `jq '.data[].name'` reads as "for each item in the `data` array,
print its `name`". **`jq` is a separate tool, not part of this CLI or of your operating system**;
if it is not installed, you have two alternatives that need nothing extra:

- **Use `--fields`** to project fields without any external tool - it covers most cases:
  `umbraco content list --fields id,name`.
- **On Windows, use PowerShell's built-in `ConvertFrom-Json`:**

  ```powershell
  # every name
  (umbraco content list --output json | ConvertFrom-Json).data.name
  # one field, ready to pipe on to another command
  (umbraco content list --fields id --output json | ConvertFrom-Json).data.id
  ```

  PowerShell auto-enumerates arrays, so `.data.name` yields every item's name with no explicit loop.

Every success envelope is `{ "status", "data", "meta" }`, so the payload always lives under
`.data` (this is why the examples say `.data[]`, not `.[]`) - including a `--dry-run` preview,
which uses `{ "status": "dry-run", "data": { ... }, "meta": { ... } }`. The `jq` pipes elsewhere in the docs
are written for bash/macOS/Linux; the `ConvertFrom-Json` form above is the PowerShell equivalent.

The full command reference is in [commands.md](commands.md).

## 9. Troubleshooting

Run `umbraco auth doctor` first - it names the failing stage. Common cases:

| Symptom | Likely cause | Fix |
|---|---|---|
| `No host configured` (exit 2) | No `--host`, no `UMBRACO_HOST`, no saved profile. | Run `auth login`, or set `UMBRACO_HOST`. |
| `Not authenticated` (exit 2) | Missing/invalid client id or secret. | Re-check the API user's credentials; re-run `auth login`. |
| TLS / connection failure in `auth doctor` | Wrong host, self-signed cert, or instance down. | Verify the URL in a browser; for local dev, use the real https URL. |
| `401` on a real command | Token could not be obtained. | Confirm client id/secret; run `auth doctor`. |
| `403` on a real command | The API user lacks that permission. | Widen the API user's permissions in the backoffice (step 3). |
| A destructive command "hangs" or aborts with exit 2 in a script | It is waiting for confirmation, or refused it because there is no TTY. | Pass `--yes`. See [agent-guide.md](agent-guide.md#7-running-non-interactively-the-rules-that-bite). |
| A write fails with a read-only error | `--readonly` or `UMBRACO_READONLY=1` is set. | Unset it, or use a read command. |

For anything not covered here, `umbraco <command> --help` and `umbraco commands` describe the
surface, and issues are welcome (see [CONTRIBUTING.md](../CONTRIBUTING.md)).
