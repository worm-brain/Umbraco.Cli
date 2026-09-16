using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Templates;

/// <summary>Wires the <c>templates update</c> command (issue #59).</summary>
public static class TemplatesUpdateCommand
{
    /// <summary>
    /// Builds the <c>templates update</c> command. Only supplied options change; anything
    /// omitted is preserved by the client's read-merge, so updating the name alone never blanks
    /// the Razor content. Content may be supplied inline (<c>--content</c>) or from a file
    /// (<c>--content-file</c>).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a Razor view template by UUID. Omitted fields are preserved.\n\nExample:\n  umbraco templates update 3f7a8b2e-... --content-file ./home.cshtml"
        );
        var idArg = new Argument<Guid>("id") { Description = "Template ID." };
        var nameOpt = new Option<string?>("--name") { Description = "New name." };
        var aliasOpt = new Option<string?>("--alias") { Description = "New alias." };
        var (contentOpt, contentFileOpt) = FileContentInput.Options(
            "Razor view content (inline). Mutually exclusive with --content-file.",
            "Path to a file whose contents become the Razor view."
        );
        cmd.Add(idArg);
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(contentOpt);
        cmd.Add(contentFileOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "templates.update",
                    async (client, c) =>
                    {
                        // null content => preserve existing (read-merge in the client). Only a
                        // supplied --content/--content-file replaces the Razor body.
                        var content = await FileContentInput.ReadAsync(
                            parseResult,
                            contentOpt,
                            contentFileOpt,
                            ct
                        );
                        return await client.UpdateTemplateAsync(
                            parseResult.GetValue(idArg),
                            new UpdateTemplateRequest
                            {
                                Name = parseResult.GetValue(nameOpt),
                                Alias = parseResult.GetValue(aliasOpt),
                                Content = content,
                            },
                            c
                        );
                    },
                    "Template updated.",
                    ct
                )
        );

        return cmd;
    }
}
