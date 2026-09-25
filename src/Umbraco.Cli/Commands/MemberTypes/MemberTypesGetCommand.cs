using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires <c>member-types get</c>.</summary>
public static class MemberTypesGetCommand
{
    /// <summary>
    /// Builds the command. It prints the member type's verbatim Management API body (#250 Phase 5,
    /// #213), which is the shape <c>member-types update --json-body</c> takes back, so a
    /// get -&gt; edit -&gt; update round-trip loses nothing.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a member type by id or alias: the full Management API body, with its properties and groups. The output is a valid 'update --json-body'.\n\nExample:\n  umbraco member-types get siteMember"
        );
        var idArg = Reference.Argument(EntityKind.MemberType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunGetAsync(
                    executor,
                    parseResult,
                    "member-types.get",
                    idArg,
                    client => client.GetMemberTypeRawAsync,
                    ct
                )
        );

        return cmd;
    }
}
