using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A get-by-id read that gets a 200 with no body reports a 404 failure rather than a hollow item
/// carrying only the id it was asked for (#119). One case per read, since each maps its own body.
/// </summary>
public class GetByIdNotFoundWireTests
{
    private static readonly Guid Id = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");

    /// <summary>
    /// Each get-by-id read, reduced to the two fields the assertion needs, so one theory covers
    /// reads with different payload types.
    /// </summary>
    private static readonly Dictionary<
        string,
        Func<UmbracoManagementClient, Task<(bool IsSuccess, int StatusCode)>>
    > Reads = new()
    {
        ["content"] = async c => Shape(await c.GetContentByIdAsync(Id)),
        ["media"] = async c => Shape(await c.GetMediaByIdAsync(Id)),
        ["document-type"] = async c => Shape(await c.GetDocumentTypeByIdAsync(Id)),
        ["data-type"] = async c => Shape(await c.GetDataTypeByIdAsync(Id)),
        ["member"] = async c => Shape(await c.GetMemberByIdAsync(Id)),
        ["user"] = async c => Shape(await c.GetUserByIdAsync(Id)),
        ["member-group"] = async c => Shape(await c.GetMemberGroupByIdAsync(Id)),
        ["data-type-folder"] = async c => Shape(await c.GetDataTypeFolderAsync(Id)),
        ["blueprint-folder"] = async c => Shape(await c.GetBlueprintFolderAsync(Id)),
        ["indexer"] = async c => Shape(await c.GetIndexerAsync("ExternalIndex")),
        ["relation-type"] = async c => Shape(await c.GetRelationTypeByIdAsync(Id)),
        ["user-group"] = async c => Shape(await c.GetUserGroupByIdAsync(Id)),
        ["dictionary"] = async c => Shape(await c.GetDictionaryItemByIdAsync(Id)),
    };

    /// <summary>The theory's cases: every key of <see cref="Reads"/>.</summary>
    public static TheoryData<string> ReadNames => [.. Reads.Keys];

    /// <summary>A response reduced to success and status.</summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="response">The response.</param>
    /// <returns>Whether it succeeded, and its status.</returns>
    private static (bool, int) Shape<T>(UmbracoResponse<T> response) =>
        (response.IsSuccess, response.StatusCode);

    /// <summary>A 200 with an empty body is a 404 failure, for every get-by-id read.</summary>
    /// <param name="read">Which read to run.</param>
    [Theory]
    [MemberData(nameof(ReadNames))]
    public async Task GetById_EmptyBody_Returns404Failure(string read)
    {
        // Arrange
        var client = Wire.Client(Wire.Blank());

        // Act
        var result = await Reads[read](client);

        // Assert
        Assert.Equal((false, 404), result);
    }
}
