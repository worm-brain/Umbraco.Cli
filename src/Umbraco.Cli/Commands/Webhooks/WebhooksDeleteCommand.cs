using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a webhook subscription by id.\n\nExamples:\n  umbraco webhook delete 3f7a8b2e-..."
        ).Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Webhook id." };
        cmd.Add(idArg);
        cmd.Destructive(parseResult => $"Delete webhook {parseResult.GetValue(idArg)}?");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .DeleteWebhookAsync(parseResult.GetValue(idArg), c)
                            .Then(ItemRef.Of(parseResult.GetValue(idArg))),
                    "Webhook deleted.",
                    ct
                )
        );

        return cmd;
    }
}
