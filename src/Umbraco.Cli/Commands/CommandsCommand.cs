using System.CommandLine;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The <c>commands</c> command (#60): emits the full command tree as JSON so an agent or
/// script can discover the entire CLI surface — every command, its arguments, options, types,
/// and one-line help — in a single call instead of scraping <c>--help</c>. It is a local
/// introspection command: it makes no API call and needs no host or authentication.
/// </summary>
public static class CommandsCommand
{
    /// <summary>
    /// Builds the <c>commands</c> command.
    /// </summary>
    /// <param name="root">
    /// The root command to describe. Captured so the catalog reflects the fully-assembled tree
    /// at invocation time (this command is itself added to <paramref name="root"/>).
    /// </param>
    /// <returns>The configured <c>commands</c> command.</returns>
    public static Command Build(RootCommand root)
    {
        var cmd = new Command(
            "commands",
            "List every command as JSON (name, arguments, options, types, help) so agents and "
                + "scripts can discover the whole CLI surface in one call."
        );

        cmd.SetAction(
            (_, _) =>
            {
                // The catalog is inherently structured data for machines, so always emit JSON
                // (a human-table rendering of the whole tree would be noise).
                new JsonOutputWriter().WriteSuccess(CommandCatalog.Describe(root), "commands");
                return Task.FromResult(0);
            }
        );

        return cmd;
    }
}
