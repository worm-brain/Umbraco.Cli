using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>
/// Reads what the target site's packages declare about the CLI's commands (ADR 0009, #440).
/// </summary>
public interface ISiteCapabilitiesClient
{
    /// <summary>
    /// Reads the site's package manifests and resolves every <c>umbracoCli</c> declaration in them
    /// against the CLI's vocabulary. Lazy and cached per client, like the server version: the
    /// manifest is read at most once per invocation, and only by commands that ask. Never fails -
    /// when the manifests cannot be read, the result says why in
    /// <see cref="SiteCapabilities.Unavailable"/> and carries no declared values.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The site's capabilities.</returns>
    Task<SiteCapabilities> GetSiteCapabilitiesAsync(CancellationToken ct = default);
}

/// <summary>
/// What the target site's packages declare about the CLI's commands: the <c>meta</c> of every
/// <c>umbracoCli</c> entry in the site's package manifests, resolved against the CLI's vocabulary
/// (docs/extensions.md). Unknown keys and values are ignored, and a key two packages disagree on
/// resolves to its default with a warning (ADR 0009, section 3).
/// </summary>
public sealed record SiteCapabilities
{
    /// <summary>The manifest extension type a package declares CLI support with.</summary>
    public const string ExtensionType = "umbracoCli";

    /// <summary>The vocabulary key for the format dictionary translations are stored in.</summary>
    public const string DictionaryValueFormatKey = "dictionaryValueFormat";

    /// <summary>The dictionary value format when no package declares one.</summary>
    public const string DefaultDictionaryValueFormat = "text";

    /// <summary>The values <see cref="DictionaryValueFormatKey"/> may take; anything else is ignored.</summary>
    public static readonly IReadOnlyList<string> DictionaryValueFormats =
    [
        "text",
        "html",
        "markdown",
    ];

    /// <summary>
    /// The format this site stores dictionary translations in: <c>text</c>, <c>html</c> or
    /// <c>markdown</c>. Null when the manifests could not be read, because "unknown" is not the
    /// same as "plain text".
    /// </summary>
    public string? DictionaryValueFormat { get; init; } = DefaultDictionaryValueFormat;

    /// <summary>Problems with the declarations, such as two packages disagreeing on a key.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Why the site's manifests could not be read, or null when they were.</summary>
    public string? Unavailable { get; init; }

    /// <summary>The capabilities of a site whose manifests could not be read.</summary>
    /// <param name="reason">Why they could not be read.</param>
    /// <returns>Capabilities with no known values.</returns>
    public static SiteCapabilities None(string reason) =>
        new() { DictionaryValueFormat = null, Unavailable = reason };

    /// <summary>Resolves the declarations in a site's manifests against the vocabulary.</summary>
    /// <param name="manifests">The site's manifests, as <see cref="IManifestClient"/> lists them.</param>
    /// <returns>The resolved capabilities.</returns>
    public static SiteCapabilities From(IEnumerable<ManifestResponse> manifests)
    {
        // Every package that declares a value we recognise, in manifest order.
        var declared = manifests
            .Where(m => m.CliCapabilities is not null)
            .Select(m =>
                (
                    Package: PackageName(m),
                    Value: KnownValue(
                        m.CliCapabilities![DictionaryValueFormatKey],
                        DictionaryValueFormats
                    )
                )
            )
            .Where(d => d.Value is not null)
            .ToList();

        var values = declared.Select(d => d.Value!).Distinct(StringComparer.Ordinal).ToList();
        if (values.Count <= 1)
            return new()
            {
                DictionaryValueFormat = values.SingleOrDefault() ?? DefaultDictionaryValueFormat,
            };

        // Two packages disagree. Picking one would be wrong half the time, so use the default
        // and say which packages said what.
        var who = string.Join(", ", declared.Select(d => $"{d.Package} declares '{d.Value}'"));
        return new()
        {
            Warnings =
            [
                $"packages disagree on {DictionaryValueFormatKey} ({who}); "
                    + $"using '{DefaultDictionaryValueFormat}'.",
            ],
        };
    }

    /// <summary>
    /// Merges the <c>meta</c> of every <c>umbracoCli</c> entry in a manifest's
    /// <c>extensions</c> array into one object (a later entry's key wins).
    /// </summary>
    /// <param name="extensions">The manifest's <c>extensions</c>, as the API returned them.</param>
    /// <returns>The merged <c>meta</c>, or null when the manifest declares nothing.</returns>
    public static JsonObject? DeclaredIn(JsonNode? extensions)
    {
        if (extensions is not JsonArray entries)
            return null;

        JsonObject? merged = null;
        foreach (var entry in entries)
        {
            if (
                entry is not JsonObject extension
                || extension["type"]?.GetValueKind() != System.Text.Json.JsonValueKind.String
                || extension["type"]!.GetValue<string>() != ExtensionType
                || extension["meta"] is not JsonObject meta
            )
                continue;

            merged ??= [];
            foreach (var (key, value) in meta)
                merged[key] = value?.DeepClone();
        }
        return merged;
    }

    /// <summary>The name a warning uses for a package: its manifest id, or its name when it has none.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>The package's name.</returns>
    private static string PackageName(ManifestResponse manifest) =>
        string.IsNullOrWhiteSpace(manifest.Id) ? manifest.Name : manifest.Id;

    /// <summary>A declared value if it is a string in <paramref name="allowed"/>, otherwise null.</summary>
    /// <param name="value">The declared value.</param>
    /// <param name="allowed">The values the key may take.</param>
    /// <returns>The value, or null when it is missing, not a string, or not allowed.</returns>
    private static string? KnownValue(JsonNode? value, IReadOnlyList<string> allowed) =>
        value is JsonValue v
        && v.TryGetValue<string>(out var s)
        && allowed.Contains(s, StringComparer.Ordinal)
            ? s
            : null;
}
