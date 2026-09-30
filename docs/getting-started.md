# Getting started

This takes you from nothing to your first command: install the CLI, create an API user, log
in and check it all works. Setting it up for a script, CI or an AI agent? The
[automation guide](agent-guide.md) covers non-interactive login and the JSON output.

## 1. Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later.
- An Umbraco **17+** instance with the Management API enabled (it is on by default).
- An **API User** on that instance (created in step 3).

## 2. Install

The package is `Umbraco.Community.Cli` on NuGet.org, and the command it installs is
`umbraco`. Keep `--prerelease` until 1.0 is out.

```bash
# Global tool (available everywhere as `umbraco`)
dotnet tool install -g Umbraco.Community.Cli --prerelease
```

Or pin it to a repository as a local tool:

```bash
dotnet new tool-manifest        # once per repo
dotnet tool install Umbraco.Community.Cli --prerelease
```

Check it's on your PATH:

```bash
umbraco --version
```

## 3. Create an API User (get a client id and secret)

The CLI logs in as an
[API User](https://docs.umbraco.com/umbraco-cms/manage-and-publish-content/users-and-members/users/api-users),
using OAuth2 client credentials.

1. Sign in to the Umbraco backoffice as an administrator.
2. Go to **Users** and open the **API Users** area.
3. Create an API user (or open an existing one) and give it the permissions you need. The
   CLI can only do what this user is allowed to do, so if a command returns `403` later, this is
   the place to widen them.
4. Save, then copy the generated **Client ID** and **Client Secret**.
5. Put the secret somewhere safe straight away (a password manager or your CI's secret store).
   Umbraco only shows it once.

## 4. Authenticate

Pick whichever fits how you'll run the CLI.

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

Pass the credentials as environment variables. Nothing is written to disk, and each one
overrides the matching field in the config file:

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

Run the built-in check first. It tests the host, the connection and TLS, your credentials,
login, who you are and the Umbraco version, with a hint for anything that fails. It exits
non-zero if a check fails:

```bash
umbraco auth doctor
```

Then see who you're logged in as:

```bash
umbraco auth whoami
```

## 6. Multiple environments (profiles)

Log in to several sites and switch between them without logging in again:

```bash
umbraco auth login --profile prod  --host https://prod.example.com  --client-id <id> --client-secret <secret>
umbraco auth login --profile stage --host https://stage.example.com --client-id <id> --client-secret <secret>

umbraco auth profile list                     # list profiles; * marks the default
umbraco auth profile use prod                      # make 'prod' the default
umbraco content list --profile stage       # use a profile for one command
UMBRACO_PROFILE=stage umbraco content list  # or via env
umbraco auth logout --profile stage        # remove one profile
```

Profiles live in the same config file. The `UMBRACO_HOST`, `UMBRACO_CLIENT_ID` and
`UMBRACO_CLIENT_SECRET` environment variables override the selected profile's fields, so CI can
run on environment variables alone.

## 7. Where configuration lives

`umbraco auth login` writes to an OS-specific config file:

- **Windows:** `%APPDATA%\Umbraco\config.json`
- **macOS:** `~/Library/Application Support/Umbraco/config.json`
- **Linux:** `~/.config/Umbraco/config.json`

Override the location with `--config <path>`. Environment variables (`UMBRACO_HOST` /
`UMBRACO_CLIENT_ID` / `UMBRACO_CLIENT_SECRET`) take precedence over the file, per field, so you
can keep a saved default and override one value for a single run.

### The token cache

Access tokens last about five minutes and are cached between runs, so a script that runs lots
of commands only logs in once. The cache is its own file, readable only by you:

- **Windows:** `%LOCALAPPDATA%\Umbraco\token-cache.json`
- **macOS:** `~/Library/Application Support/Umbraco/token-cache.json`
- **Linux:** `~/.local/share/Umbraco/token-cache.json`

Tokens are keyed by host, client id and a fingerprint of the secret (never the secret
itself), so changing the secret always logs in again. If the server rejects a cached token, the
CLI gets a new one. Set `UMBRACO_NO_TOKEN_CACHE=1` to turn the cache off, for example on a
shared build agent. `auth login` and `auth doctor` always log in afresh.

## 8. First commands

```bash
umbraco content list                                   # list content (human table in a terminal)
umbraco content list --output json | jq '.data[].name' # JSON for scripting
umbraco content get <id>                               # one item
umbraco content create --document-type blogPost --name "Hello World"
umbraco commands                                       # the entire command tree as JSON
```

### Querying JSON output

Some examples pipe JSON into [`jq`](https://jqlang.github.io/jq/), a small command-line JSON
tool. `jq '.data[].name'` means "print the `name` of every item in `data`". `jq` is a separate
install, so if you don't have it, either of these works with nothing extra:

- **Use `--fields`** to pick the fields you want. It covers most cases:
  `umbraco content list --fields id,name`.
- **In PowerShell, use `ConvertFrom-Json`:**

  ```powershell
  # every name
  (umbraco content list --output json | ConvertFrom-Json).data.name
  # one field, ready to pipe on to another command
  (umbraco content list --fields id --output json | ConvertFrom-Json).data.id
  ```

  PowerShell unrolls arrays for you, so `.data.name` gives every item's name without a loop.

JSON output is always `{ "status", "data", "meta" }`, so the result lives under `.data`. That's
why the examples say `.data[]` rather than `.[]`. The `jq` examples in these docs are written
for bash; the `ConvertFrom-Json` form above is the PowerShell equivalent.

The full command reference is in [commands.md](commands.md).

## 9. Troubleshooting

Start with `umbraco auth doctor`. It tells you which step is failing. Common cases:

| Symptom | Likely cause | Fix |
|---|---|---|
| `No host configured` (exit 2) | No `--host`, no `UMBRACO_HOST`, no saved profile. | Run `auth login`, or set `UMBRACO_HOST`. |
| `Not authenticated` (exit 2) | Missing/invalid client id or secret. | Re-check the API user's credentials; re-run `auth login`. |
| `Refusing to send credentials ... over plain HTTP` (exit 2) | The host is `http://` and not `localhost` / `127.0.0.1` / `::1`. | Use the instance's `https://` URL. |
| `--host '...' is not the host the configured credentials belong to` (exit 2) | `--host` names a different instance than the profile or `UMBRACO_HOST`. | Pass `--token` with `--host`, or log in a profile for that host and use `--profile`. |
| TLS / connection failure in `auth doctor` | Wrong host, self-signed cert, or instance down. | Verify the URL in a browser; for local dev, use the real https URL. |
| `401` on a real command | Token could not be obtained. | Confirm client id/secret; run `auth doctor`. |
| `403` on a real command | The API user lacks that permission. | Widen the API user's permissions in the backoffice (step 3). |
| A destructive command "hangs" or aborts with exit 2 in a script | It is waiting for confirmation, or refused it because there is no TTY. | Pass `--yes`. See [agent-guide.md](agent-guide.md#7-running-non-interactively-the-rules-that-bite). |
| A write fails with a read-only error | `--readonly` or `UMBRACO_READONLY=1` is set. | Unset it, or use a read command. |

Stuck on something else? `umbraco <command> --help` explains any command, and you're welcome
to [open an issue](https://github.com/worm-brain/Umbraco.Cli/issues).
