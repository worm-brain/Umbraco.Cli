using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Parse errors name what was expected in words, not .NET type names (#211): the real
/// System.CommandLine messages are produced here and rewritten, so a change in the library's
/// wording shows up as a failure rather than a leak.
/// </summary>
public class ParseErrorWordingTests
{
    private static string ErrorFor(Func<Command, Symbol> add, string args)
    {
        var cmd = new Command("thing");
        add(cmd);
        var root = new RootCommand { cmd };
        return ParseErrorReporter.Humanise(
            Assert.Single(root.Parse($"thing {args}").Errors).Message
        );
    }

    [Fact]
    public void Humanise_NullableGuidOption_SaysAGuidIdWasExpected()
    {
        var message = ErrorFor(
            c =>
            {
                var o = new Option<Guid?>("--parent");
                c.Add(o);
                return o;
            },
            "--parent Blog"
        );

        Assert.Equal("'Blog' is not valid for --parent: expected a GUID id.", message);
    }

    [Fact]
    public void Humanise_GuidArgument_NamesTheArgument()
    {
        var message = ErrorFor(
            c =>
            {
                var a = new Argument<Guid>("id");
                c.Add(a);
                return a;
            },
            "nope"
        );

        Assert.Equal("'nope' is not a valid argument for thing: expected a GUID id.", message);
    }

    [Fact]
    public void Humanise_IntOption_SaysAWholeNumber()
    {
        var message = ErrorFor(
            c =>
            {
                var o = new Option<int>("--take");
                c.Add(o);
                return o;
            },
            "--take abc"
        );

        Assert.Equal("'abc' is not valid for --take: expected a whole number.", message);
    }

    [Fact]
    public void Humanise_AnyOtherMessage_IsUnchanged()
    {
        Assert.Equal(
            "Required option '--name' is missing.",
            ParseErrorReporter.Humanise("Required option '--name' is missing.")
        );
    }
}
