using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// The content write path that cannot go through the generated client without losing data
/// (#178/#179/#162). <c>PUT /document/{id}</c> replaces rather than patches, so a typed request
/// deletes every field it does not model - which is how the template and all unlisted property
/// values were being silently cleared. These methods read the document verbatim and patch it
/// instead, the same approach as <c>UpdateRawScalarsAsync</c> and ADR 0005.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<ContentItemResponse>> UpdateContentAsync(
        Guid id,
        UpdateContentRequest request,
        WriteMode mode = WriteMode.Merge,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var path = DocumentPath(id);
                var document =
                    await GetRawJsonAsync(path, ct) as JsonObject
                    ?? throw new ApiException("The document body was not a JSON object.");

                // A culture-less rename on a variant document means the default language, as it
                // does for blueprint update (#264); the merge's guard still refuses it when the
                // document has no variant in that language. Values are left as sent.
                var variants = await FillCultureAsync(
                    request.Variants,
                    () => ExistingItemCultureAsync(document["variants"] as JsonArray, ct)
                );
                DocumentUpdateBody.Merge(document, request.Values, variants, mode);

                // Only touch the template when the caller asked for one. Omitting it means "leave
                // it alone", never "remove it" - see UpdateContentRequest.Template.
                if (request.Template is { } template)
                    document["template"] = new JsonObject
                    {
                        ["id"] = (await TemplateIdAsync(template, ct)).ToString(),
                    };

                await SendRawJsonAsync(Method.PUT, path, document, ct);

                var hydrated = await GetContentByIdAsync(id, ct);
                if (hydrated.IsSuccess && hydrated.Data is { } data)
                    return data;
                return new ContentItemResponse { Id = id };
            }
        );

    /// <summary>The by-id document path used by the raw read and write.</summary>
    /// <param name="id">The document id.</param>
    /// <returns>The path, relative to the host root.</returns>
    private static string DocumentPath(Guid id) => $"umbraco/management/api/v1/document/{id}";

    /// <summary>
    /// Resolves a template reference to an id: the id when one was given, otherwise the alias
    /// looked up. Shared by the content create and update paths (#162).
    /// </summary>
    /// <param name="template">The requested template, by id or alias.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The template's id.</returns>
    /// <exception cref="ApiException">Neither an id nor a resolvable alias was supplied.</exception>
    private async Task<Guid> TemplateIdAsync(
        ContentTemplateReference template,
        CancellationToken ct
    ) =>
        template.Id
        ?? await IdOfAsync(
            EntityKind.Template,
            template.Alias
                ?? throw new ApiException("A template reference needs either an id or an alias."),
            ct
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<DomainsResponse>> GetDomainsAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var d = await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .Domains.GetAsync(cancellationToken: ct);
                return MapDomains(d?.DefaultIsoCode, d?.Domains);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<DomainsResponse>> SetDomainsAsync(
        Guid id,
        SetDomainsRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Document[id]
                    .Domains.PutAsync(
                        new Gen.UpdateDomainsRequestModel
                        {
                            DefaultIsoCode = request.DefaultIsoCode,
                            Domains =
                            [
                                .. request.Domains.Select(d => new Gen.DomainPresentationModel
                                {
                                    DomainName = d.DomainName,
                                    IsoCode = d.IsoCode,
                                }),
                            ],
                        },
                        cancellationToken: ct
                    );

                // Report what the instance kept rather than echoing the request (#181's lesson).
                var stored = await GetDomainsAsync(id, ct);
                return stored.IsSuccess && stored.Data is { } d
                    ? d
                    : new DomainsResponse
                    {
                        DefaultIsoCode = request.DefaultIsoCode,
                        Domains = request.Domains.ToList(),
                    };
            }
        );

    /// <summary>Maps the generated domains body to the CLI-facing shape.</summary>
    /// <param name="defaultIsoCode">The default culture.</param>
    /// <param name="domains">The bindings.</param>
    /// <returns>The mapped response.</returns>
    private static DomainsResponse MapDomains(
        string? defaultIsoCode,
        List<Gen.DomainPresentationModel>? domains
    ) =>
        new()
        {
            DefaultIsoCode = defaultIsoCode,
            Domains =
            [
                .. (domains ?? []).Select(d => new DomainBinding
                {
                    DomainName = d.DomainName ?? "",
                    IsoCode = d.IsoCode ?? "",
                }),
            ],
        };
}
