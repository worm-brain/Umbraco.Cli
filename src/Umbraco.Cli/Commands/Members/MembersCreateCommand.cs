using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Members;

public static class MembersCreateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new Umbraco member.\n\nExample:\n  umbraco members create --email user@example.com --name \"Jane Doe\" --type Member"
        );
        var emailOpt = new Option<string>("--email") { Required = true };
        var nameOpt = new Option<string>("--name") { Required = true };
        var typeOpt = new Option<string>("--type")
        {
            Description = "Alias of the member type (e.g. Member).",
            Required = true,
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(emailOpt);
        cmd.Add(nameOpt);
        cmd.Add(typeOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "members.create",
                    (client, c) =>
                        client.CreateMemberAsync(
                            new CreateMemberRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Email = parseResult.GetValue(emailOpt)!,
                                Name = parseResult.GetValue(nameOpt)!,
                                MemberType = new ContentTypeReference
                                {
                                    Alias = parseResult.GetValue(typeOpt)!,
                                },
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
