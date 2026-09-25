using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-types get</c> command (issue #56).</summary>
public static class MemberTypesGetCommand
{
    /// <summary>Builds the <c>member-types get</c> command (fetch a member type by id).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a member type by id or alias, including its alias and description.\n\nExample:\n  umbraco member-types get siteMember"
        );
        var idArg = Reference.Argument(EntityKind.MemberType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "member-types.get",
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.GetMemberTypeByIdAsync(id, c),
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
