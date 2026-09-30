# ADR 0009: Packages declare CLI support in their Umbraco manifest

- Status: Accepted
- Date: 2026-09-30
- Issue: #439 (epic #437). Related: #440 (the reader), #442 (schema snapshots), #438 (package-provided commands).

## Context

Some Umbraco packages change what the CLI's existing commands should expect. The first is
Umbraco.RichDictionary: it keeps dictionary values in the core `translations[].translation`
string, but as HTML or Markdown, chosen once for the whole site by its `EditorMode` setting. Nothing
in the Management API says so, so a user or an agent writing a value with `dictionary update` has to
guess what the site expects.

Two facts shape the answer:

- **The switch is the target site, not the local install.** One profile points at a site with the
  package and another at a site without it. Anything installed on the user's machine cannot tell
  them apart; only the site can.
- **More packages will follow, most of them not ours.** Knowledge of individual packages compiled
  into the CLI would not extend to them.

## Decision

### 1. One extension type, owned by the CLI

A package declares its CLI support as an entry in its package manifest's `extensions` array, with
the type `umbracoCli`:

```json
{
  "type": "umbracoCli",
  "alias": "Umbraco.RichDictionary.Cli",
  "name": "Rich Dictionary",
  "meta": { "dictionaryValueFormat": "markdown" }
}
```

The CLI owns the vocabulary of `meta` keys ([docs/extensions.md](../extensions.md), with a JSON
Schema in [docs/umbraco-cli-extension.schema.json](../umbraco-cli-extension.schema.json)). Version 1
has one key:

| Key | Values | Meaning |
|---|---|---|
| `dictionaryValueFormat` | `text`, `html`, `markdown` | The format this site stores dictionary translations in. `text` when nothing declares it. |
| `commandTool` | `{ "noun": "foo", "package": "Umbraco.Foo.Cli" }` | The .NET tool that adds the package's own commands ([ADR 0010](0010-external-extension-commands.md)). Added with #438. |

### 2. The CLI reads declarations from the manifest endpoint

The CLI reads `GET /umbraco/management/api/v1/manifest/manifest`, which `manifest list` already
calls. It works in every runtime mode (the backoffice itself needs it), needs no new server code, and
each entry carries the package's id and version. The reader asks once per invocation, and only when
a command needs a capability, so every other command keeps its request count.

A value that depends on site configuration, such as RichDictionary's `EditorMode`, is emitted from a
C# `IPackageManifestReader` (in `Umbraco.Cms.Infrastructure.Manifest`; `PackageManifest.Extensions`
is a raw object array). A value that never changes can sit in the package's static
`umbraco-package.json`.

### 3. Unknown keys and values are ignored

A key the CLI does not know, or a value outside a key's list, is ignored as if it were not there. The
vocabulary can then grow without breaking older CLIs or newer packages.

When two packages declare different values for the same key, the CLI uses neither and reports a
warning naming both packages. Guessing which one wins would be wrong half the time.

### 4. No package names in the CLI's code

The CLI never mentions a specific package. A second package that stores rich dictionary values gets
the same support by declaring the same key.

### 5. The command tree is the same on every site

Declarations change what the CLI **reports and warns about**, never which commands or options exist:

- The tree is built before the host is known (`--host`, `--profile`, `--config` are parsed with it).
- `docs/surface.json`, `HelpTextTests` and `umbraco commands` assume one surface.
- Agents cache the command catalog, so options that come and go by site would mislead them.

### 6. No validation

A declaration never makes the CLI refuse a value. `dictionary create` and `dictionary update` store
whatever they are given, on every site. The declared format is information for the caller (and, in
`schema diff` / `apply`, a warning when source and target disagree), not a gate.

## Checks on Umbraco 17.7.0

Run against the dev site (`tests/hands-on/dev-site.py`), which now carries a fixture package,
`Umbraco.Cli.Fixture`, that declares `dictionaryValueFormat: markdown` from a static
`wwwroot/App_Plugins/Umbraco.Cli.Fixture/umbraco-package.json`.

- **The endpoint returns extensions verbatim.** Each manifest comes back as
  `{ name, id, version, cacheBuster, extensions }`, with the `umbracoCli` entry exactly as written.
- **The API user can read it.** The CLI's client-credentials user gets `200` from `/manifest/manifest`
  and `/manifest/manifest/private`, which both include the fixture. `/manifest/manifest/public` lists
  only manifests with `allowPublicAccess`, so the CLI reads the full list.
- **Manifests are cached, briefly outside production.** `PackageManifestService` caches the
  combined list under one key, for 10 seconds in every runtime mode but `Production` and for 30 days
  in `Production` (a fixed expiry, not sliding). On the dev site a new manifest, and a changed value
  in an existing one, showed up within seconds without a restart. In production a changed
  declaration reaches the CLI after a restart, which is when an `appsettings.json` change applies
  anyway.
- **The backoffice ignores the type.** The extension registry
  (`umbraco/backoffice/libs/extension-api`) checks only that an extension has a `type` and an `alias`
  and that the alias is unique, then stores it; only code that subscribes to a type ever reads it.
  Nothing subscribes to `umbracoCli`, so the entry is inert. The alias must still be unique across
  the site, so packages use `<PackageId>.Cli`.
- **A C# `IPackageManifestReader` works alongside a static `umbraco-package.json`.** Checked with
  Umbraco.RichDictionary's reader (worm-brain/Umbraco.RichDictionary#12) on 17.7.0. Umbraco does not
  merge manifests by id: the endpoint lists the C# manifest and the static one as separate entries,
  and the CLI reads the declaration from whichever carries it. Two things matter when registering
  such a reader:
  - Register it with `Services.Insert(0, ...)`, not `AddSingleton`. `PackagingService` (package
    telemetry and migration status) takes a single `IPackageManifestReader`, which resolves to the
    last registration. Appending a package's reader would hide every other package from it.
  - Give the C# manifest the package's own id and an empty name. Packages > Installed lists every
    manifest that has a name, so a named second manifest would show as a second installed package.

## Alternatives rejected

- **Plugins loaded into the CLI process** (third-party assemblies discovered at startup). They would
  freeze pre-1.0 internals such as `CommandExecutor` and `CommandSafety` as a public API, invite
  version conflicts over System.CommandLine, Kiota and Microsoft.Extensions.DI inside one global
  tool, and load third-party code into a process that holds client secrets. They also answer the
  wrong question: they say what is installed locally, not what the target site has.
- **A list of known packages in the CLI** (for example, detect `Umbraco.RichDictionary` by id and
  call its own configuration endpoint). Simplest for one package, but every new package needs a CLI
  release, and third-party authors cannot add themselves.
- **A package's own Swagger document.** Swagger is often switched off in production, and its shape
  says nothing about what the CLI should do.
- **A server-side companion package** that aggregates capabilities behind one endpoint. It would make
  every package depend on it and every site install it, for what one manifest entry already does.

## Consequences

- Package authors add one manifest entry, documented in [docs/extensions.md](../extensions.md).
- A command that needs a capability costs one extra request (the manifest), and only that command.
- The vocabulary is a public contract: keys are added, never renamed or repurposed.
- Packages that need their own commands, rather than changes to existing ones, are a separate
  mechanism (#438).
