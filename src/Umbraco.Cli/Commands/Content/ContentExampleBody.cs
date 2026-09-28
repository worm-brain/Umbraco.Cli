using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Builds <c>content create --example --document-type &lt;alias&gt;</c>'s output (#174): a create
/// body for that document type with one <c>values[]</c> entry per property, each filled with an
/// example of its editor's value shape from <see cref="PropertyValueExamples"/>.
/// <para>
/// A static schema cannot say what <c>value</c> looks like, because that depends on the data type
/// behind each property; this reads the document type (and its compositions) and each property's
/// data type off the instance, so it also covers the site's own properties. An editor with no known
/// shape gets <c>value: null</c>; every entry carries the <c>editorAlias</c> it was chosen by, which
/// <c>content create</c> ignores, so the body can be edited and fed straight back.
/// </para>
/// </summary>
public static class ContentExampleBody
{
    /// <summary>Stands in for an unset culture on a type that varies, when the instance has no default.</summary>
    private const string FallbackCulture = "en-US";

    /// <summary>Builds the example body.</summary>
    /// <param name="client">The client.</param>
    /// <param name="documentType">The document type's alias or id.</param>
    /// <param name="culture">
    /// The culture for a type that varies by culture; null uses the instance's default language.
    /// </param>
    /// <param name="name">The item name to put in the variant; null uses a placeholder.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The body, or the failure of the read that could not be done.</returns>
    public static async Task<UmbracoResponse<JsonNode>> BuildAsync(
        IUmbracoManagementClient client,
        string documentType,
        string? culture,
        string? name,
        CancellationToken ct
    )
    {
        var type = await client.GetDocumentTypeAsync(documentType, ct);
        if (!type.IsSuccess || type.Data is null)
            return UmbracoResponse<JsonNode>.FailureFrom(type);

        // The type's own properties plus those it composes, since a document of the type carries
        // them all.
        var properties = await PropertiesAsync(client, type.Data, ct);
        if (!properties.IsSuccess)
            return UmbracoResponse<JsonNode>.FailureFrom(properties);

        var editors = await EditorAliasesAsync(client, properties.Data!, ct);
        if (!editors.IsSuccess)
            return UmbracoResponse<JsonNode>.FailureFrom(editors);

        // A type that varies needs a culture on its variant and on each varying value; an
        // invariant one takes null throughout (the variants pattern in docs/commands.md).
        var variantCulture = type.Data.VariesByCulture
            ? culture ?? await DefaultCultureAsync(client, ct)
            : null;

        return UmbracoResponse<JsonNode>.Success(
            Build(type.Data, properties.Data!, editors.Data!, variantCulture, name)
        );
    }

    /// <summary>Assembles the body from what was read.</summary>
    /// <param name="type">The document type.</param>
    /// <param name="properties">Every property a document of the type has.</param>
    /// <param name="editors">The editor alias of each data type id; a missing id is unknown.</param>
    /// <param name="culture">The culture for varying values, or null for an invariant type.</param>
    /// <param name="name">The item name, or null for a placeholder.</param>
    /// <returns>The body.</returns>
    internal static JsonObject Build(
        DocumentTypeResponse type,
        IReadOnlyList<DocumentTypePropertyResponse> properties,
        IReadOnlyDictionary<Guid, string?> editors,
        string? culture,
        string? name
    )
    {
        var values = new JsonArray();
        foreach (var property in properties)
        {
            var editor = property.DataType is { } id ? editors.GetValueOrDefault(id) : null;
            values.Add(
                new JsonObject
                {
                    ["alias"] = property.Alias,
                    // A property that does not itself vary takes null even on a varying document.
                    ["culture"] = property.VariesByCulture ? culture : null,
                    ["segment"] = null,
                    ["value"] = PropertyValueExamples.For(editor),
                    ["editorAlias"] = editor,
                }
            );
        }

        return new JsonObject
        {
            ["contentType"] = new JsonObject { ["alias"] = type.Alias },
            ["parent"] = null,
            ["template"] = null,
            ["variants"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = name ?? $"Example {type.Name}",
                    ["culture"] = culture,
                    ["segment"] = null,
                }
            ),
            ["values"] = values,
        };
    }

    /// <summary>
    /// The type's properties followed by those of its compositions (and theirs), each type read
    /// once, in sort order within a type.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="type">The document type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The properties, or the failure of a composition read.</returns>
    private static async Task<
        UmbracoResponse<IReadOnlyList<DocumentTypePropertyResponse>>
    > PropertiesAsync(
        IUmbracoManagementClient client,
        DocumentTypeResponse type,
        CancellationToken ct
    )
    {
        var result = new List<DocumentTypePropertyResponse>();
        var seen = new HashSet<Guid> { type.Id };
        var pending = new Queue<DocumentTypeResponse>([type]);
        while (pending.TryDequeue(out var current))
        {
            result.AddRange((current.Properties ?? []).OrderBy(p => p.SortOrder));
            // The seen set also stops a composition cycle, which Umbraco should never allow.
            foreach (var compositionId in (current.Compositions ?? []).Where(seen.Add))
            {
                var composition = await client.GetDocumentTypeByIdAsync(compositionId, ct);
                if (!composition.IsSuccess || composition.Data is null)
                    return UmbracoResponse<IReadOnlyList<DocumentTypePropertyResponse>>.FailureFrom(
                        composition
                    );
                pending.Enqueue(composition.Data);
            }
        }
        return UmbracoResponse<IReadOnlyList<DocumentTypePropertyResponse>>.Success(result);
    }

    /// <summary>Reads the editor alias of each distinct data type the properties use.</summary>
    /// <param name="client">The client.</param>
    /// <param name="properties">The properties.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Editor alias by data type id, or the failure of a data type read.</returns>
    private static async Task<
        UmbracoResponse<IReadOnlyDictionary<Guid, string?>>
    > EditorAliasesAsync(
        IUmbracoManagementClient client,
        IReadOnlyList<DocumentTypePropertyResponse> properties,
        CancellationToken ct
    )
    {
        var editors = new Dictionary<Guid, string?>();
        foreach (var id in properties.Select(p => p.DataType).OfType<Guid>().Distinct())
        {
            var dataType = await client.GetDataTypeByIdAsync(id, ct);
            if (!dataType.IsSuccess)
                return UmbracoResponse<IReadOnlyDictionary<Guid, string?>>.FailureFrom(dataType);
            editors[id] = dataType.Data?.EditorAlias;
        }
        return UmbracoResponse<IReadOnlyDictionary<Guid, string?>>.Success(editors);
    }

    /// <summary>The instance's default language, or a placeholder when it has none or cannot be read.</summary>
    /// <param name="client">The client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An ISO code.</returns>
    private static async Task<string> DefaultCultureAsync(
        IUmbracoManagementClient client,
        CancellationToken ct
    )
    {
        // Best effort: the example is still useful with a placeholder culture, so a failed read
        // does not fail the command.
        var languages = await client.GetLanguagesAsync(ct);
        return (languages.Data ?? []).FirstOrDefault(l => l.IsDefault)?.IsoCode ?? FallbackCulture;
    }
}
