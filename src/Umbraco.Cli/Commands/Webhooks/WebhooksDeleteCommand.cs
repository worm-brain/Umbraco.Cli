using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Webhooks;

public static class WebhooksDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a webhook subscription by UUID.\n\nExample:\n  umbraco webhooks delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.Destructive(parseResult => $"Delete webhook {parseResult.GetValue(idArg)}?");
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "webhooks.delete",
                    (client, c) => client.DeleteWebhookAsync(parseResult.GetValue(idArg), c),
                    "Webhook deleted.",
                    ct
                )
        );

        return cmd;
    }
}
