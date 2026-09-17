using System.Text.Json;
using Microsoft.Kiota.Serialization.Json;
using Umbraco.Cli.Client;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for <see cref="UmbracoManagementClient.CreateDocumentBody"/>. The Umbraco 17+
/// Management API marks <c>template</c> as required on a document-create body (it is nullable),
/// but Kiota omits null complex properties, so a create with no template was rejected with
/// HTTP 400 (issue #134). These tests pin the serialized shape so the property is always present
/// and never duplicated - a future client regen that changed Kiota's null handling would trip the
/// "exactly once" assertion.
/// </summary>
public class CreateDocumentBodyTests
{
    /// <summary>Serializes a Kiota model to its JSON body exactly as the request adapter would.</summary>
    /// <param name="model">The model to serialize.</param>
    /// <returns>The serialized JSON string.</returns>
    private static string Serialize(Gen.CreateDocumentRequestModel model)
    {
        using var writer = new JsonSerializationWriter();
        // WriteObjectValue(null, ...) wraps the model in a JSON object (StartObject ->
        // model.Serialize -> EndObject), exactly as the request adapter does; calling
        // model.Serialize directly would write properties with no enclosing object.
        writer.WriteObjectValue<Gen.CreateDocumentRequestModel>(null, model);
        using var stream = writer.GetSerializedContent();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="needle"/> in <paramref name="haystack"/>.
    /// </summary>
    /// <param name="haystack">The string to search.</param>
    /// <param name="needle">The substring to count.</param>
    /// <returns>The number of occurrences.</returns>
    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    /// <summary>
    /// The fix (happy path): a create body with no template must still serialize
    /// <c>"template": null</c> - present, and exactly once - so Umbraco 17+ accepts it.
    /// </summary>
    [Fact]
    public void Serialize_NoTemplate_WritesTemplateNullExactlyOnce()
    {
        // Arrange
        var body = new UmbracoManagementClient.CreateDocumentBody
        {
            DocumentType = new Gen.ReferenceByIdModel { Id = Guid.NewGuid() },
            Variants = [],
            Values = [],
        };

        // Act
        var json = Serialize(body);

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(
            doc.RootElement.TryGetProperty("template", out var template),
            "template must be present on the create body"
        );
        Assert.Equal(JsonValueKind.Null, template.ValueKind);
        Assert.Equal(1, CountOccurrences(json, "\"template\""));
    }

    /// <summary>
    /// When a template is set, the base serializer writes it and the override adds nothing:
    /// <c>template</c> appears once, as the referenced object rather than null.
    /// </summary>
    [Fact]
    public void Serialize_WithTemplate_WritesTemplateObjectExactlyOnce()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var body = new UmbracoManagementClient.CreateDocumentBody
        {
            DocumentType = new Gen.ReferenceByIdModel { Id = Guid.NewGuid() },
            Template = new Gen.ReferenceByIdModel { Id = templateId },
            Variants = [],
            Values = [],
        };

        // Act
        var json = Serialize(body);

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("template", out var template));
        Assert.Equal(JsonValueKind.Object, template.ValueKind);
        Assert.Equal(templateId, template.GetProperty("id").GetGuid());
        Assert.Equal(1, CountOccurrences(json, "\"template\""));
    }
}
