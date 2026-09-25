using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.StaticFiles;

// FileContentInput lives in the parent Umbraco.Cli.Commands namespace (shared with Templates).

/// <summary>
/// Wires a static-file noun - <c>script</c>, <c>stylesheet</c>, or <c>partial-view</c> (issue #105).
/// The three share an identical path-addressed shape, so one factory builds the whole
/// list/get/create/update/delete verb set, parameterised by <see cref="StaticFileKind"/>, and
/// <c>Program.cs</c> registers it three times. Files are addressed by <b>path</b> (there is no
/// GUID); the path is passed through raw and the client encodes it.
/// </summary>
public static class StaticFileCommand
{
    /// <summary>Builds a static-file noun with its list/get/create/update/delete verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="kind">Which static-file resource this noun drives.</param>
    /// <param name="noun">The command name / URL slug (e.g. <c>script</c>, <c>partial-view</c>).</param>
    /// <param name="humanName">The singular human name for help/messages (e.g. <c>script</c>).</param>
    /// <returns>The configured noun command.</returns>
    public static Command Build(
        CommandExecutor executor,
        StaticFileKind kind,
        string noun,
        string humanName
    )
    {
        var cmd = new Command(
            noun,
            $"List, inspect, and manage Umbraco {humanName}s (addressed by file path).\n\n"
                + "Examples:\n"
                + $"  umbraco {noun} list\n"
                + $"  umbraco {noun} get folder/file\n"
                + $"  umbraco {noun} create --name file --content-file ./local"
        );
        cmd.Add(BuildList(executor, kind, noun));
        cmd.Add(BuildGet(executor, kind, noun));
        cmd.Add(BuildCreate(executor, kind, noun));
        cmd.Add(BuildUpdate(executor, kind, noun, humanName));
        cmd.Add(BuildDelete(executor, kind, noun, humanName));
        return cmd;
    }

    /// <summary>Builds the <c>list</c> verb (tree root, or a folder's children with <c>--parent</c>).</summary>
    private static Command BuildList(CommandExecutor executor, StaticFileKind kind, string noun)
    {
        var cmd = new Command(
            "list",
            $"List {noun} files and folders from the tree. --parent lists a folder's children."
        );
        var parentOpt = new Option<string?>("--parent")
        {
            Description = "Folder path to list children of; omit for the tree root.",
        };
        cmd.Add(parentOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetStaticFilesAsync(
                            kind,
                            parseResult.GetValue(parentOpt),
                            skip,
                            take,
                            c
                        ),
                    new[] { "Path", "Name", "Type" },
                    i => new[] { i.Path, i.Name, i.IsFolder ? "folder" : "file" },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds the <c>get</c> verb (single file by path, including content).</summary>
    private static Command BuildGet(CommandExecutor executor, StaticFileKind kind, string noun)
    {
        var cmd = new Command("get", $"Get a {noun} by path, including its content.");
        var pathArg = new Argument<string>("path") { Description = "The file path." };
        cmd.Add(pathArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.GetStaticFileAsync(kind, parseResult.GetValue(pathArg)!, c),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds the <c>create</c> verb.</summary>
    private static Command BuildCreate(CommandExecutor executor, StaticFileKind kind, string noun)
    {
        var cmd = new Command(
            "create",
            $"Create a {noun} file. Content comes from --content or --content-file (empty if neither)."
        ).Mutating();
        var nameOpt = new Option<string>("--name") { Required = true, Description = "File name." };
        var parentOpt = new Option<string?>("--parent")
        {
            Description = "Parent folder path; omit to create at the root.",
        };
        var (contentOpt, contentFileOpt) = FileContentInput.Options();
        cmd.Add(nameOpt);
        cmd.Add(parentOpt);
        cmd.Add(contentOpt);
        cmd.Add(contentFileOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        var content = await FileContentInput.ReadAsync(
                            parseResult,
                            contentOpt,
                            contentFileOpt,
                            c
                        );
                        return await client.CreateStaticFileAsync(
                            kind,
                            new CreateStaticFileRequest
                            {
                                Name = parseResult.GetValue(nameOpt)!,
                                ParentPath = parseResult.GetValue(parentOpt),
                                Content = content ?? "",
                            },
                            c
                        );
                    },
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds the <c>update</c> verb (replaces content; content is required).</summary>
    private static Command BuildUpdate(
        CommandExecutor executor,
        StaticFileKind kind,
        string noun,
        string humanName
    )
    {
        var cmd = new Command("update", $"Update a {noun}\'s content (by path).").Mutating();
        var pathArg = new Argument<string>("path") { Description = "The file path." };
        var (contentOpt, contentFileOpt) = FileContentInput.Options();
        cmd.Add(pathArg);
        cmd.Add(contentOpt);
        cmd.Add(contentFileOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        var content = await FileContentInput.ReadAsync(
                            parseResult,
                            contentOpt,
                            contentFileOpt,
                            c
                        );
                        // Update replaces content, so content is mandatory (unlike create's default).
                        if (content is null)
                            return UmbracoResponse<Empty>.Failure(
                                400,
                                "Provide --content or --content-file to update the file."
                            );
                        return await client.UpdateStaticFileAsync(
                            kind,
                            parseResult.GetValue(pathArg)!,
                            new UpdateStaticFileRequest { Content = content },
                            c
                        );
                    },
                    $"{humanName} updated.",
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds the <c>delete</c> verb (destructive; gated by confirmation).</summary>
    private static Command BuildDelete(
        CommandExecutor executor,
        StaticFileKind kind,
        string noun,
        string humanName
    )
    {
        var cmd = new Command("delete", $"Delete a {noun} by path.").Mutating();
        var pathArg = new Argument<string>("path") { Description = "The file path." };
        cmd.Add(pathArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete {noun} '{parseResult.GetValue(pathArg)}'? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client.DeleteStaticFileAsync(kind, parseResult.GetValue(pathArg)!, c),
                    $"{humanName} deleted.",
                    ct
                )
        );
        return cmd;
    }
}
