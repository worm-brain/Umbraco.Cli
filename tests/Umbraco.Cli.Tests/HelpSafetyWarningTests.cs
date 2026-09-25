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
/// into the destructive one (#178/#179); and the help must agree with what the command actually
/// does. The second one started as "must not advertise a capability the CLI lacks" - the false
/// claims on <c>document-type get</c> (#160) and <c>media get</c> (#172) sent a test agent down a
/// dead end in the 2026-09-23 round (#187). Phase 3 made both claims true, so these now assert the
/// capability is described rather than absent; the point is that the two never drift apart.
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
    public void DictionaryHelp_ExamplesUseFullCultureCodes()
    {
        // #210: "--values en=Home" always fails since #181 validates against the site's
        // isoCodes (en-US). Covers the noun's examples, create's examples and the option text.
        var dictionary = Umbraco.Cli.Commands.Dictionary.DictionaryCommand.Build(BuildExecutor());
        var create = Assert.Single(dictionary.Subcommands, c => c.Name == "create");
        var help = string.Join(
            "\n",
            dictionary.Description,
            create.Description,
            Assert.Single(create.Options, o => o.Name == "--values").Description
        );

        Assert.DoesNotMatch(@"--values [a-z]{2}=", help);
    }

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
    public void ContentTypesGetHelp_DescribesThePropertiesItNowReturns()
    {
        var contentTypes = ContentTypesCommand.Build(BuildExecutor());

        var help = DescriptionOf(contentTypes, "get");

        // This claim was false for three releases (#160) and the help was corrected to drop it.
        // Phase 3 made it true, so the assertion inverts: the capability exists and must be
        // described. What matters either way is that the two agree.
        Assert.Contains("properties", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("property groups", help, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MediaGetHelp_DescribesTheUrlAndMetadataItNowReturns()
    {
        var media = MediaCommand.Build(BuildExecutor());

        var help = DescriptionOf(media, "get");

        // Same inversion as above, for #172.
        // Case-insensitive: this guard exists to stop the help and the behaviour drifting apart,
        // so a lowercase rewording must not be what breaks it.
        Assert.Contains("url", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("umbracoWidth", help, StringComparison.OrdinalIgnoreCase);
    }
}
