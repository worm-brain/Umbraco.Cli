using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Templates;

/// <summary>Wires the <c>templates delete</c> command (issue #59).</summary>
public static class TemplatesDeleteCommand
{
    /// <summary>Builds the <c>templates delete</c> command (destructive; gated by confirmation).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a Razor view template by UUID.\n\nExample:\n  umbraco templates delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "Template ID." };
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete template {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "templates.delete",
                    (client, c) => client.DeleteTemplateAsync(parseResult.GetValue(idArg), c),
                    "Template deleted.",
                    ct
                )
        );

        return cmd;
    }
}
