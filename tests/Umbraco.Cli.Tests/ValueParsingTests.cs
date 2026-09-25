using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Id and date options get a parser that writes its own readable error (#211 review), rather than
/// relying on rewriting System.CommandLine's message text.
/// </summary>
public class ValueParsingTests
{
    private static readonly Option<Guid?> Parent = new("--parent");
    private static readonly Option<DateTimeOffset?> PublishAt = new("--publish-at");
    private static readonly Argument<Guid> Id = new("id");

    private static ParseResult Parse(string args)
    {
        var cmd = new Command("thing") { Parent, PublishAt, Id };
        var root = new RootCommand { cmd };
        ValueParsing.Apply(root);
        return root.Parse($"thing {args}");
    }

    [Fact]
    public void Apply_BadGuidOption_SaysAGuidIdWasExpected()
    {
        var error = Assert.Single(Parse($"{Guid.NewGuid()} --parent Blog").Errors);

        Assert.Equal("'Blog' is not valid for --parent: expected a GUID id.", error.Message);
    }

    [Fact]
    public void Apply_BadGuidArgument_NamesTheArgument()
    {
        var error = Assert.Single(Parse("nope").Errors);

        Assert.Equal("'nope' is not a valid id: expected a GUID id.", error.Message);
    }

    [Fact]
    public void Apply_BadDate_SaysAnIsoDateWasExpected()
    {
        var error = Assert.Single(Parse($"{Guid.NewGuid()} --publish-at tomorrow").Errors);

        Assert.StartsWith(
            "'tomorrow' is not valid for --publish-at: expected an ISO 8601",
            error.Message
        );
    }

    [Fact]
    public void Apply_ValidValues_StillParse()
    {
        var (id, parent) = (Guid.NewGuid(), Guid.NewGuid());

        var result = Parse($"{id} --parent {parent} --publish-at 2026-10-01T09:00:00Z");

        Assert.Equal(
            (
                id,
                (Guid?)parent,
                (DateTimeOffset?)new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)
            ),
            (result.GetValue(Id), result.GetValue(Parent), result.GetValue(PublishAt))
        );
    }

    [Fact]
    public void Apply_AnAbsentOptionalOption_StaysNull()
    {
        Assert.Null(Parse($"{Guid.NewGuid()}").GetValue(Parent));
    }

    [Fact]
    public void Apply_BadValueForAnOptionalNullableArgument_IsAParseErrorNotACrash()
    {
        // Found by the Phase 3 blast-radius probe: without a parser, System.CommandLine accepted
        // 'nope' for an optional Guid? argument (content update, document-blueprint update,
        // user-data update) with no parse error, and reading the value then threw.
        var key = new Argument<Guid?>("key") { Arity = ArgumentArity.ZeroOrOne };
        var root = new RootCommand { new Command("update") { key } };
        ValueParsing.Apply(root);

        var error = Assert.Single(root.Parse("update nope").Errors);

        Assert.Equal("'nope' is not a valid key: expected a GUID id.", error.Message);
    }

    [Fact]
    public void Apply_AnOptionWithItsOwnParser_IsLeftAlone()
    {
        var own = new Option<Guid?>("--own") { CustomParser = _ => Guid.Empty };
        var root = new RootCommand { own };

        ValueParsing.Apply(root);

        Assert.Equal(Guid.Empty, root.Parse("--own anything").GetValue(own));
    }
}
