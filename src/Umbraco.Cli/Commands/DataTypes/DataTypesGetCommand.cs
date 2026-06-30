using System.CommandLine;

namespace Umbraco.Cli.Commands.DataTypes;

public static class DataTypesGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a data type (property editor) by UUID.\n\nExample:\n  umbraco data-types get 3f7a8b2e-...");
        var idArg = new Argument<Guid>("id"); cmd.Add(idArg);
        cmd.SetAction((parseResult, ct) => executor.RunObjectAsync(
            parseResult, "data-types.get",
            (client, c) => client.GetDataTypeByIdAsync(parseResult.GetValue(idArg), c),
            ct));

        return cmd;
    }
}
