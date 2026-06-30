using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesCreateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new document type with a given name and alias.\n\nExamples:\n  umbraco content-types create --name \"Blog Post\" --alias blogPost\n  umbraco content-types create --name \"Widget\" --alias widget --is-element\n  umbraco content-types create --name \"Home Page\" --alias homePage --allow-at-root"
        );
        var nameOpt = new Option<string>("--name") { Required = true };
        var aliasOpt = new Option<string>("--alias") { Required = true };
        var descOpt = new Option<string?>("--description");
        var isElementOpt = new Option<bool>("--is-element") { DefaultValueFactory = _ => false };
        var allowRootOpt = new Option<bool>("--allow-at-root") { DefaultValueFactory = _ => false };
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(descOpt);
        cmd.Add(isElementOpt);
        cmd.Add(allowRootOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "content-types.create",
                    (client, c) =>
                        client.CreateDocumentTypeAsync(
                            new CreateDocumentTypeRequest
                            {
                                Name = parseResult.GetValue(nameOpt)!,
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Description = parseResult.GetValue(descOpt),
                                IsElement = parseResult.GetValue(isElementOpt),
                                AllowedAsRoot = parseResult.GetValue(allowRootOpt),
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
