using System.Text.Json.Nodes;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Culture-aware defaults shared by the document paths (#250 Phase 4). Umbraco has no "every
/// culture" wildcard - <c>"*"</c> is the invariant culture (#158) - so a call that names no culture
/// has to work out the right cultures itself. Each resolver here answers "which culture(s)?" from
/// one source: a <b>document</b> (publish, unpublish, versions, where the item exists), a
/// <b>document type</b> (creates, where it does not), or an item's <b>current variants</b> (an
/// update that has already read the item). <see cref="WithCulture"/> is the one fill step.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>The instance's languages, read once per client (only a non-empty answer is kept).</summary>
    private List<Gen.LanguageResponseModel>? _languages;

    /// <summary>
    /// The instance's languages, cached for the life of the client so a batch (dictionary creates,
    /// bulk content creates) pays for the lookup once.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The languages, or null when the list could not be read.</returns>
    private async Task<List<Gen.LanguageResponseModel>?> LanguagesAsync(CancellationToken ct)
    {
        if (_languages is not null)
            return _languages;

        var page = await _api.Umbraco.Management.Api.V1.Language.GetAsync(
            c => c.QueryParameters.Take = 1000,
            ct
        );
        var languages = (page?.Items ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.IsoCode))
            .ToList();

        // Umbraco always has at least one language, so an empty list means the read did not work.
        // Only a real answer is cached.
        if (languages.Count == 0)
            return null;

        _languages = languages;
        return languages;
    }

    /// <summary>The ISO codes of every language on the instance (see <see cref="LanguagesAsync"/>).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The codes, or null when the language list could not be read.</returns>
    private async Task<HashSet<string>?> KnownIsoCodesAsync(CancellationToken ct) =>
        (await LanguagesAsync(ct))
            ?.Select(l => l.IsoCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The instance's default language (the one flagged <c>isDefault</c>).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The default ISO code, or null when none is flagged or the list could not be read.</returns>
    private async Task<string?> DefaultCultureAsync(CancellationToken ct) =>
        (await LanguagesAsync(ct))?.FirstOrDefault(l => l.IsDefault == true)?.IsoCode;

    /// <summary>
    /// The cultures a document varies by; empty when it is invariant. Reading the document is what
    /// makes "all cultures" work without a wildcard - see <see cref="PublishContentAsync"/> and
    /// <see cref="UnpublishContentAsync"/>.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The distinct culture codes; empty when the document is invariant.</returns>
    private async Task<List<string>> DocumentCulturesAsync(Guid id, CancellationToken ct)
    {
        var document = await _api
            .Umbraco.Management.Api.V1.Document[id]
            .GetAsync(cancellationToken: ct);
        return NamedCultures((document?.Variants ?? []).Select(v => v.Culture));
    }

    /// <summary>The distinct non-empty cultures; empty for an invariant item (one null-culture variant).</summary>
    /// <param name="cultures">The culture of each variant.</param>
    /// <returns>The distinct named cultures.</returns>
    private static List<string> NamedCultures(IEnumerable<string?> cultures) =>
        cultures.Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).Distinct().ToList();

    /// <summary>
    /// The culture a create should use when the caller named none: null for a document type that
    /// is invariant, the default language for one that varies by culture (#228). Without this a
    /// flags-only create on a variant type sends a null culture and Umbraco answers
    /// <c>400 "The content type variance did not match..."</c>.
    /// </summary>
    /// <param name="documentTypeId">The document type being created.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The culture to use, or null when the type is invariant.</returns>
    /// <exception cref="Microsoft.Kiota.Abstractions.ApiException">
    /// A 400 when the type varies but the instance has no default language to fall back on.
    /// </exception>
    private async Task<string?> DocumentTypeCultureAsync(Guid documentTypeId, CancellationToken ct)
    {
        var type = await _api
            .Umbraco.Management.Api.V1.DocumentType[documentTypeId]
            .GetAsync(cancellationToken: ct);
        if (type?.VariesByCulture != true)
            return null;

        return await DefaultCultureAsync(ct)
            ?? throw new InvalidArgumentException(
                "This document type varies by culture and no default language was found; pass --culture."
            );
    }

    /// <summary>
    /// The culture an update of an existing item should give a culture-less variant (#228,
    /// blueprint <c>update --name</c>): the default language, when the item varies by culture and
    /// has a variant in it. Null otherwise, which leaves the variant as it is so
    /// <see cref="DocumentUpdateBody"/>'s guard can still refuse an ambiguous rename with the item's
    /// cultures listed.
    /// </summary>
    /// <param name="current">The item's current <c>variants</c> array, as read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The culture to use, or null.</returns>
    private async Task<string?> ExistingItemCultureAsync(JsonArray? current, CancellationToken ct)
    {
        var cultures = NamedCultures(
            (current ?? []).Select(v => v?["culture"]?.GetValue<string?>())
        );
        if (cultures.Count == 0)
            return null;

        var fallback = await DefaultCultureAsync(ct);
        return fallback is not null && cultures.Contains(fallback, StringComparer.OrdinalIgnoreCase)
            ? fallback
            : null;
    }

    /// <summary>
    /// The one fill step: gives every culture-less variant the culture <paramref name="resolve"/>
    /// returns. <paramref name="resolve"/> is only called when some variant names no culture, so a
    /// request that names all its cultures costs no extra read; when it returns null (an invariant
    /// type or item, or no default-language variant), every variant is returned unchanged.
    /// </summary>
    /// <param name="variants">The requested variants.</param>
    /// <param name="resolve">Works out the culture to fill in: one of the resolvers above.</param>
    /// <returns>The variants, with the culture filled in where it was missing.</returns>
    private static async Task<List<ContentVariant>> FillCultureAsync(
        IEnumerable<ContentVariant> variants,
        Func<Task<string?>> resolve
    )
    {
        var requested = variants.ToList();
        if (!requested.Any(v => string.IsNullOrEmpty(v.Culture)))
            return requested;
        var culture = await resolve();
        return culture is null
            ? requested
            :
            [
                .. requested.Select(v =>
                    string.IsNullOrEmpty(v.Culture) ? v with { Culture = culture } : v
                ),
            ];
    }
}
