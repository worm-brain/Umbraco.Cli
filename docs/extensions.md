# Declaring CLI support in your package

If your Umbraco package changes what the `umbraco` CLI's commands should expect, you can say so in
your package manifest. The CLI reads the manifest of whichever site it is talking to, so it knows
what that site expects without anything installed on the user's machine.

For example, a package that stores dictionary values as Markdown declares that, and
`umbraco dictionary get` then tells callers the site's values are Markdown.

## The manifest entry

Add one entry of type `umbracoCli` to the `extensions` array of your `umbraco-package.json`:

```json
{
  "id": "My.Package",
  "name": "My Package",
  "version": "1.2.0",
  "extensions": [
    {
      "type": "umbracoCli",
      "alias": "My.Package.Cli",
      "name": "My Package",
      "meta": { "dictionaryValueFormat": "markdown" }
    }
  ]
}
```

- `type` is always `umbracoCli`. The backoffice ignores this type, so the entry has no effect there.
- `alias` must be unique across the site, like any extension alias. Use your package id plus `.Cli`.
- `name` is shown by `umbraco manifest list`.
- `meta` holds the capabilities you declare, from the vocabulary below. The JSON Schema is
  [umbraco-cli-extension.schema.json](umbraco-cli-extension.schema.json).

### When the value depends on configuration

A static `umbraco-package.json` can't read `appsettings.json`. If what you declare depends on your
package's settings, build the manifest in C# with an `IPackageManifestReader` and register it in a
composer:

```csharp
public class CliManifestReader(IOptionsMonitor<MyPackageOptions> options) : IPackageManifestReader
{
    public Task<IEnumerable<PackageManifest>> ReadPackageManifestsAsync()
    {
        var format = options.CurrentValue.Mode == MyMode.Markdown ? "markdown" : "html";
        IEnumerable<PackageManifest> manifests =
        [
            new PackageManifest
            {
                Id = "My.Package.Cli",
                Name = "My Package (CLI)",
                Extensions =
                [
                    new
                    {
                        type = "umbracoCli",
                        alias = "My.Package.Cli",
                        name = "My Package",
                        meta = new { dictionaryValueFormat = format },
                    },
                ],
            },
        ];
        return Task.FromResult(manifests);
    }
}

public class CliManifestComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.Services.AddSingleton<IPackageManifestReader, CliManifestReader>();
}
```

## Vocabulary

| Key | Values | What the CLI does with it |
|---|---|---|
| `dictionaryValueFormat` | `text`, `html`, `markdown` | `dictionary get` and `dictionary list` report it as `meta.valueFormat`. `schema diff` and `schema apply` warn when the source and target sites store dictionary values in different formats. |

When nothing declares a key, the CLI uses its default (`text` for `dictionaryValueFormat`).

## How the CLI treats declarations

- **It reads the site, not the machine.** The CLI calls `GET /umbraco/management/api/v1/manifest/manifest`
  on the site a command targets, only for commands that need a capability, and at most once per
  command. `umbraco manifest list` shows what each package declares.
- **Declarations inform; they never restrict.** The CLI stores whatever values it is given. A
  declared format tells callers what the site expects, and powers warnings.
- **The commands are the same on every site.** A declaration never adds or removes a command or an
  option, so help, `umbraco commands` and scripts behave the same everywhere.
- **Unknown keys and values are ignored.** A newer package works with an older CLI, and the other
  way round.
- **Conflicts cancel out.** If two packages declare different values for one key, the CLI uses the
  default and warns, naming both packages.

The design and its trade-offs are in [ADR 0009](adr/0009-package-declared-cli-capabilities.md).
