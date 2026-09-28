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

// Write UTF-8 everywhere, so non-ASCII text (the "Søg" example in dictionary help, Danish or
// Japanese names in JSON) survives on the Windows console and through pipes alike.
ConsoleEncoding.UseUtf8();

// Honour NO_COLOR (#94): strip colour from all Spectre.Console output when the variable is present.
ConsoleColorSetup.ApplyFromEnvironment();

// ── DI ────────────────────────────────────────────────────────────────────────
var services = new ServiceCollection();

// --verbose (#374): one gated logging handler on every client, switched on after parsing below.
// The default client carries the OAuth token exchange and the auth doctor probes, so they are
// logged too (with the client secret and tokens redacted).
services.AddSingleton<VerboseState>();
services.AddTransient<VerboseHttpHandler>();
services
    .AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
    .AddHttpMessageHandler<VerboseHttpHandler>();

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

// Error bodies are read as ProblemDetails (#286); one that is not JSON (a proxy's HTML page) is
// dropped here so the failure keeps its status instead of crashing the parse.
services.AddTransient<UnreadableErrorBodyHandler>();

// The verbose handler sits inside the 401 retry (so both attempts are logged) and outside the
// dry-run interceptor (so a recorded write is logged with its fake "not sent" response).
services
    .AddHttpClient("umbraco")
    .AddHttpMessageHandler<TokenRefreshHandler>()
    .AddHttpMessageHandler<UnreadableErrorBodyHandler>()
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

// Switch on the HTTP logging for every client at once, before any command (auth login and
// auth doctor included) makes a request (#374).
sp.GetRequiredService<VerboseState>().Enabled = parsed.GetValue(globalOptions.Verbose);

// #167: System.CommandLine reports a parse error as plain text plus the help screen, whichever
// output format was asked for - so `... -o json | jq` failed on the help text instead of reading
// an error envelope. Emit the same envelope every other failure uses before handing over.
if (parsed.Errors.Count > 0 && !ParseErrorReporter.IsHelpOrVersion(parsed))
    return ParseErrorReporter.Report(parsed, globalOptions);

return await parsed.InvokeAsync();
