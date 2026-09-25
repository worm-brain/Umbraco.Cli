using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
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
using Umbraco.Cli.Commands.StaticFiles;
using Umbraco.Cli.Commands.Tags;
using Umbraco.Cli.Commands.Templates;
using Umbraco.Cli.Commands.UserData;
using Umbraco.Cli.Commands.UserGroups;
using Umbraco.Cli.Commands.Users;
using Umbraco.Cli.Commands.Webhooks;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

public class CommandParseTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // The shipped tree, so the parse errors are the ones a user sees.
    private static RootCommand BuildRoot() => TestCliRoot.Build();

    [Theory]
    [InlineData("content apply content.json --exclude-type contactSubmission")]
    [InlineData("content apply content.json --exclude-root 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("schema apply schema.json --force")]
    public void PruneOnlyOptionWithoutPrune_IsAParseError(string args)
    {
        // Review finding: these narrow or override a prune, and without --prune they were
        // silently ignored, which reads as protection that is not there.
        Assert.True(HasErrors(args));
    }

    /// <summary>Every leaf command in the tree, with its dotted path (e.g. <c>content.delete</c>).</summary>
    private static IEnumerable<(string Path, Command Command)> Leaves(Command command, string path)
    {
        if (command.Subcommands.Count == 0)
            yield return (path, command);
        foreach (var sub in command.Subcommands)
        foreach (var leaf in Leaves(sub, path.Length == 0 ? sub.Name : $"{path}.{sub.Name}"))
            yield return leaf;
    }

    [Fact]
    public void RealTree_AlwaysDestructiveCommands_AreExactlyTheIrreversibleAndHighImpactOnes()
    {
        // #255: the commands that need --yes, pinned. Adding or removing a gate is a
        // deliberate change to this list. Copy/move (#247) and redirect tracking enable (#249)
        // are absent on purpose.
        string[] expected =
        [
            "content.bulk.delete",
            "content.bulk.unpublish",
            "content.delete",
            "content.empty-recycle-bin",
            "content.unpublish",
            "content-types.delete",
            "data-types.delete",
            "data-types.folder.delete",
            "dictionary.delete",
            "document-blueprint.delete",
            "document-blueprint.folder.delete",
            "indexer.rebuild",
            "languages.delete",
            "log-viewer.saved-search.delete",
            "media.delete",
            "media.empty-recycle-bin",
            "media-types.delete",
            "member-groups.delete",
            "member-types.delete",
            "members.delete",
            "models-builder.build",
            "partial-view.delete",
            "redirect.delete",
            "redirect.tracking.disable",
            "script.delete",
            "stylesheet.delete",
            "templates.delete",
            "user-data.delete",
            "user-groups.delete",
            "user-groups.delete-many",
            "webhooks.delete",
        ];

        var actual = Leaves(BuildRoot(), "")
            .Where(l => CommandSafety.IsAlwaysDestructive(l.Command))
            .Select(l => l.Path)
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void RealTree_ConditionallyDestructiveCommands_AreTheApplyPrunes()
    {
        var actual = Leaves(BuildRoot(), "")
            .Where(l => CommandSafety.DestructiveWhen(l.Command) is not null)
            .Select(l => $"{l.Path} {CommandSafety.DestructiveWhen(l.Command)}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(["content.apply --prune", "schema.apply --prune"], actual);
    }

    /// <summary>
    /// Parses <paramref name="args"/> against a fresh root using the same parser configuration as
    /// production (response files disabled, #115), so the tests exercise real parsing behaviour.
    /// </summary>
    /// <param name="args">The raw command-line string.</param>
    /// <returns>The parse result.</returns>
    private static ParseResult Parse(string args) =>
        BuildRoot().Parse(args, Umbraco.Cli.Infrastructure.CliParserConfiguration.Create());

    private static bool HasErrors(string args) => Parse(args).Errors.Count > 0;

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
    [InlineData("content update 3f7a8b2e-1234-5678-abcd-ef0123456789 --json-body u.json --replace")]
    [InlineData(
        "content update 3f7a8b2e-1234-5678-abcd-ef0123456789 --json-body u.json --template blogPost"
    )]
    [InlineData("content create --content-type textPage --name About --template blogPost")]
    // #159: these take a human key now, not only a UUID.
    [InlineData("content-types get blogPost")]
    [InlineData("data-types get Textstring")]
    [InlineData("languages create --culture da-DK --fallback en-US")]
    // #161/#169: the schema verbs take a full body, and --schema needs no other argument.
    [InlineData("content-types create --json-body t.json")]
    [InlineData("content-types create --schema")]
    [InlineData("content-types update blogPost --json-body t.json")]
    [InlineData("content-types update --schema")]
    [InlineData("data-types create --json-body d.json")]
    [InlineData("data-types update Textstring --json-body d.json")]
    [InlineData("data-types update --schema")]
    public void WriteCommand_ValidOrSchema_IsNotParseError(string args) =>
        Assert.False(HasErrors(args));

    [Theory]
    [InlineData("content sort")] // --children is required (#88)
    [InlineData("content sort --parent 1a2b3c4d-1234-5678-abcd-ef0123456789")]
    [InlineData("media sort")]
    public void Sort_MissingChildren_IsParseError(string args) => Assert.True(HasErrors(args));

    [Theory]
    [InlineData("content find")] // neither --name nor --path (#89)
    [InlineData("content find --name About --path Home/About")] // both is ambiguous
    [InlineData("media find")]
    [InlineData("media find --name logo --path Images/Logos")]
    public void Find_NotExactlyOneMode_IsParseError(string args) => Assert.True(HasErrors(args));

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
    [InlineData("member-groups")]
    [InlineData("tags")]
    [InlineData("cultures")]
    [InlineData("user-groups")]
    [InlineData("user-data")]
    [InlineData("document-blueprint")]
    [InlineData("server")]
    [InlineData("health")]
    [InlineData("log-viewer")]
    [InlineData("models-builder")]
    [InlineData("manifest")]
    [InlineData("redirect")]
    [InlineData("relation-type")]
    [InlineData("relation")]
    [InlineData("indexer")]
    [InlineData("searcher")]
    [InlineData("imaging")]
    [InlineData("property-type")]
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
            "tree",
            "find",
            "get",
            "create",
            "update",
            "delete",
            "publish",
            "unpublish",
            "versions",
            "version",
            "rollback",
            "trash",
            "restore",
            "empty-recycle-bin",
            "move",
            "sort",
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
        new[]
        {
            "list",
            "tree",
            "find",
            "get",
            "upload",
            "delete",
            "trash",
            "restore",
            "empty-recycle-bin",
            "move",
            "sort",
        }
    )]
    [InlineData("media-types", new[] { "list", "get", "create", "delete" })]
    [InlineData("member-types", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("script", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("stylesheet", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("partial-view", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("member-groups", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData("tags", new[] { "list" })]
    [InlineData("cultures", new[] { "list" })]
    [InlineData(
        "user-groups",
        new[]
        {
            "list",
            "get",
            "create",
            "update",
            "delete",
            "delete-many",
            "add-users",
            "remove-users",
        }
    )]
    [InlineData("user-data", new[] { "list", "get", "create", "update", "delete" })]
    [InlineData(
        "document-blueprint",
        new[]
        {
            "list",
            "get",
            "scaffold",
            "create",
            "update",
            "delete",
            "from-document",
            "move",
            "folder",
        }
    )]
    [InlineData("server", new[] { "status", "info", "configuration", "troubleshooting" })]
    [InlineData("health", new[] { "list", "get", "run" })]
    [InlineData(
        "log-viewer",
        new[] { "log", "levels", "level-count", "message-templates", "saved-search" }
    )]
    [InlineData("models-builder", new[] { "dashboard", "status", "build" })]
    [InlineData("manifest", new[] { "list" })]
    [InlineData("redirect", new[] { "list", "status", "delete", "tracking" })]
    [InlineData("relation-type", new[] { "list", "get" })]
    [InlineData("relation", new[] { "list" })]
    [InlineData("indexer", new[] { "list", "get", "rebuild" })]
    [InlineData("searcher", new[] { "list", "query" })]
    [InlineData("imaging", new[] { "resize-urls" })]
    [InlineData("property-type", new[] { "is-used" })]
    [InlineData(
        "data-types",
        new[]
        {
            "list",
            "get",
            "create",
            "update",
            "delete",
            "is-used",
            "referenced-by",
            "copy",
            "move",
            "folder",
        }
    )]
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
    [InlineData("content tree")]
    [InlineData("content tree --parent 3f7a8b2e-1234-5678-abcd-ef0123456789 --recursive")]
    [InlineData("content tree --depth 3")]
    [InlineData("content find --name About")]
    [InlineData("content find --name Team --parent 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content find --path Home/About")]
    [InlineData("content get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content publish 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "content publish 3f7a8b2e-1234-5678-abcd-ef0123456789 --publish-at 2026-01-01T09:00:00Z"
    )]
    [InlineData(
        "content publish 3f7a8b2e-1234-5678-abcd-ef0123456789 --publish-at 2026-01-01T09:00:00Z --unpublish-at 2026-02-01T18:30:00Z"
    )]
    [InlineData("content unpublish 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content versions 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content versions 3f7a8b2e-1234-5678-abcd-ef0123456789 --culture en-US")]
    [InlineData("content version 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content rollback 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content trash 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content restore 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "content restore 3f7a8b2e-1234-5678-abcd-ef0123456789 --parent 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("content restore 3f7a8b2e-1234-5678-abcd-ef0123456789 --to-root")]
    [InlineData("content empty-recycle-bin")]
    [InlineData(
        "content move 3f7a8b2e-1234-5678-abcd-ef0123456789 --parent 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("content move 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content copy 3f7a8b2e-1234-5678-abcd-ef0123456789 --include-descendants")]
    [InlineData(
        "content sort --parent 1a2b3c4d-1234-5678-abcd-ef0123456789 --children 3f7a8b2e-1234-5678-abcd-ef0123456789 9c4d5e6f-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("content sort --children 3f7a8b2e-1234-5678-abcd-ef0123456789")] // --parent optional (reorder the root)
    [InlineData(
        "content publish-descendants 3f7a8b2e-1234-5678-abcd-ef0123456789 --cultures en-US"
    )]
    [InlineData("content publish-descendants 3f7a8b2e-1234-5678-abcd-ef0123456789 --wait")]
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
    [InlineData(
        "content apply content.json --prune --exclude-type contactSubmission --exclude-root 3f7a8b2e-1234-5678-abcd-ef0123456789 --yes"
    )]
    [InlineData("schema apply schema.json --prune --force --yes")]
    [InlineData("media trash 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media restore 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media empty-recycle-bin")]
    [InlineData(
        "media move 3f7a8b2e-1234-5678-abcd-ef0123456789 --parent 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData(
        "media sort --parent 1a2b3c4d-1234-5678-abcd-ef0123456789 --children 3f7a8b2e-1234-5678-abcd-ef0123456789 9c4d5e6f-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("media list")]
    [InlineData("media tree")]
    [InlineData("media tree --parent 3f7a8b2e-1234-5678-abcd-ef0123456789 --depth 2")]
    [InlineData("media find --name logo")]
    [InlineData("media find --path Images/Logos")]
    [InlineData("media get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media upload ./logo.png")] // --parent optional (#57)
    [InlineData("media upload ./big.mp4 --media-type File --name Promo")]
    [InlineData("media upload ./p.jpg --parent 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "media upload ./b.pdf --media-type brochure --id 3f7a8b2e-1234-5678-abcd-ef0123456789 --value title=Brochure --value pages=12"
    )] // #226, #220
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
    [InlineData("templates delete blogPost")] // #206: <id|alias>
    [InlineData("templates update blogPost --name \"Blog post\"")]
    [InlineData("media-types get brochure")] // #221
    [InlineData("media-types delete brochure --force")]
    [InlineData("member-types get siteMember")] // #213
    [InlineData("member-types update siteMember --name Author")]
    [InlineData("member-types delete siteMember")]
    [InlineData("content-types delete blogPost --force")]
    [InlineData("data-types delete \"Homepage Blocks\"")]
    [InlineData("data-types is-used Tags")] // data types by name everywhere
    [InlineData("data-types copy Tags --target 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("property-type is-used --content-type blogPost --alias bodyText")]
    [InlineData("user-groups get blogEditors")] // #217
    [InlineData("user-groups delete-many --ids blogEditors newsEditors")]
    [InlineData(
        "user-groups create --alias blogEditors --name \"Blog editors\" --document-start-node 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("dictionary delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("dictionary tree")]
    [InlineData("dictionary tree --parent 1a2b3c4d-1234-5678-abcd-ef0123456789")]
    [InlineData("dictionary create --key Nav.Home")]
    [InlineData("dictionary create --key Nav.Home --parent 1a2b3c4d-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "dictionary move 3f7a8b2e-1234-5678-abcd-ef0123456789 --target 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("dictionary move 3f7a8b2e-1234-5678-abcd-ef0123456789")] // --target optional (to root)
    [InlineData(
        "content sort --children 3f7a8b2e-1234-5678-abcd-ef0123456789,1a2b3c4d-1234-5678-abcd-ef0123456789"
    )] // #232
    [InlineData(
        "content sort --parent 3f7a8b2e-1234-5678-abcd-ef0123456789 --by publishDate --desc"
    )]
    [InlineData("media sort --by name")]
    [InlineData("content publish 3f7a8b2e-1234-5678-abcd-ef0123456789 --cultures en-US,da-DK")] // #231
    [InlineData(
        "content move 3f7a8b2e-1234-5678-abcd-ef0123456789 --target 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )] // #242: --target and --parent both work
    [InlineData(
        "media move 3f7a8b2e-1234-5678-abcd-ef0123456789 --target 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("dictionary move Blog.Tags --parent Blog")]
    [InlineData(
        "data-types move 3f7a8b2e-1234-5678-abcd-ef0123456789 --parent 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )]
    [InlineData(
        "document-blueprint move 3f7a8b2e-1234-5678-abcd-ef0123456789 --parent 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )]
    [InlineData(
        "user-data update 3f7a8b2e-1234-5678-abcd-ef0123456789 --group g --identifier i --value v"
    )] // #242: positional key
    [InlineData(
        "document-blueprint update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name X --replace"
    )]
    [InlineData("dictionary move Blog.Tags --target Blog")] // #211: keys everywhere
    [InlineData("dictionary update Blog.MinRead --values da-DK=Min")]
    [InlineData("dictionary delete Blog.MinRead")]
    [InlineData("dictionary tree --parent Blog")]
    [InlineData("dictionary create --key Blog.MinRead --parent Blog")]
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
    [InlineData("member-groups list")]
    [InlineData("member-groups get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("member-groups create --name Editors")]
    [InlineData("member-groups update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name Editors")]
    [InlineData("member-groups delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("tags list")]
    [InlineData("tags list --group default --culture en-US")]
    [InlineData("cultures list")]
    [InlineData("user-groups list")]
    [InlineData("user-groups list --skip 0 --take 50")]
    [InlineData("user-groups get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("user-groups create --alias editors --name Editors")]
    [InlineData(
        "user-groups create --alias editors --name Editors --section Umb.Section.Content --section Umb.Section.Media --language en-US --fallback-permission Umb.Document.Read --has-access-to-all-languages --document-root-access"
    )]
    [InlineData("user-groups create --alias e --name E --id 3f7a8b2e-1234-5678-abcd-ef0123456789")] // #86 --id
    [InlineData(
        "user-groups update 3f7a8b2e-1234-5678-abcd-ef0123456789 --alias editors --name Editors"
    )]
    [InlineData("user-groups delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "user-groups delete-many --ids 3f7a8b2e-1234-5678-abcd-ef0123456789 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData(
        "user-groups add-users 3f7a8b2e-1234-5678-abcd-ef0123456789 --user 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData(
        "user-groups remove-users 3f7a8b2e-1234-5678-abcd-ef0123456789 --user 1a2b3c4d-1234-5678-abcd-ef0123456789 --user 2b3c4d5e-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("user-data list")]
    [InlineData("user-data list --group myGroup --identifier theme --skip 0 --take 10")]
    [InlineData("user-data get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("user-data create --group myGroup --identifier theme --value dark")]
    [InlineData(
        "user-data create --group g --identifier i --value v --key 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )] // #86 --key
    [InlineData(
        "user-data update --key 3f7a8b2e-1234-5678-abcd-ef0123456789 --group g --identifier i --value light"
    )]
    [InlineData("user-data delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("document-blueprint list")]
    [InlineData("document-blueprint list --parent 3f7a8b2e-1234-5678-abcd-ef0123456789 --take 50")]
    [InlineData("document-blueprint get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("document-blueprint scaffold 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("document-blueprint create --document-type textPage --name Starter")]
    [InlineData(
        "document-blueprint create --document-type textPage --name Starter --parent 1a2b3c4d-1234-5678-abcd-ef0123456789 --id 2b3c4d5e-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("document-blueprint create --json-body bp.json")]
    [InlineData("document-blueprint create --schema")] // --schema bypasses the requirement
    [InlineData("document-blueprint update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name Renamed")]
    [InlineData(
        "document-blueprint update 3f7a8b2e-1234-5678-abcd-ef0123456789 --json-body bp.json"
    )]
    [InlineData("document-blueprint update --schema")]
    [InlineData("document-blueprint delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "document-blueprint from-document 3f7a8b2e-1234-5678-abcd-ef0123456789 --name Starter"
    )]
    [InlineData("document-blueprint move 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "document-blueprint move 3f7a8b2e-1234-5678-abcd-ef0123456789 --target 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("document-blueprint folder get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("document-blueprint folder create --name Marketing")]
    [InlineData(
        "document-blueprint folder update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name Marketing"
    )]
    [InlineData("document-blueprint folder delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("server status")]
    [InlineData("server info")]
    [InlineData("server troubleshooting")]
    [InlineData("health list")]
    [InlineData("health get \"Data Integrity\"")]
    [InlineData("health run Services")]
    [InlineData("log-viewer log")]
    [InlineData("log-viewer log --level Error --level Warning --take 50 --ascending")]
    [InlineData("log-viewer log --start-date 2026-01-01 --end-date 2026-02-01 --filter foo")]
    [InlineData("log-viewer levels")]
    [InlineData("log-viewer level-count")]
    [InlineData("log-viewer message-templates")]
    [InlineData("log-viewer saved-search list")]
    [InlineData("log-viewer saved-search create --name Errors --query x")]
    [InlineData("log-viewer saved-search delete Errors")]
    [InlineData("models-builder dashboard")]
    [InlineData("models-builder status")]
    [InlineData("models-builder build")]
    [InlineData("manifest list")]
    [InlineData("manifest list --scope Public")]
    [InlineData("redirect list")]
    [InlineData("redirect list --filter old --skip 0 --take 20")]
    [InlineData("redirect list --content 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("redirect status")]
    [InlineData("redirect delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("redirect tracking enable")]
    [InlineData("redirect tracking disable")]
    [InlineData("relation-type list")]
    [InlineData("relation-type get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("relation list --type 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("indexer list")]
    [InlineData("indexer get ExternalIndex")]
    [InlineData("indexer rebuild ExternalIndex")]
    [InlineData("searcher list")]
    [InlineData("searcher query ExternalSearcher --term news")]
    [InlineData("imaging resize-urls --id 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "imaging resize-urls --id 3f7a8b2e-1234-5678-abcd-ef0123456789 --width 300 --height 200 --mode Crop --format webp"
    )]
    [InlineData(
        "property-type is-used --content-type 3f7a8b2e-1234-5678-abcd-ef0123456789 --alias bodyText"
    )]
    [InlineData("data-types is-used 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("data-types referenced-by 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData(
        "data-types copy 3f7a8b2e-1234-5678-abcd-ef0123456789 --target 1a2b3c4d-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("data-types move 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("data-types folder create --name Pickers")]
    [InlineData("data-types folder get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("data-types folder update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name Pickers")]
    [InlineData("data-types folder delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
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
    [InlineData("--output csv content list")]
    [InlineData("-o csv content list")]
    [InlineData("--quiet content list")]
    [InlineData("-q content delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
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
    [InlineData("member-types create --alias onlyAlias")] // missing required --name
    [InlineData("user-data update --group g --identifier i --value v")] // #242: no key at all
    [InlineData("content sort --children 3f7a8b2e-1234-5678-abcd-ef0123456789 --by name")] // #232: an explicit order and a field conflict
    [InlineData("content sort --children 3f7a8b2e-1234-5678-abcd-ef0123456789 --desc")] // --desc needs --by
    [InlineData("media sort --by publishDate")] // media has no publish date
    [InlineData("content sort --children 3f7a8b2e-1234-5678-abcd-ef0123456789,nope")]
    [InlineData("user-groups create --name NoAlias")] // missing required --alias
    [InlineData("user-groups create --alias noName")] // missing required --name
    [InlineData(
        "user-groups update editors --alias editors --name Editors --document-root-access --document-start-node 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )] // #217: a start node and root access conflict
    [InlineData("user-groups delete-many")] // missing required --ids
    [InlineData("user-groups add-users 3f7a8b2e-1234-5678-abcd-ef0123456789")] // missing required --user
    [InlineData("user-data create --group g --identifier i")] // missing required --value
    [InlineData("user-data update --group g --identifier i --value v")] // missing required --key
    [InlineData("user-data get not-a-uuid")]
    [InlineData("document-blueprint create")] // needs --document-type + --name, or --json-body/--schema
    [InlineData("document-blueprint create --document-type textPage")] // missing --name
    [InlineData("document-blueprint update 3f7a8b2e-1234-5678-abcd-ef0123456789")] // needs --name or --json-body
    [InlineData("document-blueprint get not-a-uuid")]
    [InlineData("document-blueprint from-document 3f7a8b2e-1234-5678-abcd-ef0123456789")] // missing --name
    [InlineData("document-blueprint folder create")] // missing --name
    [InlineData("health get")] // missing group name argument
    [InlineData("health run")] // missing group name argument
    [InlineData("log-viewer saved-search create --name Errors")] // missing required --query
    [InlineData("log-viewer log --take abc")] // non-integer take
    [InlineData("log-viewer log --level Nonsense")] // invalid log level rejected at parse time
    [InlineData("manifest list --scope Nonsense")] // invalid enum value
    [InlineData("redirect delete not-a-uuid")]
    [InlineData("relation-type get not-a-uuid")]
    [InlineData("relation list")] // --type is required
    [InlineData("relation list --type not-a-uuid")]
    [InlineData("indexer get")] // missing name argument
    [InlineData("searcher query ExternalSearcher")] // missing required --term
    [InlineData("imaging resize-urls")] // missing required --id
    [InlineData("imaging resize-urls --id 3f7a8b2e-1234-5678-abcd-ef0123456789 --mode Nonsense")] // invalid enum
    [InlineData("property-type is-used --alias bodyText")] // missing required --content-type
    [InlineData("data-types folder get not-a-uuid")] // folders have no name lookup
    [InlineData("data-types folder create")] // missing required --name
    [InlineData("templates update --name X")] // id now optional at parse level; the validator requires it
    [InlineData("member-types update --name X")]
    [InlineData("media-types update")] // no id and no --json-body
    [InlineData("media-types update brochure")] // no --json-body (it has no flags)
    [InlineData("content-types update blogPost --replace")] // --replace needs a body
    [InlineData("media-types create --name OnlyName")] // --alias missing and no body
    [InlineData("totally-unknown-command")]
    [InlineData("content unknown-verb")]
    [InlineData("auth unknown-verb")]
    public void InvalidArgs_ProduceParseErrors(string args)
    {
        Assert.True(HasErrors(args), $"Expected parse errors for: {args}");
    }

    [Fact]
    public void MediaUpload_ValueForUmbracoFile_IsRejected()
    {
        // The file is umbracoFile; a --value for it would replace the upload (#220).
        Assert.Contains(
            Parse("media upload ./b.pdf --value umbracoFile=x").Errors,
            e => e.Message.Contains("umbracoFile")
        );
    }

    // ── key=value options refuse what they used to silently drop ─────────────────
    // Every repeatable pair option parsed with .Where(p => p.Length == 2), so a token with no
    // "=" in it vanished and the command reported success having written nothing. These pin the
    // refusal, and the message that names the offending token.

    [Theory]
    [InlineData("dictionary create --key Nav.Home --values en-US", "en-US")]
    [InlineData("dictionary update 3f7a8b2e-1234-5678-abcd-ef0123456789 --values Home", "Home")]
    [InlineData("members update 3f7a8b2e-1234-5678-abcd-ef0123456789 --value company", "company")]
    [InlineData("media upload ./b.pdf --value title", "title")]
    [InlineData(
        "content domains set 3f7a8b2e-1234-5678-abcd-ef0123456789 --domain example.com",
        "example.com"
    )]
    [InlineData("dictionary create --key Nav.Home --values =Home", "=Home")]
    public void PairOption_WithoutAnEquals_IsRejectedAndNamesTheToken(string args, string token)
    {
        var error = Assert.Single(Parse(args).Errors, e => e.Message.Contains("Not understood"));

        Assert.Contains(token, error.Message);
        // The reporter appends "Run '<cmd> --help' for usage." directly after this message, so it
        // has to end a sentence or the two run together.
        Assert.EndsWith(".", error.Message);
    }

    [Theory]
    [InlineData("dictionary create --key Nav.Home --values en-US=Home")]
    [InlineData("dictionary update 3f7a8b2e-1234-5678-abcd-ef0123456789 --values da-DK=Hjem")]
    [InlineData("members update 3f7a8b2e-1234-5678-abcd-ef0123456789 --value company=Acme")]
    // An empty value is legitimate - it is how a property is cleared.
    [InlineData("members update 3f7a8b2e-1234-5678-abcd-ef0123456789 --value company=")]
    [InlineData(
        "content domains set 3f7a8b2e-1234-5678-abcd-ef0123456789 --domain example.com/da=da-DK"
    )]
    public void PairOption_WellFormed_Parses(string args)
    {
        Assert.False(HasErrors(args), $"Unexpected parse errors for: {args}");
    }

    // ── Response-file tokens disabled (#115) ─────────────────────────────────────
    // An option value starting with '@' (common in Serilog log-viewer filters, e.g.
    // "@Level='Error'") must be taken literally, not as an "@file" response-file directive that
    // would fail to load and abort the parse.

    [Theory]
    [InlineData("log-viewer log --filter @Level='Error'", "--filter", "@Level='Error'")]
    [InlineData(
        "log-viewer saved-search create --name Errors --query @Exception",
        "--query",
        "@Exception"
    )]
    public void AtPrefixedOptionValue_IsNotTreatedAsResponseFile_AndRoundTrips(
        string args,
        string option,
        string expected
    )
    {
        var parsed = Parse(args);

        Assert.Empty(parsed.Errors);
        Assert.Equal(expected, parsed.GetValue<string>(option));
    }
}
