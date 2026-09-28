using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

/// <summary><c>webhook delete &lt;id&gt;</c>: remove a webhook subscription.</summary>
public static class WebhooksDeleteCommand
{
    /// <summary>
    /// Builds the <c>delete</c> leaf. It takes the webhook's name as well as its id, like
    /// <c>get</c> and <c>update</c> (docs/conventions.md 3.2, #237).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a webhook subscription by id or name.")
            .WithExamples(
                "umbraco webhook delete 3f7a8b2e-...",
                "umbraco webhook delete \"Deploy hook\" --yes"
            )
            .Mutating();
        var idArg = Reference.Argument(EntityKind.Webhook);
        cmd.Add(idArg);
        cmd.Destructive(parseResult => $"Delete webhook {parseResult.GetValue(idArg)}?");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteWebhookAsync(id, c).Then(ItemRef.Of(id)),
                            c
                        ),
                    "Webhook deleted.",
                    ct
                )
        );

        return cmd;
    }
}
