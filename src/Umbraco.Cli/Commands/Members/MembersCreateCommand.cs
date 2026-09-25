using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Members;

public static class MembersCreateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new Umbraco member.\n\nExample:\n  umbraco members create --email user@example.com --name \"Jane Doe\" --type Member"
        ).Mutating();
        var emailOpt = new Option<string>("--email") { Required = true };
        var nameOpt = new Option<string>("--name") { Required = true };
        var typeOpt = new Option<string>("--type")
        {
            Description = "Alias of the member type (e.g. Member).",
            Required = true,
        };
        var passwordOpt = new Option<string?>("--password")
        {
            // #136: without a password the API rejects the create ("did not meet the complexity
            // requirements"). When omitted we generate a policy-compliant one so a member can be
            // provisioned non-interactively; it is not returned, so pass --password to set a known one.
            Description =
                "Member password. If omitted, a random policy-compliant password is generated "
                + "(not returned in the response; supply --password to set a known one).",
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(emailOpt);
        cmd.Add(nameOpt);
        cmd.Add(typeOpt);
        cmd.Add(passwordOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.CreateMemberAsync(
                            new CreateMemberRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Email = parseResult.GetValue(emailOpt)!,
                                Name = parseResult.GetValue(nameOpt)!,
                                // Use the supplied password, or a generated compliant one (#136).
                                Password = parseResult.GetValue(passwordOpt) is { Length: > 0 } pw
                                    ? pw
                                    : PasswordGenerator.Generate(),
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
