using System.Text.Json.Nodes;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Tests;

/// <summary>
/// The key check every <c>--json-body</c> goes through (ADR 0011): a key is unknown only where
/// none of the given schemas defines it, at its own position.
/// </summary>
public class JsonBodyKeysTests
{
    // A request with a name and a list of items, each with an alias and a free-form value.
    private static readonly JsonNode Request = JsonNode.Parse(
        """
        { "type": "object", "additionalProperties": false, "properties": {
            "name": { "type": "string" },
            "items": { "type": "array", "items": { "type": "object", "additionalProperties": false,
              "properties": { "alias": { "type": "string" }, "value": true } } },
            "first": { "$ref": "#/properties/items/items" } } }
        """
    )!;

    // A read shape that adds an id at the top and a state on each item.
    private static readonly JsonNode Read = JsonNode.Parse(
        """
        { "type": "object", "additionalProperties": false, "properties": {
            "id": { "type": "string" },
            "items": { "type": "array", "items": { "type": "object", "additionalProperties": false,
              "properties": { "alias": { "type": "string" }, "state": { "type": "string" } } } } } }
        """
    )!;

    [Fact]
    public void FindUnknown_KeysEitherSchemaDefinesAtTheirPosition_ReturnsNothing()
    {
        var body = JsonNode.Parse(
            """{ "id": "x", "name": "n", "items": [{ "alias": "a", "value": 1, "state": "Draft" }] }"""
        )!;

        Assert.Empty(JsonBodyKeys.FindUnknown(body, Request, Read));
    }

    [Fact]
    public void FindUnknown_KeyDefinedOnlyAtAnotherPosition_IsUnknownWhereItIs()
    {
        // "id" is a top-level key of the read shape, not an item key of either.
        var body = JsonNode.Parse("""{ "items": [{ "alias": "a", "id": "x" }] }""")!;

        Assert.Equal(["$.items[0].id"], JsonBodyKeys.FindUnknown(body, Request, Read));
    }

    [Fact]
    public void FindUnknown_KeyUnderAReference_IsResolvedThroughThePointer()
    {
        var body = JsonNode.Parse("""{ "first": { "alias": "a", "nope": 1 } }""")!;

        Assert.Equal(["$.first.nope"], JsonBodyKeys.FindUnknown(body, Request));
    }

    [Fact]
    public void FindUnknown_KeysInsideAFreeFormValue_AreNotChecked()
    {
        var body = JsonNode.Parse(
            """{ "items": [{ "alias": "a", "value": { "anything": [1] } }] }"""
        )!;

        Assert.Empty(JsonBodyKeys.FindUnknown(body, Request));
    }

    [Fact]
    public void Error_SeveralKeys_ListsThemAndNamesBothWaysOut()
    {
        var error = JsonBodyKeys.Error(
            ["$.a", "$.b[0].c"],
            "the content update schema",
            "The CLI",
            "ignore them instead"
        );

        Assert.Equal(
            "--json-body has keys the content update schema does not define: $.a, $.b[0].c. "
                + "The CLI would ignore them. Run with --schema to see the body's shape, or pass "
                + "--allow-unknown-keys to ignore them instead.",
            error.Message
        );
    }
}
