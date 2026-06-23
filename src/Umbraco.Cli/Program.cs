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

// ── DI ────────────────────────────────────────────────────────────────────────
var services = new ServiceCollection();
services.AddHttpClient();
services.AddSingleton<ConfigStore>();
services.AddSingleton<UmbracoAuthService>();
services.AddSingleton(sp => new CommandContextFactory(
    sp.GetRequiredService<ConfigStore>(),
    sp.GetRequiredService<UmbracoAuthService>(),
    sp.GetRequiredService<IHttpClientFactory>()));

var sp = services.BuildServiceProvider();
var configStore = sp.GetRequiredService<ConfigStore>();
var authService = sp.GetRequiredService<UmbracoAuthService>();
var factory = sp.GetRequiredService<CommandContextFactory>();

// ── Global options (recursive — available on every command) ───────────────────
var hostOpt = new Option<string?>("--host", new[] { "-H" })
    { Description = "Umbraco instance base URL (overrides config / UMBRACO_HOST)." };

var tokenOpt = new Option<string?>("--token")
    { Description = "Raw bearer token (overrides credential store)." };

var outputOpt = new Option<string?>("--output", new[] { "-o" })
    { Description = "Output format: json | human (default: json when piped, human in terminal)." };

var verboseOpt = new Option<bool>("--verbose", new[] { "-v" })
    { Description = "Write HTTP request/response details to stderr." };

var configOpt = new Option<string?>("--config")
    { Description = $"Path to config file (default: {ConfigStore.DefaultConfigPath})." };

// ── Root command ──────────────────────────────────────────────────────────────
var root = new RootCommand("Umbraco CLI — manage your Umbraco CMS from the terminal.");
root.Add(hostOpt);
root.Add(tokenOpt);
root.Add(outputOpt);
root.Add(verboseOpt);
root.Add(configOpt);

// ── Sub-commands ──────────────────────────────────────────────────────────────
root.Add(AuthCommand.Build(hostOpt, tokenOpt, outputOpt, configStore, authService, factory));
root.Add(ContentCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(MediaCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(ContentTypesCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(DataTypesCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(LanguagesCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(TemplatesCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(MembersCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(UsersCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(DictionaryCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
root.Add(WebhooksCommand.Build(hostOpt, tokenOpt, outputOpt, factory));

// ── Run ───────────────────────────────────────────────────────────────────────
return await root.Parse(args).InvokeAsync();
