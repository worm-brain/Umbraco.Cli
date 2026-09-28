using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires <c>member-type get</c>.</summary>
public static class MemberTypesGetCommand
{
    /// <summary>
    /// Builds the command. It prints the member type's verbatim Management API body (#250 Phase 5,
    /// #213), which is the shape <c>member-type update --json-body</c> takes back, so a
    /// get -&gt; edit -&gt; update round-trip loses nothing.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a member type by alias, name or id, as the full Management API body.\n\nThe body has its properties and groups. The output is a valid 'update --json-body'."
        ).WithExamples(
            "umbraco member-type get siteMember",
            "umbraco member-type get siteMember -o json | jq .data > mt.json"
        );
        var idArg = Reference.Argument(EntityKind.MemberType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunGetAsync(executor, parseResult, SchemaNoun.MemberTypes, idArg, ct)
        );

        return cmd;
    }
}
