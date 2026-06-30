using System.CommandLine;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("list", "List all document types defined in the Umbraco instance.\n\nExamples:\n  umbraco content-types list\n  umbraco content-types list --output json | jq '.[].alias'");
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(skipOpt); cmd.Add(takeOpt);
        cmd.SetAction((parseResult, ct) => executor.RunTableAsync(
            parseResult, "content-types.list",
            (client, c) => client.GetDocumentTypesAsync(parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), c),
            ["ID", "Name", "Alias", "IsElement"],
            data => data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.Alias, i.IsElement.ToString() }) ?? [],
            ct));

        return cmd;
    }
}
