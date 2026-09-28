using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Templates;

/// <summary>Wires the <c>template delete</c> command (issue #59).</summary>
public static class TemplatesDeleteCommand
{
    /// <summary>Builds the <c>template delete</c> command (destructive; gated by confirmation).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a Razor view template by id or alias.\n\n"
                + "A template that a document type allows or defaults to is refused unless --force is given: "
                + "those document types, and their documents, would be left without it."
        )
            .WithExamples(
                "umbraco template delete blogPost --yes",
                "umbraco template delete blogPost --force --yes"
            )
            .Mutating();
        var idArg = Reference.Argument(EntityKind.Template);
        cmd.Add(idArg);
        InUseGuard.Protect(
            cmd,
            idArg,
            "Delete even though document types use the template, leaving them without it."
        );
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
                            id => client.DeleteTemplateAsync(id, c).Then(ItemRef.Of(id)),
                            c
                        ),
                    "Template deleted.",
                    ct
                )
        );

        return cmd;
    }
}
