using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <see cref="CommandPath"/> is the one source of <c>meta.command</c> and the allow-list's
/// command names, so it must name exactly the command that was parsed.
/// </summary>
public class CommandPathTests
{
    [Fact]
    public void Of_NestedCommand_IsTheDottedPathWithoutTheRoot()
    {
        var root = new RootCommand();
        var content = new Command("content");
        var domain = new Command("domain");
        domain.Add(new Command("set"));
        content.Add(domain);
        root.Add(content);

        var name = CommandPath.Of(root.Parse("content domain set"));

        Assert.Equal("content.domain.set", name);
    }

    [Fact]
    public void Of_OnlyTheRootParsed_IsNull()
    {
        var root = new RootCommand();
        root.Add(new Command("content"));

        var name = CommandPath.Of(root.Parse(""));

        Assert.Null(name);
    }
}
