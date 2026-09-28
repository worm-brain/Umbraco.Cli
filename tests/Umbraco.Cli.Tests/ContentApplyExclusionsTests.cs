using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <see cref="ContentApplyCommand.ResolveExclusionsAsync"/>: how <c>content apply --prune
/// --exclude-type</c> turns document type references into ids, resolving in the command rather
/// than the client (#225, #262).
/// </summary>
public class ContentApplyExclusionsTests
{
    /// <summary>An alias resolves to the document type's id.</summary>
    [Fact]
    public async Task ResolveExclusionsAsync_KnownAlias_ExcludesItsId()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        var id = Guid.NewGuid();
        fake.References[(EntityKind.DocumentType, "contactSubmission")] = id;

        // Act
        var result = await ContentApplyCommand.ResolveExclusionsAsync(
            fake,
            ["contactSubmission"],
            [],
            CancellationToken.None
        );

        // Assert
        Assert.Equal([id], result.Data!.DocumentTypeIds);
    }

    /// <summary>An alias that names no document type is a failure, not an empty exclusion.</summary>
    [Fact]
    public async Task ResolveExclusionsAsync_UnknownAlias_Fails()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();

        // Act
        var result = await ContentApplyCommand.ResolveExclusionsAsync(
            fake,
            ["nope"],
            [],
            CancellationToken.None
        );

        // Assert
        Assert.Equal(404, result.StatusCode);
    }
}
