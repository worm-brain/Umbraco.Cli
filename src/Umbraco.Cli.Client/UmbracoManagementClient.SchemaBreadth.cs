using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// The raw reads and writes behind the schema snapshot's newer kinds (#227): languages, the
/// dictionary, and member and user groups. Items with a GUID use the kind-keyed raw path in
/// <c>UmbracoManagementClient.SchemaMerge.cs</c>; this file adds what that path cannot do -
/// enumerate them, and read and write languages, which are keyed by ISO code rather than an id.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>Base path of the Management API, relative to the host root.</summary>
    private const string ApiRoot = "umbraco/management/api/v1";

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<JsonNode>>> GetLanguagesRawAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<JsonNode>>(
            ct,
            async () => await ReadAllRawItemsAsync($"{ApiRoot}/language", ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> CreateLanguageRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await SendRawJsonAsync(Method.POST, $"{ApiRoot}/language", body, ct);
                // The cached language list now misses the new one.
                _languages = null;
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateLanguageRawAsync(
        string isoCode,
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                if (body.DeepClone() is not JsonObject payload)
                    throw new InvalidArgumentException("The language body was not a JSON object.");
                // The update model has no isoCode: the route names the language.
                payload.Remove("isoCode");
                await SendRawJsonAsync(
                    Method.PUT,
                    $"{ApiRoot}/language/{Uri.EscapeDataString(isoCode)}",
                    payload,
                    ct
                );
                _languages = null;
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<DictionaryEntry>>> GetDictionaryEntriesAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<DictionaryEntry>>(
            ct,
            async () =>
                (await ReadAllRawItemsAsync($"{ApiRoot}/dictionary", ct))
                    .Select(item => (Id: GuidAt(item, "id"), Parent: GuidAt(item?["parent"], "id")))
                    .Where(e => e.Id is not null)
                    .Select(e => new DictionaryEntry(e.Id!.Value, e.Parent))
                    .ToList()
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetMemberGroupIdsAsync(
        CancellationToken ct = default
    ) => IdsAsync($"{ApiRoot}/member-group", ct);

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetUserGroupIdsAsync(
        CancellationToken ct = default
    ) => IdsAsync($"{ApiRoot}/user-group", ct);

    /// <summary>The <c>id</c> of every item in a paged collection.</summary>
    /// <param name="path">The collection path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ids, or a mapped failure.</returns>
    private Task<UmbracoResponse<IReadOnlyList<Guid>>> IdsAsync(
        string path,
        CancellationToken ct
    ) =>
        GuardedApiAsync<IReadOnlyList<Guid>>(
            ct,
            async () =>
                (await ReadAllRawItemsAsync(path, ct))
                    .Select(item => GuidAt(item, "id"))
                    .OfType<Guid>()
                    .ToList()
        );

    /// <summary>
    /// Reads every page of a <c>{total, items}</c> collection and returns the items verbatim.
    /// Stops on a short page or once <c>total</c> is covered, so a partial read cannot pass for the
    /// whole collection.
    /// </summary>
    /// <param name="path">The collection path, without a query string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every item.</returns>
    private async Task<List<JsonNode>> ReadAllRawItemsAsync(string path, CancellationToken ct)
    {
        const int take = 100;
        var all = new List<JsonNode>();
        while (true)
        {
            var page = await GetRawJsonAsync($"{path}?skip={all.Count}&take={take}", ct);
            var items = (page["items"] as JsonArray ?? []).OfType<JsonNode>().ToList();
            all.AddRange(items);

            var total = page["total"] is JsonValue t && t.TryGetValue<long>(out var n) ? n : -1;
            if (items.Count < take || (total >= 0 && all.Count >= total))
                break;
        }
        return all;
    }

    /// <summary>Parses the GUID at <paramref name="node"/>[<paramref name="field"/>], if any.</summary>
    /// <param name="node">The object to read from.</param>
    /// <param name="field">The field name.</param>
    /// <returns>The GUID, or null when the field is missing or not a GUID.</returns>
    private static Guid? GuidAt(JsonNode? node, string field) =>
        node?[field] is JsonValue v
        && v.TryGetValue<string>(out var s)
        && Guid.TryParse(s, out var g)
            ? g
            : null;
}
