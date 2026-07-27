using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for the <c>--json-body</c> schema generation (#61): the JSON Schema is derived from
/// the request record and honours its <c>[JsonPropertyName]</c> wire names.
/// </summary>
public class JsonBodySchemaTests
{
    [Fact]
    public void For_CreateContentRequest_DescribesWireShape()
    {
        var schema = JsonBodySchema.For<CreateContentRequest>();

        var properties = schema!["properties"]!.AsObject();
        // Property names come from [JsonPropertyName], not the C# names.
        Assert.True(properties.ContainsKey("contentType"));
        Assert.True(properties.ContainsKey("values"));
        Assert.True(properties.ContainsKey("variants"));

        // contentType is an object with an alias field.
        var contentType = properties["contentType"]!["properties"]!.AsObject();
        Assert.True(contentType.ContainsKey("alias"));
    }

    [Fact]
    public void For_UpdateContentRequest_DescribesValuesAndVariants()
    {
        var schema = JsonBodySchema.For<UpdateContentRequest>();

        var properties = schema!["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("values"));
        Assert.True(properties.ContainsKey("variants"));
    }
}
