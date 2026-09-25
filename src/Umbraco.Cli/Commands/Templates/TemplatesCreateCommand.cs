using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Templates;

/// <summary>Wires the <c>templates create</c> command (issue #59).</summary>
public static class TemplatesCreateCommand
{
    /// <summary>
    /// Builds the <c>templates create</c> command. Content is the Razor view body; it defaults
    /// to empty and can be supplied inline with <c>--content</c> or read from a file with
    /// <c>--content-file</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a Razor view template.\n\nExamples:\n  umbraco templates create --name \"Blog Post\" --alias blogPost\n  umbraco templates create --name Home --alias home --content-file ./home.cshtml"
        ).Mutating();
        var nameOpt = new Option<string>("--name") { Required = true };
        var aliasOpt = new Option<string>("--alias") { Required = true };
        var (contentOpt, contentFileOpt) = FileContentInput.Options(
            "Razor view content (inline). Mutually exclusive with --content-file.",
            "Path to a file whose contents become the Razor view."
        );
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(contentOpt);
        cmd.Add(contentFileOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        // Prefer --content-file when given; else --content; else empty.
                        var content =
                            await FileContentInput.ReadAsync(
                                parseResult,
                                contentOpt,
                                contentFileOpt,
                                ct
                            ) ?? "";
                        return await client.CreateTemplateAsync(
                            new CreateTemplateRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Name = parseResult.GetValue(nameOpt)!,
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Content = content,
                            },
                            c
                        );
                    },
                    ct
                )
        );

        return cmd;
    }
}
