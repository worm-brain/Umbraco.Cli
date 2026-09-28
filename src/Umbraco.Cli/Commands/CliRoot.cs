using System.CommandLine;
using System.CommandLine.Help;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.ContentTypes;
using Umbraco.Cli.Commands.Cultures;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.Diagnostics;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Commands.DocumentBlueprints;
using Umbraco.Cli.Commands.Examine;
using Umbraco.Cli.Commands.Imaging;
using Umbraco.Cli.Commands.Languages;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Commands.MediaTypes;
using Umbraco.Cli.Commands.MemberGroups;
using Umbraco.Cli.Commands.Members;
using Umbraco.Cli.Commands.MemberTypes;
using Umbraco.Cli.Commands.PropertyTypes;
using Umbraco.Cli.Commands.Redirects;
using Umbraco.Cli.Commands.Relations;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Commands.StaticFiles;
using Umbraco.Cli.Commands.Tags;
using Umbraco.Cli.Commands.Templates;
using Umbraco.Cli.Commands.UserData;
using Umbraco.Cli.Commands.UserGroups;
using Umbraco.Cli.Commands.Users;
using Umbraco.Cli.Commands.Webhooks;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Assembles the complete <c>umbraco</c> command tree. It is the one place the top-level nouns
/// are registered, so <c>Program.cs</c>, the parse tests and the committed surface snapshot
/// (<c>docs/surface.json</c>) all see exactly the same tree rather than hand-copied lists that
/// drift apart.
/// </summary>
public static class CliRoot
{
    /// <summary>
    /// Builds the fully-assembled root command: global options, every noun, the
    /// <c>commands</c> catalog, the richer <c>--version</c> action and the readable value-parse
    /// errors.
    /// </summary>
    /// <param name="globalOptions">The shared global options, added recursively to the root.</param>
    /// <param name="configStore">The persisted CLI configuration (used by <c>auth</c>).</param>
    /// <param name="authService">The OAuth service (used by <c>auth</c>).</param>
    /// <param name="executor">The executor every API-backed command runs through.</param>
    /// <param name="httpClientFactory">The HTTP client factory (used by <c>auth doctor</c>).</param>
    /// <param name="clientFactory">The management-client factory (used by <c>auth</c>).</param>
    /// <returns>The root command, ready to parse.</returns>
    public static RootCommand Build(
        GlobalOptions globalOptions,
        ConfigStore configStore,
        UmbracoAuthService authService,
        CommandExecutor executor,
        IHttpClientFactory httpClientFactory,
        IUmbracoManagementClientFactory clientFactory
    )
    {
        var root = new RootCommand(
            "Umbraco CLI — manage your Umbraco CMS from the terminal.\n\n"
                + "Quick start:\n"
                + "  umbraco auth login --host https://mysite.com\n"
                + "  umbraco content list --output json\n"
                + "  umbraco content list | jq '.data[].name'\n\n"
                + "All commands support --output json (default when stdout is piped).\n"
                + "Use UMBRACO_HOST, UMBRACO_CLIENT_ID, UMBRACO_CLIENT_SECRET for CI/CD."
        );

        // Global options are recursive — available on every command.
        globalOptions.AddTo(root);

        // ── Sub-commands ──────────────────────────────────────────────────────
        root.Add(
            AuthCommand.Build(
                globalOptions,
                configStore,
                authService,
                executor,
                httpClientFactory,
                clientFactory
            )
        );
        root.Add(ContentCommand.Build(executor));
        root.Add(MediaCommand.Build(executor));
        root.Add(MediaTypesCommand.Build(executor));
        root.Add(ContentTypesCommand.Build(executor));
        root.Add(DataTypesCommand.Build(executor));
        root.Add(LanguagesCommand.Build(executor));
        root.Add(TemplatesCommand.Build(executor));
        root.Add(MembersCommand.Build(executor));
        root.Add(MemberTypesCommand.Build(executor));
        root.Add(UsersCommand.Build(executor));
        root.Add(DictionaryCommand.Build(executor));
        root.Add(WebhooksCommand.Build(executor));
        root.Add(SchemaCommand.Build(executor));

        // Static-file resources: one factory, three nouns (#105).
        root.Add(StaticFileCommand.Build(executor, StaticFileKind.Script, "script", "script"));
        root.Add(
            StaticFileCommand.Build(executor, StaticFileKind.Stylesheet, "stylesheet", "stylesheet")
        );
        root.Add(
            StaticFileCommand.Build(
                executor,
                StaticFileKind.PartialView,
                "partial-view",
                "partial view"
            )
        );

        // Small coverage resources (#107).
        root.Add(MemberGroupsCommand.Build(executor));
        root.Add(TagsCommand.Build(executor));
        root.Add(CulturesCommand.Build(executor));

        // User-administration resources (#109).
        root.Add(UserGroupsCommand.Build(executor));
        root.Add(UserDataCommand.Build(executor));

        // Document blueprints / content templates (#113).
        root.Add(DocumentBlueprintCommand.Build(executor));

        // Read-only diagnostics: server, health, log-viewer, models-builder, manifest (#115).
        root.Add(ServerCommand.Build(executor));
        root.Add(HealthCommand.Build(executor));
        root.Add(LogViewerCommand.Build(executor));
        root.Add(ModelsBuilderCommand.Build(executor));
        root.Add(ManifestCommand.Build(executor));

        // Redirects and relations (#118).
        root.Add(RedirectCommand.Build(executor));
        root.Add(RelationTypeCommand.Build(executor));
        root.Add(RelationCommand.Build(executor));

        // Examine, imaging, property-type (#121).
        root.Add(IndexerCommand.Build(executor));
        root.Add(SearcherCommand.Build(executor));
        root.Add(ImagingCommand.Build(executor));
        root.Add(PropertyTypeCommand.Build(executor));

        // Shell tab completion (#92): a local command, like the catalog below.
        root.Add(CompletionCommand.Build());

        // Machine-readable command catalog for agents (#60). Added last and given the root so it
        // can describe the fully-assembled tree (including itself).
        root.Add(CommandsCommand.Build(globalOptions, root));

        // Richer --version (#95): replace System.CommandLine's default version action so the
        // output reports the tool version, target framework and runtime instead of just the
        // assembly version.
        //
        // Help keeps --help, -h and -? but drops the DOS-style /h and /? aliases (#379): the
        // tab completion scripts list every alias, so they cluttered every "<TAB>" with two
        // spellings no Unix shell user types.
        foreach (var option in root.Options)
        {
            if (option is VersionOption versionOption)
                versionOption.Action = new VersionCommandAction();
            if (option is HelpOption helpOption)
            {
                helpOption.Aliases.Remove("/h");
                helpOption.Aliases.Remove("/?");
            }
        }

        // Readable parse errors for every id and date option, installed once on the finished tree.
        ValueParsing.Apply(root);

        return root;
    }
}
