using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands;

/// <summary>
/// One schema noun - document types, data types, media types, member types, templates - as the
/// raw-body commands need it (#250 Phase 5): its <see cref="EntityKind"/>, which picks the
/// endpoint on the client, how it is named in messages, and how to list its ids for
/// <c>--schema</c>. One descriptor per noun, so a command names its kind once rather than as
/// an enum here and a loose string there.
/// </summary>
/// <param name="Kind">The schema kind; selects the Management API endpoint.</param>
/// <param name="Plural">The plural used in messages, e.g. <c>document types</c>.</param>
/// <param name="ListIds">Lists the ids of every item of the kind, for <c>--schema</c>.</param>
public sealed record SchemaNoun(
    EntityKind Kind,
    string Plural,
    Func<
        IUmbracoManagementClient,
        CancellationToken,
        Task<UmbracoResponse<IReadOnlyList<Guid>>>
    > ListIds
)
{
    /// <summary>Document types (<c>document-type</c>).</summary>
    public static readonly SchemaNoun DocumentTypes = new(
        EntityKind.DocumentType,
        "document types",
        (client, ct) => client.GetDocumentTypeIdsAsync(ct)
    );

    /// <summary>Data types.</summary>
    public static readonly SchemaNoun DataTypes = new(
        EntityKind.DataType,
        "data types",
        (client, ct) => client.GetDataTypeIdsAsync(ct)
    );

    /// <summary>Media types.</summary>
    public static readonly SchemaNoun MediaTypes = new(
        EntityKind.MediaType,
        "media types",
        (client, ct) => client.GetMediaTypeIdsAsync(ct)
    );

    /// <summary>Member types.</summary>
    public static readonly SchemaNoun MemberTypes = new(
        EntityKind.MemberType,
        "member types",
        (client, ct) => client.GetMemberTypeIdsAsync(ct)
    );

    /// <summary>Templates.</summary>
    public static readonly SchemaNoun Templates = new(
        EntityKind.Template,
        "templates",
        (client, ct) => client.GetTemplateIdsAsync(ct)
    );

    /// <summary>The singular used in messages, e.g. <c>document type</c>.</summary>
    public string Singular => Kind.Noun();
}
