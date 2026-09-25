using System.CommandLine;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Multi-value options take commas as well as spaces and repeats (#231, #232), and name the values
/// they could not read.
/// </summary>
public class ListOptionTests
{
    private static ParseResult Parse<T>(Option<T[]> option, string args)
    {
        var root = new RootCommand { option };
        return root.Parse(args);
    }

    [Theory]
    [InlineData("--cultures en-US,da-DK")]
    [InlineData("--cultures en-US da-DK")]
    [InlineData("--cultures en-US --cultures da-DK")]
    [InlineData("--cultures \" en-US , da-DK ,\"")] // spaces and a trailing comma are tidied away
    public void Strings_CommasSpacesAndRepeats_AllGiveTheSameList(string args)
    {
        var option = ListOption.Strings("--cultures", "Cultures.");

        Assert.Equal(["en-US", "da-DK"], Parse(option, args).GetValue(option)!);
    }

    [Fact]
    public void Guids_CommaSeparated_AreParsed()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var option = ListOption.Guids("--children", "Children.");

        Assert.Equal([a, b], Parse(option, $"--children {a},{b}").GetValue(option)!);
    }

    [Fact]
    public void Guids_AnInvalidItem_IsAParseErrorNamingIt()
    {
        var option = ListOption.Guids("--children", "Children.");

        var error = Assert.Single(Parse(option, $"--children {Guid.NewGuid()},nope").Errors);

        Assert.Equal("--children expects GUID ids. Not understood: nope.", error.Message);
    }

    [Fact]
    public void Enums_AreMatchedIgnoringCase()
    {
        var option = ListOption.Enums<SortKey>("--key", "Keys.");

        Assert.Equal(
            [SortKey.Name, SortKey.PublishDate],
            Parse(option, "--key name,PUBLISHDATE").GetValue(option)!
        );
    }
}
