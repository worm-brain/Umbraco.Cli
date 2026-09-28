using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <see cref="CommandExamples"/> (#276): examples are kept as a list on the command and rendered
/// into its description in the layout help has always used.
/// </summary>
public class CommandExamplesTests
{
    [Fact]
    public void WithExamples_Examples_AppendsTheIndentedBlockToTheDescription()
    {
        // Arrange
        var command = new Command("get", "Get a thing.");

        // Act
        command.WithExamples("umbraco thing get <id>", "umbraco thing get <id> -o json");

        // Assert
        Assert.Equal(
            "Get a thing.\n\nExamples:\n  umbraco thing get <id>\n  umbraco thing get <id> -o json",
            command.Description
        );
    }

    [Fact]
    public void WithExamples_Examples_StoresThemInOrder()
    {
        // Arrange
        var command = new Command("get", "Get a thing.");

        // Act
        command.WithExamples("umbraco thing get <id>", "umbraco thing get <id> -o json");

        // Assert
        Assert.Equal(
            ["umbraco thing get <id>", "umbraco thing get <id> -o json"],
            CommandExamples.Of(command)
        );
    }

    [Fact]
    public void WithExamples_Chained_ReturnsTheSameCommand()
    {
        var command = new Command("get", "Get a thing.");

        var returned = command.WithExamples("umbraco thing get <id>");

        Assert.Same(command, returned);
    }

    [Fact]
    public void WithExamples_NoExamples_Throws()
    {
        var command = new Command("get", "Get a thing.");

        Assert.Throws<ArgumentException>(() => command.WithExamples());
    }

    [Fact]
    public void WithExamples_MultiLineExample_Throws()
    {
        var command = new Command("get", "Get a thing.");

        Assert.Throws<ArgumentException>(() => command.WithExamples("umbraco a\numbraco b"));
    }

    [Fact]
    public void WithExamples_CalledTwice_Throws()
    {
        // Arrange
        var command = new Command("get", "Get a thing.").WithExamples("umbraco thing get <id>");

        // Act + Assert: a second block would be rendered below the first.
        Assert.Throws<InvalidOperationException>(() => command.WithExamples("umbraco thing list"));
    }

    [Fact]
    public void Of_CommandWithoutExamples_ReturnsEmpty()
    {
        var command = new Command("get", "Get a thing.");

        Assert.Empty(CommandExamples.Of(command));
    }

    [Fact]
    public void Describe_CommandWithExamples_ListsThemInTheCatalog()
    {
        // Arrange
        var command = new Command("get", "Get a thing.").WithExamples("umbraco thing get <id>");

        // Act
        var node = CommandCatalog.Describe(command);

        // Assert
        Assert.Equal(["umbraco thing get <id>"], node.Examples);
    }

    [Fact]
    public void Describe_CommandWithoutExamples_LeavesExamplesNull()
    {
        var node = CommandCatalog.Describe(new Command("get", "Get a thing."));

        Assert.Null(node.Examples);
    }
}
