# Configuration

Everything that configures the `umbraco` CLI: the config file and its profiles, the environment
variables, the global options that set the same things, and the order in which they win. For a
walkthrough, start with [Getting started](../getting-started.md); for every option of every
command, see the [command reference](../commands.md).

## Where each setting comes from

A command resolves each setting on its own, and the first source that has a value wins.

| Setting | 1. Option | 2. Environment variable | 3. Config file | Otherwise |
| --- | --- | --- | --- | --- |
| Config file path | `--config <path>` | `UMBRACO_CONFIG` | - | The default path (below) |
| Profile | `--profile <name>`, `-p` | `UMBRACO_PROFILE` | `defaultProfile` | `default` |
| Host | `--host <url>`, `-H` | `UMBRACO_HOST` | The profile's `host` | None: the command fails with `No host configured` |
| Client id | - | `UMBRACO_CLIENT_ID` | The profile's `clientId` | None |
| Client secret | - | `UMBRACO_CLIENT_SECRET` | The profile's `clientSecret` | None |
| Bearer token | `--token <bearer>` | `UMBRACO_TOKEN` | - | Exchanged for the client id and secret |
| Output format | `--output`, `-o` | `UMBRACO_OUTPUT` | - | `json` when stdout is piped, `human` in a terminal |
| Read-only mode | `--readonly` | `UMBRACO_READONLY` | - | Off |
| Dry run | `--dry-run` | `UMBRACO_DRY_RUN` | - | Off |

- The environment variables override the selected profile **field by field**, so CI can run on
  environment variables alone, or override one field of a saved profile.
- A token from `--token` or `UMBRACO_TOKEN` is used as it is, and no client credentials are sent.
- `UMBRACO_READONLY` and `UMBRACO_DRY_RUN` are on for `1`, `true` or `yes` (any case). They can
  only turn the mode on: an option cannot turn it off again.
- An `UMBRACO_OUTPUT` value other than `json`, `human` or `csv` is ignored. An `--output` value
  other than those is an error.
