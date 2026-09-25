using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Templates;

/// <summary>Wires the <c>template update</c> command (issue #59).</summary>
public static class TemplatesUpdateCommand
{
    /// <summary>
    /// Builds the <c>template update</c> command. Only supplied options change; anything
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
            "Update a Razor view template by id or alias. Omitted fields are preserved. With --json-body, the body's top-level keys (as 'template get' prints them) are merged into the template; --replace sends it as the whole template.\n\nExamples:\n  umbraco template update blogPost --content-file ./blog-post.cshtml\n  umbraco template get blogPost -o json | jq .data > t.json\n  umbraco template update blogPost --json-body t.json"
        ).Mutating();
        var options = RawBodyCommand.AddUpdateOptions(cmd, SchemaNoun.Templates, hasFlags: true);
        var nameOpt = new Option<string?>("--name") { Description = "New name." };
        var aliasOpt = new Option<string?>("--alias") { Description = "New alias." };
        var (contentOpt, contentFileOpt) = FileContentInput.Options(
            "Razor view content (inline). Mutually exclusive with --content-file.",
            "Path to a file whose contents become the Razor view."
        );
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(contentOpt);
        cmd.Add(contentFileOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunUpdateAsync(
                    executor,
                    parseResult,
                    SchemaNoun.Templates,
                    options,
                    "Template updated.",
                    async (client, id, c) =>
                        await client.UpdateTemplateAsync(
                            id,
                            new UpdateTemplateRequest
                            {
                                Name = parseResult.GetValue(nameOpt),
                                Alias = parseResult.GetValue(aliasOpt),
                                // null content => preserve existing (read-merge in the client). Only
                                // a supplied --content/--content-file replaces the Razor body.
                                Content = await FileContentInput.ReadAsync(
                                    parseResult,
                                    contentOpt,
                                    contentFileOpt,
                                    c
                                ),
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
