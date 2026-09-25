using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// The write half of the schema round-trip (#250 Phase 5, #201): <c>get</c> returns the verbatim
/// Management API body, and <c>update --json-body</c> writes a body of that same shape back. The
/// PUT endpoints replace the whole item, so a caller who sent only the keys they changed used to
/// lose the rest (#201: every property's validation, the list view, the allowed children). Here
/// the item is read first and the body's top-level keys are laid over it, so an omitted key keeps
/// its value - the same read-modify-write <c>content update</c> does (#178).
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> MergeSchemaItemAsync(
        EntityKind kind,
        Guid id,
        JsonNode body,
        bool replace = false,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                if (body is not JsonObject patch)
                    throw BadRequest("--json-body did not contain a JSON object.");

                var path = $"umbraco/management/api/v1/{SchemaSegment(kind)}/{id}";
                var merged = replace
                    ? patch
                    : Overlay(
                        await GetRawJsonAsync(path, ct) as JsonObject
                            ?? throw new ApiException(
                                $"The {kind.Noun()} body was not a JSON object."
                            ),
                        patch
                    );
                await SendRawJsonAsync(Method.PUT, path, merged, ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Lays <paramref name="patch"/>'s top-level keys over <paramref name="current"/>: a key in the
    /// patch replaces the current value whole (arrays included - a <c>properties</c> array is the
    /// complete list, not a delta), and a key the patch leaves out keeps its value. The item's own
    /// <c>id</c> always wins, so a body read from one item cannot be written over another's id.
    /// </summary>
    /// <param name="current">The item as read; updated in place.</param>
    /// <param name="patch">The body the caller supplied.</param>
    /// <returns><paramref name="current"/>, merged.</returns>
    internal static JsonObject Overlay(JsonObject current, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (key == "id")
                continue;
            current[key] = value?.DeepClone();
        }
        return current;
    }

    /// <summary>The Management API path segment for a schema kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The segment, e.g. <c>document-type</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not a schema item.</exception>
    private static string SchemaSegment(EntityKind kind) =>
        kind switch
        {
            EntityKind.DocumentType => "document-type",
            EntityKind.MediaType => "media-type",
            EntityKind.MemberType => "member-type",
            EntityKind.DataType => "data-type",
            EntityKind.Template => "template",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Only schema items (document, media and member types, data types, templates) can be merged."
            ),
        };
}
