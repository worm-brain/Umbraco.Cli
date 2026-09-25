using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
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
using Umbraco.Cli.Infrastructure.Http;
using Umbraco.Cli.Infrastructure.Output;

// Render non-ASCII output (e.g. the "Søg" example in dictionary help) correctly on the
// Windows console, whose default code page would otherwise show it as "S?g" (issue #49).
// Guarded: setting the encoding can throw when output is redirected to a non-console.
try
{
    if (!Console.IsOutputRedirected)
        Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException)
{
    // No attached console (or it rejected the change); safe to ignore.
}

// Honour NO_COLOR (#94): strip colour from all Spectre.Console output when the variable is present.
ConsoleColorSetup.ApplyFromEnvironment();

// ── DI ────────────────────────────────────────────────────────────────────────
var services = new ServiceCollection();
services.AddHttpClient();

// Mutation interceptor: powers --dry-run (and, later, #69/#70). Registered as the innermost
// handler on both Management-API clients so it sees the fully-built request; gated by the
// per-invocation MutationInterceptState so it is a no-op unless --dry-run is set. Not added
// to the auth (default) client, so the OAuth token exchange is never intercepted.
services.AddSingleton<MutationInterceptState>();
services.AddTransient<MutationInterceptorHandler>();

// 401 recovery (#248): outermost, so a retried request goes through the rest of the pipeline
// (and --verbose logs both attempts). Not on the auth (default) client: the token exchange is
// what it calls.
services.AddSingleton<TokenRefreshState>();
services.AddTransient<TokenRefreshHandler>();
services
    .AddHttpClient("umbraco")
    .AddHttpMessageHandler<TokenRefreshHandler>()
    .AddHttpMessageHandler<MutationInterceptorHandler>();

// A second named client that logs request/response to stderr; selected by --verbose.
services.AddTransient<VerboseHttpHandler>();
services
    .AddHttpClient("umbraco-verbose")
    .AddHttpMessageHandler<TokenRefreshHandler>()
    .AddHttpMessageHandler<VerboseHttpHandler>()
    .AddHttpMessageHandler<MutationInterceptorHandler>();
services.AddSingleton<ConfigStore>();

// Tokens outlive the process in a per-user file unless UMBRACO_NO_TOKEN_CACHE is set (#248).
services.AddSingleton(sp => new UmbracoAuthService(
    sp.GetRequiredService<IHttpClientFactory>(),
    cache: FileTokenCache.FromEnvironment()
));
services.AddSingleton<GlobalOptions>();
services.AddSingleton<IUmbracoManagementClientFactory, UmbracoManagementClientFactory>();
services.AddSingleton(sp => new CommandContextFactory(
    sp.GetRequiredService<ConfigStore>(),
    sp.GetRequiredService<UmbracoAuthService>(),
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<GlobalOptions>(),
    sp.GetRequiredService<IUmbracoManagementClientFactory>(),
    sp.GetRequiredService<MutationInterceptState>(),
    sp.GetRequiredService<TokenRefreshState>()
));
services.AddSingleton<IConfirmationPrompt, ConsoleConfirmationPrompt>();
services.AddSingleton(sp => new CommandExecutor(
    sp.GetRequiredService<CommandContextFactory>(),
    sp.GetRequiredService<IConfirmationPrompt>()
));

var sp = services.BuildServiceProvider();
var configStore = sp.GetRequiredService<ConfigStore>();
var authService = sp.GetRequiredService<UmbracoAuthService>();
var globalOptions = sp.GetRequiredService<GlobalOptions>();
var executor = sp.GetRequiredService<CommandExecutor>();

// ── Root command ──────────────────────────────────────────────────────────────
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

// ── Sub-commands ──────────────────────────────────────────────────────────────
root.Add(
    AuthCommand.Build(
        globalOptions,
        configStore,
        authService,
        executor,
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<IUmbracoManagementClientFactory>()
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
root.Add(StaticFileCommand.Build(executor, StaticFileKind.Stylesheet, "stylesheet", "stylesheet"));
root.Add(
    StaticFileCommand.Build(executor, StaticFileKind.PartialView, "partial-view", "partial view")
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

// Machine-readable command catalog for agents (#60). Added last and given the root so it can
// describe the fully-assembled tree (including itself).
root.Add(CommandsCommand.Build(globalOptions, root));

// Richer --version (#95): replace System.CommandLine's default version action so the output
// reports the tool version, target framework and runtime instead of just the assembly version.
foreach (var option in root.Options)
{
    if (option is VersionOption versionOption)
        versionOption.Action = new VersionCommandAction();
}

// ── Run ───────────────────────────────────────────────────────────────────────
// Parse with response-file expansion disabled (#115) so option values beginning with '@'
// (e.g. Serilog log-viewer filters like "@Level='Error'") are passed through verbatim.
// Readable parse errors for every id and date option, installed once on the finished tree.
ValueParsing.Apply(root);

var parsed = root.Parse(args, CliParserConfiguration.Create());

// #167: System.CommandLine reports a parse error as plain text plus the help screen, whichever
// output format was asked for - so `... -o json | jq` failed on the help text instead of reading
// an error envelope. Emit the same envelope every other failure uses before handing over.
if (parsed.Errors.Count > 0 && !ParseErrorReporter.IsHelpOrVersion(parsed))
    return ParseErrorReporter.Report(parsed, globalOptions);

return await parsed.InvokeAsync();