- Command allow-lists do not follow this table: every list in force applies at once (see
  [Command allow-list](#command-allow-list)).

## The config file

`umbraco auth login` writes the config file; you rarely need to edit it by hand.

| Platform | Default path |
| --- | --- |
| Windows | `%APPDATA%\Umbraco\config.json` |
| macOS | `~/Library/Application Support/Umbraco/config.json` |
| Linux | `~/.config/Umbraco/config.json` |

`umbraco --help` prints the path on the current machine, in the description of `--config`.

```json
{
  "defaultProfile": "prod",
  "profiles": {
    "prod": {
      "host": "https://www.example.com",
      "clientId": "umbraco-back-office-cli",
      "clientSecret": "<encrypted secret>",
      "allowedCommands": "content,media"
    },
    "stage": {
      "host": "https://stage.example.com",
      "clientId": "umbraco-back-office-cli",
      "clientSecret": "<encrypted secret>"
    }
  }
}
```

| Field | Meaning |
| --- | --- |
| `defaultProfile` | The profile a command uses when no `--profile` or `UMBRACO_PROFILE` names one. The first profile saved becomes the default. |
| `profiles` | The named profiles. Names are case-insensitive. |
| `host` | The Umbraco site's base URL. |
| `clientId` | The client id of an Umbraco API user. |
| `clientSecret` | That API user's client secret. |
| `allowedCommands` | Optional. A comma-separated list of the commands that may run; see [Command allow-list](#command-allow-list). |

- **Secrets at rest.** On Windows the client secret is encrypted with DPAPI for the current user
  (the stored value starts with `dpapi:`), so a copied file is useless to anyone else. On macOS
  and Linux the secret is stored as is and the file is restricted to its owner (mode 600). A
  plaintext secret left in a file on Windows is encrypted the next time the CLI reads it.
- **A file the CLI cannot read is never overwritten.** `auth login` refuses to save into it, so a
  damaged file cannot silently cost you your other profiles. Fix or remove it, then log in again.
- An older file with `host`, `clientId` and `clientSecret` at the top level is read as a single
  `default` profile.

## Profiles

A profile is one site and the API user the CLI signs in to it as. Keep one per environment:

```bash
umbraco auth login --profile prod  --host https://www.example.com  --client-id <id> --client-secret <secret>
umbraco auth login --profile stage --host https://stage.example.com --client-id <id> --client-secret <secret>

umbraco auth profile list            # every profile; the default is marked
umbraco auth profile use prod        # make prod the default
umbraco content list --profile stage # one command against stage
umbraco auth logout --profile stage  # remove stage
```

- `auth login`, `auth logout` and every other command resolve the profile the same way: the
  option, then `UMBRACO_PROFILE`, then the default.
- Logging out of the default profile leaves no default while other profiles remain: commands
  without `--profile` then fail as not authenticated until `auth profile use` picks one, rather
  than falling back to another site. Logging out of the last profile deletes the file.
- Logging out of a profile that has an `allowedCommands` list clears its credentials but keeps
  the list.

## Command allow-list

An allow-list limits which commands may run. It is meant to be set by whoever supervises a
session, such as a CI job or an AI agent, and nothing the session does can loosen it.

- Set it with `UMBRACO_ALLOWED_COMMANDS`, or with `allowedCommands` on a profile.
- Each entry is a noun group (`content`) or a full command (`content.list`), compared without
  regard to case. Separate entries with commas: `content,media,document-type.list`.
- Every list in force applies, and a command must be on all of them: the environment variable,
  every profile's list in the default config file, and, with `--config` or `UMBRACO_CONFIG`, every
  profile's list in that file too. Choosing another profile, changing the default or pointing at
  another file can only add restrictions.
- A list that is present but empty (for example `UMBRACO_ALLOWED_COMMANDS=" "`) allows nothing.
- The `auth` commands are always allowed, so a restricted session can still log in and run
  `auth doctor`.

```bash
UMBRACO_READONLY=1 UMBRACO_ALLOWED_COMMANDS=content,media umbraco content list
```

## Where credentials are sent

- Stored or `UMBRACO_CLIENT_ID` / `UMBRACO_CLIENT_SECRET` credentials go only to the host they
  belong to: the profile's host, or `UMBRACO_HOST`. A `--host` naming another site is refused
  unless you pass `--token` with it, or use a profile logged in to that site.
- A host on plain `http://` is refused unless it is a loopback address such as `localhost`.

## Token cache

The CLI caches the short-lived bearer token it gets for a profile, so consecutive commands do not
each request a new one.

| Platform | Cache file |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Umbraco\token-cache.json` |
| macOS | `~/Library/Application Support/Umbraco/token-cache.json` |
| Linux | `~/.local/share/Umbraco/token-cache.json` |

- On macOS and Linux the file is readable only by its owner. The cache holds tokens, never client
  secrets, and `auth logout` removes the profile's tokens.
- Set `UMBRACO_NO_TOKEN_CACHE=1` to turn the cache off, for example on a shared build agent.

## Environment variables

| Variable | Effect |
| --- | --- |
| `UMBRACO_HOST` | The site's base URL; overrides the profile's `host`. |
| `UMBRACO_CLIENT_ID` | The API user's client id; overrides the profile's `clientId`. |
| `UMBRACO_CLIENT_SECRET` | The API user's client secret; overrides the profile's `clientSecret`. |
| `UMBRACO_TOKEN` | A bearer token to use as it is, as `--token`. |
| `UMBRACO_PROFILE` | The profile to use, as `--profile`. |
| `UMBRACO_CONFIG` | The config file to use, as `--config`. |
| `UMBRACO_OUTPUT` | `json`, `human` or `csv`, as `--output`. |
| `UMBRACO_READONLY` | `1`, `true` or `yes` refuses every write, as `--readonly`. |
| `UMBRACO_DRY_RUN` | `1`, `true` or `yes` previews writes instead of sending them, as `--dry-run`. |
| `UMBRACO_ALLOWED_COMMANDS` | A command allow-list; see [Command allow-list](#command-allow-list). |
| `UMBRACO_NO_TOKEN_CACHE` | `1`, `true` or `yes` turns the token cache off. |
| `NO_COLOR` | Present with any value, human output has no colour ([no-color.org](https://no-color.org)). |

Extension commands receive the context options of the command line that launched them as these
same variables; see [Writing an extension command](../extension-commands.md).
