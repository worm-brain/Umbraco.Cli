using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a data type by name or UUID, including its editor configuration.\n\nA data type has no alias - 'editorAlias' names the property editor behind it, which many data types share - so the human-facing key is its name.\n\nExamples:\n  umbraco data-types get Textstring\n  umbraco data-types get 3f7a8b2e-..."
        );
        var idArg = new Argument<string>("id")
        {
            Description = "Data type name (e.g. Textstring) or UUID.",
        };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "data-types.get",
                    (client, c) => client.GetDataTypeAsync(parseResult.GetValue(idArg)!, c),
                    ct
                )
        );

        return cmd;
    }
}
