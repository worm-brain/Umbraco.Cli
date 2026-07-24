using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.ContentTypes;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Commands.Languages;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Commands.Members;
using Umbraco.Cli.Commands.Templates;
using Umbraco.Cli.Commands.Users;
using Umbraco.Cli.Commands.Webhooks;
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

// A second named client that logs request/response to stderr; selected by --verbose.
services.AddTransient<VerboseHttpHandler>();
services.AddHttpClient("umbraco-verbose").AddHttpMessageHandler<VerboseHttpHandler>();
services.AddSingleton<ConfigStore>();
services.AddSingleton<UmbracoAuthService>();
services.AddSingleton<GlobalOptions>();
services.AddSingleton<IUmbracoManagementClientFactory, UmbracoManagementClientFactory>();
services.AddSingleton(sp => new CommandContextFactory(
    sp.GetRequiredService<ConfigStore>(),
    sp.GetRequiredService<UmbracoAuthService>(),
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<GlobalOptions>(),
    sp.GetRequiredService<IUmbracoManagementClientFactory>()
));
services.AddSingleton(sp => new CommandExecutor(sp.GetRequiredService<CommandContextFactory>()));

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
root.Add(AuthCommand.Build(globalOptions, configStore, authService, executor));
root.Add(ContentCommand.Build(executor));
root.Add(MediaCommand.Build(executor));
root.Add(ContentTypesCommand.Build(executor));
root.Add(DataTypesCommand.Build(executor));
root.Add(LanguagesCommand.Build(executor));
root.Add(TemplatesCommand.Build(executor));
root.Add(MembersCommand.Build(executor));
root.Add(UsersCommand.Build(executor));
root.Add(DictionaryCommand.Build(executor));
root.Add(WebhooksCommand.Build(executor));

// ── Run ───────────────────────────────────────────────────────────────────────
return await root.Parse(args).InvokeAsync();
