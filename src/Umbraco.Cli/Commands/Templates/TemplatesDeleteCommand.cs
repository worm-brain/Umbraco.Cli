using System.CommandLine;
using Umbraco.Cli.Client;
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
            "Delete a Razor view template by id or alias.\n\nExample:\n  umbraco templates delete blogPost"
        );
        var idArg = Reference.Argument(EntityKind.Template);
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete template {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteTemplateAsync(id, c),
                            c
                        ),
                    "Template deleted.",
                    ct
                )
        );

        return cmd;
    }
}
