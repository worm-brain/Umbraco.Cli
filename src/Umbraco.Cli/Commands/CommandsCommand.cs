using System.CommandLine;
using System.Text;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The <c>commands</c> command (#60): emits the full command tree so an agent or script can
/// discover the entire CLI surface — every command, its arguments, options, types, and
/// one-line help — in a single call instead of scraping <c>--help</c>. It is a local
/// introspection command: it makes no API call and needs no host or authentication.
/// JSON (the default, and what agents want) returns the structured tree; <c>--output human</c>
/// prints an indented outline.
/// </summary>
public static class CommandsCommand
{
    /// <summary>
    /// Builds the <c>commands</c> command.
    /// </summary>
    /// <param name="globalOptions">The shared global options (used to resolve the output format).</param>
    /// <param name="root">
    /// The root command to describe. Captured so the catalog reflects the fully-assembled tree
    /// at invocation time (this command is itself added to <paramref name="root"/>).
    /// </param>
    /// <returns>The configured <c>commands</c> command.</returns>
    public static Command Build(GlobalOptions globalOptions, RootCommand root)
    {
        var cmd = new Command(
            "commands",
            "List every command, with its arguments, options, types and help.\n\n"
                + "Agents and scripts can discover the whole CLI surface in one call. JSON by default; "
                + "--output human prints an outline.\n\n"
                + "Examples:\n  umbraco commands\n  umbraco commands --output human\n"
                + "  umbraco commands | jq '.data.commands[].name'"
        );

        cmd.SetAction(
            (parseResult, _) =>
            {
                var catalog = CommandCatalog.Describe(root);
                var format = OutputFormatParser.Parse(parseResult.GetValue(globalOptions.Output));
                if (format == OutputFormat.Human)
                    Console.WriteLine(RenderHuman(catalog));
                else
                    new JsonOutputWriter().WriteSuccess(catalog, "commands");
                return Task.FromResult(0);
            }
        );

        return cmd;
    }

    /// <summary>
    /// Renders the catalog as an indented human outline: one line per command showing its path
    /// and one-line help, nested by depth.
    /// </summary>
    /// <param name="root">The root catalog node.</param>
    /// <returns>The formatted outline.</returns>
    private static string RenderHuman(CommandCatalogNode root)
    {
        var sb = new StringBuilder();
        // The root itself is the program; start the outline at its children.
        foreach (var child in root.Commands)
            AppendNode(sb, child, 0);
        return sb.ToString().TrimEnd();
    }

    private static void AppendNode(StringBuilder sb, CommandCatalogNode node, int depth)
    {
        var indent = new string(' ', depth * 2);
        // Use only the first line of the description — some commands carry multi-line help with
        // examples, which would break the one-line-per-command outline.
        var firstLine = node.Description?.Split('\n')[0].Trim();
        var help = string.IsNullOrEmpty(firstLine) ? "" : $" — {firstLine}";
        sb.AppendLine($"{indent}{node.Name}{help}");
        foreach (var child in node.Commands)
            AppendNode(sb, child, depth + 1);
    }
}
