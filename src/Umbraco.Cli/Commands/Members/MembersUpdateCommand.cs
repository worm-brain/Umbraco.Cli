using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Members;

/// <summary>Wires the <c>members update</c> command (issue #59).</summary>
public static class MembersUpdateCommand
{
    /// <summary>
    /// Builds the <c>members update</c> command. Only the supplied options are changed — the
    /// client reads the current member and merges them, so groups, property values and password
    /// are preserved (the member PUT is otherwise a full replace).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a member's email, name, or approved state by UUID.\n\nExamples:\n  umbraco members update 3f7a8b2e-... --name \"Jane Roe\"\n  umbraco members update 3f7a8b2e-... --email jane@example.com --approved"
        );
        var idArg = new Argument<Guid>("id") { Description = "Member ID." };
        var emailOpt = new Option<string?>("--email") { Description = "New email address." };
        var nameOpt = new Option<string?>("--name") { Description = "New display name." };
        var approvedOpt = new Option<bool?>("--approved")
        {
            Description = "Set the member's approved state (--approved or --approved false).",
        };
        cmd.Add(idArg);
        cmd.Add(emailOpt);
        cmd.Add(nameOpt);
        cmd.Add(approvedOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "members.update",
                    (client, c) =>
                        client.UpdateMemberAsync(
                            parseResult.GetValue(idArg),
                            new UpdateMemberRequest
                            {
                                Email = parseResult.GetValue(emailOpt),
                                Name = parseResult.GetValue(nameOpt),
                                IsApproved = parseResult.GetValue(approvedOpt),
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
