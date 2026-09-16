using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.ContentTypes;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Commands.Languages;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Commands.MediaTypes;
using Umbraco.Cli.Commands.Members;
using Umbraco.Cli.Commands.MemberTypes;
using Umbraco.Cli.Commands.StaticFiles;
using Umbraco.Cli.Commands.Templates;
using Umbraco.Cli.Commands.Users;
using Umbraco.Cli.Commands.Webhooks;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

public class CommandParseTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static RootCommand BuildRoot()
    {
        var stub = new StubHttpClientFactory();
        var configStore = new ConfigStore(
            Path.Combine(Path.GetTempPath(), $"umbraco-parse-test-{Guid.NewGuid()}.json")
        );
        var authService = new UmbracoAuthService(stub);
        var globalOptions = new GlobalOptions();
        var factory = new CommandContextFactory(
            configStore,
            authService,
            stub,
            globalOptions,
            new UmbracoManagementClientFactory(),
            new Umbraco.Cli.Infrastructure.Http.MutationInterceptState()
        );
        var executor = new CommandExecutor(
            factory,
            new Umbraco.Cli.Infrastructure.ConsoleConfirmationPrompt()
        );

        var root = new RootCommand("Umbraco CLI");
        globalOptions.AddTo(root);

        root.Add(
            AuthCommand.Build(
                globalOptions,
                configStore,
                authService,
                executor,
                stub,
                new UmbracoManagementClientFactory()
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

        return root;
    }

    private static bool HasErrors(string args)
    {
        var parsed = BuildRoot().Parse(args);
        return parsed.Errors.Count > 0;
    }

    // ── Conditional requirement + --schema (#61) ─────────────────────────────

    [Theory]
    [InlineData("content create")] // missing --content-type/--name and no --json-body
    [InlineData("content update")] // missing id and --json-body
    public void WriteCommand_MissingRequiredInput_IsParseError(string args) =>
        Assert.True(HasErrors(args));

    [Theory]
    [InlineData("content create --content-type textPage --name About")]
    [InlineData("content create --json-body body.json")]
    [InlineData("content create --schema")] // --schema bypasses the requirement
    [InlineData("content update 3f7a8b2e-1234-5678-abcd-ef0123456789 --json-body u.json")]
    [InlineData("content update --schema")] // --schema bypasses the requirement
    public void WriteCommand_ValidOrSchema_IsNotParseError(string args) =>
        Assert.False(HasErrors(args));

    // ── Command tree structure ────────────────────────────────────────────────

    [Theory]
    [InlineData("auth")]
    [InlineData("content")]
    [InlineData("media")]
    [InlineData("media-types")]
    [InlineData("content-types")]
    [InlineData("data-types")]
    [InlineData("languages")]
    [InlineData("templates")]
    [InlineData("members")]
    [InlineData("member-types")]
    [InlineData("users")]
    [InlineData("dictionary")]
    [InlineData("webhooks")]
    [InlineData("script")]
    [InlineData("stylesheet")]
    [InlineData("partial-view")]
    public void RootCommand_ContainsExpectedSubcommand(string subcommand)
    {
        var root = BuildRoot();
        var names = root.Subcommands.Select(c => c.Name).ToList();
        Assert.Contains(subcommand, names);
    }

    [Theory]
    [InlineData(
        "content",
        new[]
        {
            "list",
            "get",
            "create",
            "update",
            "delete",
            "publish",
            "unpublish",
            "versions",
            "rollback",
            "trash",
            "restore",
            "empty-recycle-bin",
            "move",
            "copy",
            "publish-descendants",
            "bulk",
            "export",
            "diff",
            "apply",
        }
    )]
    [InlineData(
        "media",
        new[] { "list", "get", "upload", "delete", "trash", "restore", "empty-recycle-bin", "move" }
    )]
    [InlineData("media-types", new[] { "list", "get", "create", "delete" })]
    [InlineData("member-types", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("script", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("stylesheet", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("partial-view", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("auth", new[] { "login", "logout", "whoami", "doctor" })]
    public void SubcommandGroup_ContainsExpectedVerbs(string group, string[] verbs)
    {
        var root = BuildRoot();
        var groupCmd = root.Subcommands.First(c => c.Name == group);
        var subNames = groupCmd.Subcommands.Select(c => c.Name).ToList();

        foreach (var verb in verbs)
            Assert.Contains(verb, subNames);
    }

    // ── Valid parses → no errors ──────────────────────────────────────────────

    [Theory]
    [InlineData("content list")]
    [InlineData("content list --skip 0 --take 50")]
    [InlineData("content list --take 100")]
    [InlineData("content list --parent 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content publish 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content unpublish 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content versions 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content versions 3f7a8b2e-1234-5678-abcd-ef0123456789 --culture en-US")]
    [InlineData("content rollback 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content trash 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content restore 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "content restore 3f7a8b2e-1234-5678-abcd-ef0123456789 --parent 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("content empty-recycle-bin")]
    [InlineData(
        "content move 3f7a8b2e-1234-5678-abcd-ef0123456789 --parent 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("content move 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content copy 3f7a8b2e-1234-5678-abcd-ef0123456789 --include-descendants")]
    [InlineData(
        "content publish-descendants 3f7a8b2e-1234-5678-abcd-ef0123456789 --cultures en-US"
    )]
    [InlineData("content bulk delete --file ids.txt")]
    [InlineData("content bulk delete")] // reads stdin at run time
    [InlineData("content bulk publish --cultures en-US")]
    [InlineData("content bulk unpublish --file ids.txt")]
    [InlineData("content export")]
    [InlineData("content export --out content.json")]
    [InlineData("content export --root 3f7a8b2e-1234-5678-abcd-ef0123456789 --out sub.json")]
    [InlineData("content diff content.json")]
    [InlineData("content diff -")]
    [InlineData("content apply content.json")]
    [InlineData("content apply content.json --dry-run")]
    [InlineData("content apply content.json --prune --yes")]
    [InlineData("media trash 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media restore 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media empty-recycle-bin")]
    [InlineData(
        "media move 3f7a8b2e-1234-5678-abcd-ef0123456789 --parent 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("media list")]
    [InlineData("media get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media upload ./logo.png")] // --parent optional (#57)
    [InlineData("media upload ./big.mp4 --media-type File --name Promo")]
    [InlineData("media upload ./p.jpg --parent 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media-types list")]
    [InlineData("media-types get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media-types create --name \"Custom Image\" --alias customImage")]
    [InlineData("media-types delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content-types list")]
    [InlineData("data-types list")]
    [InlineData(
        "data-types create --name X --editor-alias Umbraco.TextBox --editor-ui-alias Umb.Ui"
    )]
    [InlineData(
        "data-types update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name X --editor-alias a --editor-ui-alias b"
    )]
    [InlineData("data-types delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("languages list")]
    [InlineData("languages update fr-FR --name \"French (France)\"")]
    [InlineData("templates list")]
    [InlineData("templates create --name Home --alias home")]
    [InlineData("templates update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name Home --alias home")]
    [InlineData("templates delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("dictionary delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("members update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name \"Jane Roe\"")]
    [InlineData("members update 3f7a8b2e-1234-5678-abcd-ef0123456789 --approved")]
    [InlineData(
        "members update 3f7a8b2e-1234-5678-abcd-ef0123456789 --email a@b.com --approved false"
    )]
    [InlineData("templates update 3f7a8b2e-1234-5678-abcd-ef0123456789")] // all fields optional (read-merge)
    [InlineData("data-types update 3f7a8b2e-1234-5678-abcd-ef0123456789")] // all fields optional (read-merge)
    [InlineData("languages update fr-FR")] // all fields optional (read-merge)
    [InlineData(
        "webhooks create --url https://x.com/h --events A --id 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )] // #86 --id
    [InlineData(
        "webhooks create --url https://x.com/h --events A --name Hook --description \"on publish\""
    )] // #80 --name/--description
    [InlineData("dictionary create --key K --id 3f7a8b2e-1234-5678-abcd-ef0123456789")] // #86 --id
    [InlineData("media-types create --name X --alias x --id 3f7a8b2e-1234-5678-abcd-ef0123456789")] // #86 --id
    [InlineData(
        "content-types create --name X --alias x --id 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )] // #86 --id
    [InlineData("members list")]
    [InlineData("member-types list")]
    [InlineData("member-types get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("script list")]
    [InlineData("script list --parent folder --take 50")]
    [InlineData("script get folder/site.js")]
    [InlineData("script create --name site.js --content \"// hi\"")]
    [InlineData("script create --name site.js --parent lib --content-file ./x.js")]
    [InlineData("script update folder/site.js --content \"// x\"")]
    [InlineData("script delete folder/site.js")]
    [InlineData("stylesheet get theme/site.css")]
    [InlineData("stylesheet create --name site.css")]
    [InlineData("partial-view get grid/row.cshtml")]
    [InlineData("partial-view delete grid/row.cshtml")]
    [InlineData("member-types create --name Author --alias author")]
    [InlineData("member-types update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name Author")]
    [InlineData("member-types update 3f7a8b2e-1234-5678-abcd-ef0123456789 --icon icon-user")]
    [InlineData("member-types delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("users list")]
    [InlineData("dictionary list")]
    [InlineData("webhooks list")]
    [InlineData("auth login --host https://example.com --client-id foo --client-secret bar")]
    [InlineData("auth logout")]
    [InlineData("auth whoami")]
    [InlineData("auth doctor")]
    [InlineData("auth doctor --output json")]
    public void ValidArgs_ProduceNoParseErrors(string args)
    {
        Assert.False(HasErrors(args), $"Unexpected parse errors for: {args}");
    }

    // ── Global options on valid commands ──────────────────────────────────────

    [Theory]
    [InlineData("--output json content list")]
    [InlineData("--host https://x.com content list")]
    [InlineData("-H https://x.com content list")]
    [InlineData("-o json content list")]
    [InlineData("--verbose content list")]
    [InlineData("-v content list")]
    [InlineData("--token bearer123 content list")]
    [InlineData("content list --skip 5 --take 10")]
    public void GlobalOptions_ValidCombinations_NoParseErrors(string args)
    {
        Assert.False(HasErrors(args), $"Unexpected parse errors for: {args}");
    }

    // ── Invalid parses → produce errors ──────────────────────────────────────

    [Theory]
    [InlineData("content list --take abc")]
    [InlineData("content list --skip -1.5")]
    [InlineData("content get not-a-uuid")]
    [InlineData("content delete not-a-uuid")]
    [InlineData("media-types create --name OnlyName")] // missing required --alias
    [InlineData("media-types get not-a-uuid")]
    [InlineData("member-types create --alias onlyAlias")] // missing required --name
    [InlineData("member-types get not-a-uuid")]
    [InlineData("member-types update not-a-uuid --name Author")] // id must be a uuid
    [InlineData("totally-unknown-command")]
    [InlineData("content unknown-verb")]
    [InlineData("auth unknown-verb")]
    public void InvalidArgs_ProduceParseErrors(string args)
    {
        Assert.True(HasErrors(args), $"Expected parse errors for: {args}");
    }
}
