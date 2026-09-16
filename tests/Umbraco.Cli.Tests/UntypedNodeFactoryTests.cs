using System.Text.Json;
using Microsoft.Kiota.Abstractions.Serialization;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for <see cref="UntypedNodeFactory"/>, the JSON (<see cref="JsonElement"/>) to
/// Kiota <see cref="UntypedNode"/> converter used to map property-editor values onto the
/// generated content/media/member request models (#79). Verifies the mapping behaviour at
/// the converter's public interface: given JSON in, the shape and typed values of the node
/// tree out. Number mapping is the one lossy case and is covered explicitly (ADR 0004).
/// </summary>
public class UntypedNodeFactoryTests
{
    /// <summary>
    /// Parses a JSON literal into the <see cref="JsonElement"/> the converter consumes.
    /// </summary>
    /// <param name="json">A JSON document literal.</param>
    /// <returns>The root element of the parsed document.</returns>
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>
    /// Tracer: a JSON string maps to an <see cref="UntypedString"/> carrying the same value.
    /// </summary>
    [Fact]
    public void FromJsonElement_String_MapsToUntypedString()
    {
        // Arrange
        var element = Parse("\"hello\"");

        // Act
        var node = UntypedNodeFactory.FromJsonElement(element);

        // Assert
        var str = Assert.IsType<UntypedString>(node);
        Assert.Equal("hello", str.GetValue());
    }

    /// <summary>
    /// An integer within <see cref="int"/> range maps to an <see cref="UntypedInteger"/>.
    /// </summary>
    [Fact]
    public void FromJsonElement_SmallInteger_MapsToUntypedInteger()
    {
        var node = UntypedNodeFactory.FromJsonElement(Parse("42"));

        var integer = Assert.IsType<UntypedInteger>(node);
        Assert.Equal(42, integer.GetValue());
    }

    /// <summary>
    /// An integer beyond <see cref="int"/> range maps to an <see cref="UntypedLong"/> rather
    /// than overflowing or degrading to a floating-point type.
    /// </summary>
    [Fact]
    public void FromJsonElement_LargeInteger_MapsToUntypedLong()
    {
        var node = UntypedNodeFactory.FromJsonElement(Parse("9999999999"));

        var value = Assert.IsType<UntypedLong>(node);
        Assert.Equal(9999999999L, value.GetValue());
    }

    /// <summary>
    /// A non-integer maps to an <see cref="UntypedDecimal"/>, preserving decimal precision
    /// rather than introducing floating-point rounding.
    /// </summary>
    [Fact]
    public void FromJsonElement_Decimal_MapsToUntypedDecimalPreservingPrecision()
    {
        var node = UntypedNodeFactory.FromJsonElement(Parse("19.99"));

        var value = Assert.IsType<UntypedDecimal>(node);
        Assert.Equal(19.99m, value.GetValue());
    }

    /// <summary>
    /// A JSON boolean maps to an <see cref="UntypedBoolean"/> carrying the same value.
    /// </summary>
    [Fact]
    public void FromJsonElement_Boolean_MapsToUntypedBoolean()
    {
        var node = UntypedNodeFactory.FromJsonElement(Parse("true"));

        var value = Assert.IsType<UntypedBoolean>(node);
        Assert.True(value.GetValue());
    }

    /// <summary>
    /// A JSON null maps to an <see cref="UntypedNull"/>.
    /// </summary>
    [Fact]
    public void FromJsonElement_Null_MapsToUntypedNull()
    {
        var node = UntypedNodeFactory.FromJsonElement(Parse("null"));

        Assert.IsType<UntypedNull>(node);
    }

    /// <summary>
    /// A JSON array maps to an <see cref="UntypedArray"/>, recursing into and preserving the
    /// order of its elements.
    /// </summary>
    [Fact]
    public void FromJsonElement_Array_MapsToUntypedArrayPreservingOrder()
    {
        var node = UntypedNodeFactory.FromJsonElement(Parse("[\"a\", 1, true]"));

        var array = Assert.IsType<UntypedArray>(node);
        var elements = array.GetValue().ToList();
        Assert.Equal(3, elements.Count);
        Assert.Equal("a", Assert.IsType<UntypedString>(elements[0]).GetValue());
        Assert.Equal(1, Assert.IsType<UntypedInteger>(elements[1]).GetValue());
        Assert.True(Assert.IsType<UntypedBoolean>(elements[2]).GetValue());
    }

    /// <summary>
    /// A nested JSON object maps to an <see cref="UntypedObject"/>, preserving keys and
    /// recursing into nested objects and arrays - the shape a block-list property value takes.
    /// </summary>
    [Fact]
    public void FromJsonElement_NestedObject_MapsToUntypedObjectPreservingKeys()
    {
        var node = UntypedNodeFactory.FromJsonElement(
            Parse("{\"name\":\"Home\",\"blocks\":[{\"udi\":\"abc\"}]}")
        );

        var obj = Assert.IsType<UntypedObject>(node);
        var members = obj.GetValue().ToDictionary(m => m.Key, m => m.Value);
        Assert.Equal("Home", Assert.IsType<UntypedString>(members["name"]).GetValue());
        var blocks = Assert.IsType<UntypedArray>(members["blocks"]).GetValue().ToList();
        var firstBlock = Assert.IsType<UntypedObject>(Assert.Single(blocks));
        var blockMembers = firstBlock.GetValue().ToDictionary(m => m.Key, m => m.Value);
        Assert.Equal("abc", Assert.IsType<UntypedString>(blockMembers["udi"]).GetValue());
    }

    /// <summary>
    /// <see cref="UntypedNodeFactory.FromValue"/> maps a <see langword="null"/> boxed value to
    /// an <see cref="UntypedNull"/> (rather than throwing or returning null).
    /// </summary>
    [Fact]
    public void FromValue_Null_MapsToUntypedNull()
    {
        var node = UntypedNodeFactory.FromValue(null);

        Assert.IsType<UntypedNull>(node);
    }

    /// <summary>
    /// <see cref="UntypedNodeFactory.FromValue"/> routes a boxed <see cref="JsonElement"/>
    /// (how property values arrive from a deserialized <c>--json-body</c>) through the JSON
    /// mapping.
    /// </summary>
    [Fact]
    public void FromValue_BoxedJsonElement_MapsThroughJsonPath()
    {
        object boxed = Parse("\"boxed\"");

        var node = UntypedNodeFactory.FromValue(boxed);

        Assert.Equal("boxed", Assert.IsType<UntypedString>(node).GetValue());
    }

    /// <summary>
    /// <see cref="UntypedNodeFactory.FromValue"/> normalises a raw CLR value (built in code,
    /// not deserialized) through the same JSON mapping.
    /// </summary>
    [Fact]
    public void FromValue_RawClrObject_MapsThroughJsonPath()
    {
        var node = UntypedNodeFactory.FromValue(new { temporaryFileId = "temp-1" });

        var obj = Assert.IsType<UntypedObject>(node);
        var members = obj.GetValue().ToDictionary(m => m.Key, m => m.Value);
        Assert.Equal("temp-1", Assert.IsType<UntypedString>(members["temporaryFileId"]).GetValue());
    }
}
