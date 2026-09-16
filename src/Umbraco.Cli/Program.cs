using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.ContentTypes;
using Umbraco.Cli.Commands.Cultures;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Commands.Languages;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Commands.MediaTypes;
using Umbraco.Cli.Commands.MemberGroups;
using Umbraco.Cli.Commands.Members;
using Umbraco.Cli.Commands.MemberTypes;
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

// ── DI ────────────────────────────────────────────────────────────────────────
var services = new ServiceCollection();
services.AddHttpClient();

// Mutation interceptor: powers --dry-run (and, later, #69/#70). Registered as the innermost
// handler on both Management-API clients so it sees the fully-built request; gated by the
// per-invocation MutationInterceptState so it is a no-op unless --dry-run is set. Not added
// to the auth (default) client, so the OAuth token exchange is never intercepted.
services.AddSingleton<MutationInterceptState>();
services.AddTransient<MutationInterceptorHandler>();
services.AddHttpClient("umbraco").AddHttpMessageHandler<MutationInterceptorHandler>();

// A second named client that logs request/response to stderr; selected by --verbose.
services.AddTransient<VerboseHttpHandler>();
services
    .AddHttpClient("umbraco-verbose")
    .AddHttpMessageHandler<VerboseHttpHandler>()
    .AddHttpMessageHandler<MutationInterceptorHandler>();
services.AddSingleton<ConfigStore>();
services.AddSingleton<UmbracoAuthService>();
services.AddSingleton<GlobalOptions>();
services.AddSingleton<IUmbracoManagementClientFactory, UmbracoManagementClientFactory>();
services.AddSingleton(sp => new CommandContextFactory(
    sp.GetRequiredService<ConfigStore>(),
    sp.GetRequiredService<UmbracoAuthService>(),
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<GlobalOptions>(),
    sp.GetRequiredService<IUmbracoManagementClientFactory>(),
    sp.GetRequiredService<MutationInterceptState>()
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

// Machine-readable command catalog for agents (#60). Added last and given the root so it can
// describe the fully-assembled tree (including itself).
root.Add(CommandsCommand.Build(globalOptions, root));

// ── Run ───────────────────────────────────────────────────────────────────────
return await root.Parse(args).InvokeAsync();
