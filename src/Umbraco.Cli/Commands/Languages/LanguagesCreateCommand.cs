using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Languages;

public static class LanguagesCreateCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("create", "Add a language.");
        var cultureOpt = new Option<string>("--culture") { Description = "ISO 4646 culture code for the language (e.g. en-US, fr-FR, da-DK).", Required = true  };
        var defaultOpt = new Option<bool>("--default") { DefaultValueFactory = _ => false, Description = "Set this language as the default for new content." };
        var mandatoryOpt = new Option<bool>("--mandatory") { DefaultValueFactory = _ => false, Description = "Mark this language as mandatory (content must be translated into it)." };
        cmd.Add(cultureOpt); cmd.Add(defaultOpt); cmd.Add(mandatoryOpt);
        cmd.SetAction((parseResult, ct) => executor.RunObjectAsync(
            parseResult, "languages.create",
            (client, c) => client.CreateLanguageAsync(new CreateLanguageRequest { IsoCode = parseResult.GetValue(cultureOpt)!, IsDefault = parseResult.GetValue(defaultOpt), IsMandatory = parseResult.GetValue(mandatoryOpt) }, c),
            ct));

        return cmd;
    }
}
