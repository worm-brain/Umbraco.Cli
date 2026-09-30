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
package's settings, emit a second manifest from C# with an `IPackageManifestReader`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Cms.Infrastructure.Manifest;

internal sealed class CliManifestReader(IOptionsMonitor<MyPackageOptions> options)
    : IPackageManifestReader
{
    public Task<IEnumerable<PackageManifest>> ReadPackageManifestsAsync()
    {
        var manifest = new PackageManifest
        {
            // Your package's id, so the declaration is credited to it.
            Id = "My.Package",
            // Empty on purpose: Packages > Installed lists every manifest that has a name.
            Name = "",
            Extensions =
            [
                // A dictionary, so the keys are written exactly as given.
                new Dictionary<string, object>
                {
                    ["type"] = "umbracoCli",
                    ["alias"] = "My.Package.Cli",
                    ["name"] = "My Package",
                    ["meta"] = new Dictionary<string, string>
                    {
                        ["dictionaryValueFormat"] =
                            options.CurrentValue.Mode == MyMode.Markdown ? "markdown" : "html",
                    },
                },
            ],
        };
        return Task.FromResult<IEnumerable<PackageManifest>>([manifest]);
    }
}

public sealed class CliManifestComposer : IComposer
{
    // Insert at the front rather than AddSingleton: Umbraco's PackagingService takes a single
    // IPackageManifestReader, which resolves to the last one registered. Appending yours would
    // make it see only your manifest and lose every other package on the site.
    public void Compose(IUmbracoBuilder builder) =>
        builder.Services.Insert(
            0,
            ServiceDescriptor.Singleton<IPackageManifestReader, CliManifestReader>()
        );
}
```

Umbraco lists this manifest next to your static one; it does not merge them. It also caches the
manifest list, for 30 days in the `Production` runtime mode and 10 seconds otherwise, so in
production a settings change reaches the CLI after a restart.

## Vocabulary

| Key | Values | What the CLI does with it |
|---|---|---|
| `dictionaryValueFormat` | `text`, `html`, `markdown` | `dictionary get` and `dictionary list` report it as `meta.valueFormat`. `schema diff` and `schema apply` warn when the source and target sites store dictionary values in different formats. |
| `commandTool` | `{ "noun": "foo", "package": "Umbraco.Foo.Cli" }` | Names the .NET tool that adds your package's own commands ([Writing an extension command](extension-commands.md)). `umbraco auth doctor` says whether it is installed, and how to install it when it isn't. |

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
