using System.CommandLine;
using System.Runtime.CompilerServices;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Declares that an option or argument is required unless one of a set of other options is given
/// (#84) - e.g. <c>content create --name</c> unless <c>--json-body</c> or <c>--schema</c>. Such an
/// input cannot be <c>Required</c> at parse level, because then <c>--schema</c> could not run
/// without it, so the catalog used to report it <c>required: false</c>, which understated it.
/// <para>
/// The declaration is metadata for <c>umbraco commands</c> (<c>requiredUnless</c>); the rule itself
/// is still enforced by the command's own validator, which also carries the finer conditions
/// (conflicting flags, "at least one of") that a list of alternatives cannot express. Keep the two
/// together at the call site.
/// </para>
/// </summary>
public static class CommandRequirements
{
    // Keyed by the symbol instance, so the declaration lives and dies with the command tree
    // without adding a field to System.CommandLine's types (the same approach as CommandSafety).
    private static readonly ConditionalWeakTable<Symbol, IReadOnlyList<string>> Declarations =
        new();

    /// <summary>
    /// Declares <paramref name="symbol"/> required unless any of <paramref name="alternatives"/>
    /// is given, and appends the matching "Required unless ... is used." sentence to its help text,
    /// so the help and the catalog's <c>requiredUnless</c> come from one declaration and cannot
    /// disagree (#377). Set the description first and leave that sentence out of it.
    /// </summary>
    /// <typeparam name="TSymbol">The option or argument type, returned for chaining.</typeparam>
    /// <param name="symbol">The option or argument.</param>
    /// <param name="alternatives">The option names that make it unnecessary (e.g. <c>--json-body</c>).</param>
    /// <returns>The same symbol.</returns>
    /// <exception cref="ArgumentException">No alternatives were given.</exception>
    public static TSymbol RequiredUnless<TSymbol>(this TSymbol symbol, params string[] alternatives)
        where TSymbol : Symbol
    {
        if (alternatives.Length == 0)
            throw new ArgumentException(
                "Name at least one option that makes this input unnecessary.",
                nameof(alternatives)
            );
        Declarations.AddOrUpdate(symbol, alternatives);
        var sentence = Sentence(alternatives);
        symbol.Description = string.IsNullOrWhiteSpace(symbol.Description)
            ? sentence
            : $"{symbol.Description.TrimEnd()} {sentence}";
        return symbol;
    }

    /// <summary>The help sentence for a conditional requirement, e.g. "Required unless --json-body or --schema is used.".</summary>
    /// <param name="alternatives">The option names that make the input unnecessary; at least one.</param>
    /// <returns>The sentence.</returns>
    internal static string Sentence(IReadOnlyList<string> alternatives)
    {
        var list =
            alternatives.Count == 1
                ? alternatives[0]
                : $"{string.Join(", ", alternatives.Take(alternatives.Count - 1))} or {alternatives[^1]}";
        return $"Required unless {list} is used.";
    }

    /// <summary>The options that make <paramref name="symbol"/> unnecessary, if it declared any.</summary>
    /// <param name="symbol">The option or argument.</param>
    /// <returns>The alternative option names, or null when it has no conditional requirement.</returns>
    public static IReadOnlyList<string>? RequiredUnlessOf(Symbol symbol) =>
        Declarations.TryGetValue(symbol, out var alternatives) ? alternatives : null;
}
