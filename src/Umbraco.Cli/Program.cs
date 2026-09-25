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
// The whole tree is assembled in CliRoot so the tests and the surface snapshot see the same one.
var root = CliRoot.Build(
    globalOptions,
    configStore,
    authService,
    executor,
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<IUmbracoManagementClientFactory>()
);

// ── Run ───────────────────────────────────────────────────────────────────────
// Parse with response-file expansion disabled (#115) so option values beginning with '@'
// (e.g. Serilog log-viewer filters like "@Level='Error'") are passed through verbatim.
var parsed = root.Parse(args, CliParserConfiguration.Create());

// #167: System.CommandLine reports a parse error as plain text plus the help screen, whichever
// output format was asked for - so `... -o json | jq` failed on the help text instead of reading
// an error envelope. Emit the same envelope every other failure uses before handing over.
if (parsed.Errors.Count > 0 && !ParseErrorReporter.IsHelpOrVersion(parsed))
    return ParseErrorReporter.Report(parsed, globalOptions);

return await parsed.InvokeAsync();
