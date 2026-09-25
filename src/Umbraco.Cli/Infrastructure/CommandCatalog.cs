using System.CommandLine;
using System.CommandLine.Help;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// A single command in the machine-readable catalog (#60): its name, help text, positional
/// arguments, options, and nested sub-commands. Serialized to JSON so an agent can discover
/// the whole CLI surface in one call instead of scraping <c>--help</c>.
/// </summary>
/// <param name="Name">The command token (e.g. <c>list</c>, <c>content</c>).</param>
/// <param name="Description">One-line help, or null when the command has none.</param>
/// <param name="Arguments">Positional arguments the command accepts.</param>
/// <param name="Options">Options specific to this command (recursive global options appear once, on the root).</param>
/// <param name="Commands">Nested sub-commands.</param>
/// <param name="Mutating">
/// True for a leaf command that changes server state (an API write). Agents should treat these
/// as blocked under <c>--readonly</c>. Declared by the command itself (#258).
/// </param>
/// <param name="Destructive">
/// True for a leaf command gated by a confirmation prompt — i.e. one that requires <c>--yes</c>
/// non-interactively (#70/#82). Covers irreversible commands (permanent <c>delete</c>,
/// <c>empty-recycle-bin</c>) and high-impact reversible ones (<c>unpublish</c> takes live
/// content offline). A subset of <see cref="Mutating"/>. Low-impact reversible writes
/// (trash/move/copy/publish) are mutating but not gated (#84).
/// </param>
/// <param name="AcceptsJsonBody">
/// True when the command takes a full request body via <c>--json-body</c> (and exposes its
/// shape via <c>--schema</c>); in that case its individual field options/args are alternatives
/// to the body, which is why they report <c>required: false</c> (#84).
/// </param>
public sealed record CommandCatalogNode(
    string Name,
    string? Description,
    IReadOnlyList<CommandCatalogArgument> Arguments,
    IReadOnlyList<CommandCatalogOption> Options,
    IReadOnlyList<CommandCatalogNode> Commands,
    bool Mutating = false,
    bool Destructive = false,
    bool AcceptsJsonBody = false,
    string? DestructiveWhen = null
);

/// <summary>A positional argument in the catalog.</summary>
/// <param name="Name">The argument name.</param>
/// <param name="Description">One-line help, or null.</param>
/// <param name="Type">A friendly type name (e.g. <c>string</c>, <c>guid</c>).</param>
/// <param name="Required">Whether the argument must be supplied.</param>
/// <param name="HasDefault">Whether the argument has a default value (so omitting it is valid) (#84).</param>
public sealed record CommandCatalogArgument(
    string Name,
    string? Description,
    string Type,
    bool Required,
    bool HasDefault = false
);

/// <summary>An option in the catalog.</summary>
/// <param name="Name">The primary option name (e.g. <c>--output</c>).</param>
/// <param name="Aliases">Alternative names (e.g. <c>-o</c>).</param>
/// <param name="Description">One-line help, or null.</param>
/// <param name="Type">A friendly type name; <c>flag</c> for a boolean switch.</param>
/// <param name="Required">Whether the option must be supplied.</param>
/// <param name="HasDefault">Whether the option has a default value (so omitting it is valid) (#84).</param>
public sealed record CommandCatalogOption(
    string Name,
    IReadOnlyList<string> Aliases,
    string? Description,
    string Type,
    bool Required,
    bool HasDefault = false
);

/// <summary>
/// Builds the <see cref="CommandCatalogNode"/> tree by reflecting over the live
/// <see cref="System.CommandLine"/> command tree — so the catalog is always in sync with the
/// real commands rather than a hand-maintained list that can drift (#60).
/// </summary>
public static class CommandCatalog
{
    /// <summary>Describes a command and everything beneath it.</summary>
    /// <param name="command">The command to describe (typically the root).</param>
    /// <returns>The catalog node for the command tree.</returns>
    public static CommandCatalogNode Describe(Command command)
    {
        // A leaf command (no sub-commands) is the thing that actually runs; only a leaf can be
        // mutating/destructive. Nouns (content, media, ...) just group verbs.
        var isLeaf = command.Subcommands.Count == 0;
        // Destructive comes from the command's own CommandSafety declaration - the same one the
        // executor gates on - so "needs --yes" in the catalog cannot drift from the real gate
        // (#255). Mutating comes from the command's own declaration too (#258): every write
        // declares .Mutating(), and a destructive command is always mutating.
        var destructive = isLeaf && CommandSafety.IsAlwaysDestructive(command);
        var destructiveWhen = isLeaf ? CommandSafety.DestructiveWhen(command) : null;
        var mutating = isLeaf && CommandSafety.IsDeclaredMutating(command);
        var acceptsJsonBody = command.Options.Any(o => o.Name == "--json-body");

        return new CommandCatalogNode(
            // The root command's name is derived from the executable path (e.g. "Umbraco.Cli"
            // under `dotnet exec`), which is environment-dependent — pin it to the shipped
            // command name so agents can key on a stable root.
            command is RootCommand
                ? "umbraco"
                : command.Name,
            NullIfEmpty(command.Description),
            command.Arguments.Select(DescribeArgument).ToList(),
            command.Options.Where(o => !IsHelpOrVersion(o)).Select(DescribeOption).ToList(),
            command.Subcommands.Select(Describe).ToList(),
            mutating,
            destructive,
            acceptsJsonBody,
            destructiveWhen
        );
    }

    private static CommandCatalogArgument DescribeArgument(Argument argument) =>
        new(
            argument.Name,
            NullIfEmpty(argument.Description),
            FriendlyType(argument.ValueType),
            // Required means the parser will reject its absence: the arity demands a value AND
            // there is no default to fall back on (an argument with a default parses fine when
            // omitted, so it is not required).
            argument.Arity.MinimumNumberOfValues > 0
                && !argument.HasDefaultValue,
            argument.HasDefaultValue
        );

    private static CommandCatalogOption DescribeOption(Option option) =>
        new(
            option.Name,
            option.Aliases.ToList(),
            NullIfEmpty(option.Description),
            FriendlyType(option.ValueType),
            option.Required,
            option.HasDefaultValue
        );

    /// <summary>
    /// Auto-generated help/version options are noise in the catalog — filter them out. Matched
    /// by type (not name) so a user-defined <c>--version</c> option on a command would not be
    /// wrongly dropped.
    /// </summary>
    private static bool IsHelpOrVersion(Option option) => option is HelpOption or VersionOption;

    /// <summary>
    /// Maps a CLR type to a short, agent-friendly type name. A boolean is rendered as
    /// <c>flag</c> (a presence switch). Nullable value types are unwrapped to their underlying
    /// type. Unknown types fall back to the CLR type name.
    /// </summary>
    /// <param name="type">The option/argument value type.</param>
    /// <returns>A friendly type name.</returns>
    private static string FriendlyType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        // A multi-valued option/argument (e.g. --event) surfaces as "string[]" rather than
        // the CLR "String[]".
        if (t.IsArray)
            return FriendlyType(t.GetElementType()!) + "[]";
        if (t == typeof(bool))
            return "flag";
        if (t == typeof(string))
            return "string";
        if (t == typeof(Guid))
            return "guid";
        if (t == typeof(int))
            return "int";
        if (t == typeof(long))
            return "long";
        if (t == typeof(System.IO.FileInfo))
            return "file";
        if (t == typeof(System.IO.DirectoryInfo))
            return "directory";
        if (t == typeof(Uri))
            return "uri";
        return t.Name;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
