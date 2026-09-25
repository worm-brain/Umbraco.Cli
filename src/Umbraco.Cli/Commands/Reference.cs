using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The command-line side of <c>&lt;id|alias&gt;</c> (#250 Phase 3): an argument or option that
/// takes an item's id <i>or</i> its alias, name or key as a string, for the command to resolve
/// with <see cref="IReferenceResolver"/>. Typing it as <see cref="Guid"/> is what made
/// <c>templates delete blogPost</c> a parse error while <c>templates get blogPost</c> worked.
/// </summary>
public static class Reference
{
    /// <summary>A positional argument naming one item of <paramref name="kind"/>.</summary>
    /// <param name="kind">What the argument names.</param>
    /// <param name="name">The argument name shown in help.</param>
    /// <returns>The argument.</returns>
    public static Argument<string> Argument(EntityKind kind, string name = "id") =>
        new(name) { Description = $"The {kind.Noun()}'s id, or its {kind.KeyName()}." };

    /// <summary>An optional option naming one item of <paramref name="kind"/>.</summary>
    /// <param name="name">The option name, e.g. <c>--parent</c>.</param>
    /// <param name="kind">What the option names.</param>
    /// <param name="purpose">What the item is for, e.g. <c>Parent item; omit for the root</c>.</param>
    /// <param name="aliases">Other names for the option.</param>
    /// <returns>The option.</returns>
    public static Option<string?> Option(
        string name,
        EntityKind kind,
        string purpose,
        params string[] aliases
    ) => new(name, aliases) { Description = $"{purpose}: its id, or its {kind.KeyName()}." };
}
