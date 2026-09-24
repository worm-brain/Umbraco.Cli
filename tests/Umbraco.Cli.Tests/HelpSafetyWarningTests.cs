using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.ContentTypes;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Help text is the only description of a command's contract an agent gets before running it, so
/// the parts that decide whether data survives are pinned here rather than left to review. Two
/// obligations: the write semantics of <c>content update</c> must be stated, including how to opt
/// into the destructive one (#178/#179); and no command may advertise a capability the CLI does
/// not have - the false claims on <c>content-types get</c> (#160) and <c>media get</c> (#172) are
/// what sent a test agent down a dead end in the 2026-09-23 round (#187).
/// </summary>
public class HelpSafetyWarningTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>
    /// Builds a <see cref="CommandExecutor"/> with throwaway config and a stub HTTP factory.
    /// Nothing here reaches the network: these tests only read the command tree's descriptions.
    /// </summary>
    /// <returns>An executor suitable for building command trees.</returns>
    private static CommandExecutor BuildExecutor()
    {
        var stub = new StubHttpClientFactory();
        var configStore = new ConfigStore(
            Path.Combine(Path.GetTempPath(), $"umbraco-help-test-{Guid.NewGuid()}.json")
        );
        var factory = new CommandContextFactory(
            configStore,
            new UmbracoAuthService(stub),
            stub,
            new GlobalOptions(),
            new UmbracoManagementClientFactory(),
            new MutationInterceptState()
        );
        return new CommandExecutor(
            factory,
            new Umbraco.Cli.Infrastructure.ConsoleConfirmationPrompt()
        );
    }

    /// <summary>Finds a subcommand's description by name, failing the test if it is absent.</summary>
    /// <param name="noun">The built noun command (e.g. <c>content</c>).</param>
    /// <param name="verb">The subcommand name to look up (e.g. <c>update</c>).</param>
    /// <returns>The subcommand's description text.</returns>
    private static string DescriptionOf(Command noun, string verb) =>
        Assert.Single(noun.Subcommands, c => c.Name == verb).Description ?? "";

    [Fact]
    public void ContentUpdateHelp_StatesThatValuesAreMergedAndNamesTheReplaceOptOut()
    {
        var content = ContentCommand.Build(BuildExecutor());

        var help = DescriptionOf(content, "update");

        Assert.Contains("MERGED", help);
        Assert.Contains("--replace", help);
        Assert.Contains("template is preserved", help);
    }

    [Fact]
    public void ContentTypesGetHelp_DoesNotClaimToReturnPropertyGroups()
    {
        var contentTypes = ContentTypesCommand.Build(BuildExecutor());

        var help = DescriptionOf(contentTypes, "get");

        Assert.DoesNotContain("including its property groups", help);
        Assert.Contains("schema export", help);
    }

    [Fact]
    public void MediaGetHelp_DoesNotClaimToReturnTheUrl()
    {
        var media = MediaCommand.Build(BuildExecutor());

        var help = DescriptionOf(media, "get");

        Assert.DoesNotContain("including URL and metadata", help);
    }
}
