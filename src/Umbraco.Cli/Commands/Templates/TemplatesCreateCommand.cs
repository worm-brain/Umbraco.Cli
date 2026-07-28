using System.CommandLine;
using Umbraco.Cli.Client;

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
        );
        var nameOpt = new Option<string>("--name") { Required = true };
        var aliasOpt = new Option<string>("--alias") { Required = true };
        var contentOpt = new Option<string?>("--content")
        {
            Description = "Razor view content (inline). Mutually exclusive with --content-file.",
        };
        var contentFileOpt = new Option<FileInfo?>("--content-file")
        {
            Description = "Path to a file whose contents become the Razor view.",
        };
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
                    "templates.create",
                    async (client, c) =>
                    {
                        // Prefer --content-file when given; else --content; else empty. The file
                        // read happens here (inside the executor's try) so a missing file becomes
                        // a clean error rather than an unhandled exception.
                        var file = parseResult.GetValue(contentFileOpt);
                        var content = file is not null
                            ? await File.ReadAllTextAsync(file.FullName, ct)
                            : parseResult.GetValue(contentOpt) ?? "";
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
