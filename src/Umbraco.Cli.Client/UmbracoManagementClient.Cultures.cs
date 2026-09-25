using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Culture-aware defaults shared by the document write paths (#250 Phase 4). Umbraco has no
/// "every culture" wildcard - <c>"*"</c> is the invariant culture (#158) - so a call that names no
/// culture has to work out the right cultures itself. There are two twins here: one reads a
/// <b>document</b> (for publish, unpublish, versions and blueprint renames, where the document
/// already exists) and one reads a <b>document type</b> (for creates, where it does not).
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>The instance's default language ISO code, read once per client.</summary>
    private string? _defaultCulture;

    /// <summary>
    /// The cultures a document varies by, or a single <c>null</c> culture when it is invariant.
    /// Reading the document is what makes "all cultures" work without a wildcard - see
    /// <see cref="PublishContentAsync"/> and <see cref="UnpublishContentAsync"/>.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The culture codes; a single-element list containing null when invariant.</returns>
    private async Task<List<string?>> DocumentCulturesAsync(Guid id, CancellationToken ct)
    {
        var document = await _api
            .Umbraco.Management.Api.V1.Document[id]
            .GetAsync(cancellationToken: ct);
        return CulturesOf((document?.Variants ?? []).Select(v => v.Culture));
    }

    /// <summary>
    /// Reduces variant cultures to the distinct named ones, or a single <c>null</c> when there are
    /// none (an invariant item has one variant with a null culture).
    /// </summary>
    /// <param name="cultures">The culture of each variant.</param>
    /// <returns>The distinct cultures, or <c>[null]</c> when invariant.</returns>
    internal static List<string?> CulturesOf(IEnumerable<string?> cultures)
    {
        var named = cultures.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        return named.Count > 0 ? named : [null];
    }

    /// <summary>
    /// The instance's default language (the one flagged <c>isDefault</c>), cached for the life of
    /// the client so a bulk run pays for the lookup once.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The default ISO code, or null when no language is flagged as the default.</returns>
    private async Task<string?> DefaultCultureAsync(CancellationToken ct)
    {
        if (_defaultCulture is not null)
            return _defaultCulture;

        var languages = await _api.Umbraco.Management.Api.V1.Language.GetAsync(
            c => c.QueryParameters.Take = 1000,
            ct
        );
        _defaultCulture = (languages?.Items ?? [])
            .FirstOrDefault(l => l.IsDefault == true)
            ?.IsoCode;
        return _defaultCulture;
    }

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
            ?? throw BadRequest(
                "This document type varies by culture and no default language was found; pass --culture."
            );
    }

    /// <summary>
    /// Gives every culture-less variant the type's default culture when the type varies (#228). A
    /// variant that already names a culture is left alone, and nothing is read when every variant
    /// names one, so an explicit <c>--culture</c> or a full <c>--json-body</c> costs no extra call.
    /// </summary>
    /// <param name="variants">The variants about to be sent; updated in place.</param>
    /// <param name="documentTypeId">The document type the item is being created from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the variants are updated.</returns>
    private async Task DefaultVariantCulturesAsync(
        List<Gen.DocumentVariantRequestModel> variants,
        Guid documentTypeId,
        CancellationToken ct
    )
    {
        if (variants.All(v => !string.IsNullOrEmpty(v.Culture)))
            return;

        var culture = await DocumentTypeCultureAsync(documentTypeId, ct);
        if (culture is null)
            return;

        foreach (var variant in variants.Where(v => string.IsNullOrEmpty(v.Culture)))
            variant.Culture = culture;
    }
}
