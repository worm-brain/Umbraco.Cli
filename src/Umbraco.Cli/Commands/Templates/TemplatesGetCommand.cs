using System.CommandLine;

namespace Umbraco.Cli.Commands.Templates;

public static class TemplatesGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a template by its alias, including the view file content.\n\nExample:\n  umbraco templates get master\n  umbraco templates get textPage"
        );
        var aliasArg = new Argument<string>("alias");
        cmd.Add(aliasArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "templates.get",
                    (client, c) =>
                        client.GetTemplateByAliasAsync(parseResult.GetValue(aliasArg)!, c),
                    ct
                )
        );

        return cmd;
    }
}
