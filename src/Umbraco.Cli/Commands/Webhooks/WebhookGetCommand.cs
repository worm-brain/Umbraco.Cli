using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary><c>webhook get &lt;id&gt;</c> (#237): one webhook, by id or name.</summary>
public static class WebhookGetCommand
{
    /// <summary>Builds the <c>get</c> leaf under <c>webhook</c>.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a webhook by id or name.").WithExamples(
            "umbraco webhook get 3f7a8b2e-...",
            "umbraco webhook get \"Deploy hook\""
        );
        var idArg = Reference.Argument(EntityKind.Webhook);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.GetWebhookAsync(id, c),
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }
}
