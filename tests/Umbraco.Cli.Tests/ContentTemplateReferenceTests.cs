using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <see cref="ContentTemplateReference.Parse"/>: the one rule that turns a <c>--template</c> value
/// into a reference, shared by <c>content create</c> and <c>content update</c> (#190).
/// </summary>
public class ContentTemplateReferenceTests
{
    /// <summary>A GUID is read as an id, so no alias lookup is needed.</summary>
    [Fact]
    public void Parse_Guid_ReturnsIdReference()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var reference = ContentTemplateReference.Parse(id.ToString());

        // Assert
        Assert.Equal(new ContentTemplateReference { Id = id }, reference);
    }

    /// <summary>Anything that is not a GUID is read as an alias, as typed.</summary>
    [Fact]
    public void Parse_Alias_ReturnsAliasReference()
    {
        // Act
        var reference = ContentTemplateReference.Parse("blogPost");

        // Assert
        Assert.Equal(new ContentTemplateReference { Alias = "blogPost" }, reference);
    }
}
