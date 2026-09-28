using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>The "did you mean" helper (#278), moved out of the API client.</summary>
public class SuggestionsTests
{
    private static readonly string[] Events =
    [
        "Umbraco.ContentPublish",
        "Umbraco.ContentUnpublish",
        "Umbraco.MediaSave",
    ];

    [Theory]
    [InlineData("ContentPublished", "Umbraco.ContentPublish")]
    [InlineData("Umbraco.ContentUnpublished", "Umbraco.ContentUnpublish")]
    [InlineData("mediasave", "Umbraco.MediaSave")]
    [InlineData("umbraco.contentpublish", "Umbraco.ContentPublish")]
    public void Nearest_CloseName_SuggestsTheCandidate(string typed, string expected)
    {
        Assert.Equal(expected, Suggestions.Nearest(typed, Events, optionalPrefix: "Umbraco."));
    }

    [Fact]
    public void Nearest_UnrelatedName_SuggestsNothing()
    {
        Assert.Null(Suggestions.Nearest("MemberGroupDeleted", Events, optionalPrefix: "Umbraco."));
    }

    [Fact]
    public void Nearest_BareNameWithoutThePrefixOption_SuggestsNothing()
    {
        // The prefix is only stripped when the caller says it is optional.
        Assert.Null(Suggestions.Nearest("MediaSave", Events));
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("same", "same", 0)]
    public void EditDistance_TwoStrings_CountsSingleCharacterEdits(string a, string b, int expected)
    {
        Assert.Equal(expected, Suggestions.EditDistance(a, b));
    }
}
