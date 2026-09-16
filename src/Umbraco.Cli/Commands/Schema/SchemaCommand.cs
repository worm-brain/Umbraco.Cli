using System.CommandLine;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// The <c>schema</c> noun (issue #68 / ADR 0005): export, diff, and apply an instance's
/// document types, data types, and templates as a portable JSON snapshot — the CLI's
/// CI/agent-facing schema pipeline, complementing uSync but driven from any shell.
/// </summary>
public static class SchemaCommand
{
    /// <summary>Builds the <c>schema</c> command tree and wires its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured <c>schema</c> command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "schema",
            "Export, diff, and apply Umbraco schema (document types, data types, templates).\n\n"
                + "Examples:\n"
                + "  umbraco schema export --out schema.json\n"
                + "  umbraco schema diff schema.json\n"
                + "  umbraco schema apply schema.json --dry-run\n"
                + "  umbraco schema apply schema.json --prune --yes"
        );
        cmd.Add(SchemaExportCommand.Build(executor));
        cmd.Add(SchemaDiffCommand.Build(executor));
        cmd.Add(SchemaApplyCommand.Build(executor));
        return cmd;
    }
}
