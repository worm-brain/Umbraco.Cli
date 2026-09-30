using System.CommandLine;
using System.CommandLine.Parsing;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Parsing and validation for the repeatable <c>key=value</c> options the CLI uses for
/// translations (<c>--value en-US=Home</c>), property values (<c>--value author=Ann</c>) and
/// domain bindings (<c>--domain example.com=en-US</c>).
/// <para>
/// Every one of these used to parse with <c>.Split('=', 2).Where(p =&gt; p.Length == 2)</c>, which
/// silently <b>discards</b> a token with no <c>=</c> in it. A typo (<c>--value en-US Home</c>,
/// two tokens because the shell ate the quotes) therefore reported success having written
/// nothing - the same "said it worked, did nothing" failure as #158. Validation belongs at parse
/// time, where it costs the caller a clear message and no API call at all.
/// </para>
/// <para>
/// A key is given once per option (docs/conventions.md 4.3, #444). Two values for one key used to
/// be settled by whichever the command happened to keep - the last one, or both sent for Umbraco
/// to pick - which is the silent precedence 4.5 rules out.
/// </para>
/// </summary>
public static class KeyValuePairs
{
    /// <summary>
    /// How the property-value options (<c>--value alias=value</c> on <c>media upload</c>,
    /// <c>media update</c> and <c>member update</c>) compare aliases: exactly, case included.
    /// Umbraco's content editing service looks a value's alias up in a plain dictionary of the
    /// type's property aliases, and the CLI's own merge keys values the same way, so
    /// <c>title</c> and <c>Title</c> are two keys: one is the property, the other an unknown
    /// alias that Umbraco refuses rather than merges.
    /// </summary>
    public static readonly IEqualityComparer<string> PropertyAliases = StringComparer.Ordinal;

    /// <summary>
    /// Adds the parse-time checks of a pair option: every token is <c>key=value</c> with a key, and
    /// no key is given twice. Every pair option uses this, except one whose keys are shared with
    /// another option (see <see cref="ValidateShape"/>).
    /// </summary>
    /// <param name="cmd">The command to validate.</param>
    /// <param name="option">The repeatable pair option.</param>
    /// <param name="shape">
    /// The expected shape, shown in the error - e.g. <c>"--value must be isoCode=translation, e.g.
    /// en-US=Home"</c>.
    /// </param>
    /// <param name="keys">
    /// When two keys are the same one: the comparison the command's target uses, so two keys that
    /// would land on the same entry count as a repeat. <see cref="StringComparer.OrdinalIgnoreCase"/>
    /// for ISO codes, hostnames and header names; <see cref="PropertyAliases"/> for aliases.
    /// </param>
    public static void Validate(
        Command cmd,
        Option<string[]> option,
        string shape,
        IEqualityComparer<string> keys
    )
    {
        ValidateShape(cmd, option, shape);
        cmd.Validators.Add(result =>
        {
            var repeated = Repeated(WellFormed(result.GetValue(option)).Select(p => p.Key), keys);
            if (repeated.Count > 0)
                result.AddError(
                    $"Each key can be given once in {option.Name}. Given more than once: {string.Join(", ", repeated)}."
                );
        });
    }

    /// <summary>
    /// Adds only the shape check of <see cref="Validate"/>: every token is <c>key=value</c> with a
    /// key. For an option whose keys share one key space with another option, where a validator
    /// spanning both refuses a repeated key (dictionary <c>--value</c> and <c>--value-file</c>).
    /// </summary>
    /// <param name="cmd">The command to validate.</param>
    /// <param name="option">The repeatable pair option.</param>
    /// <param name="shape">The expected shape, shown in the error.</param>
    public static void ValidateShape(Command cmd, Option<string[]> option, string shape)
    {
        cmd.Validators.Add(result =>
        {
            // An empty key (a leading "=") is as unusable as a missing one: Umbraco would take
            // "" as the alias/ISO code and quietly store nothing useful.
            var bad = (result.GetValue(option) ?? [])
                .Where(v => v.Split('=', 2) is not [{ Length: > 0 }, _])
                .ToArray();
            if (bad.Length > 0)
                // Ends with a full stop: the parse-error reporter appends "Run '<cmd> --help' for
                // usage." straight after, and without one the two sentences run together.
                result.AddError($"Each {shape}. Not understood: {string.Join(", ", bad)}.");
        });
    }

    /// <summary>The keys that appear more than once.</summary>
    /// <param name="keys">The keys, in the order given.</param>
    /// <param name="comparer">When two keys are the same one.</param>
    /// <returns>
    /// Each repeated key once, spelled as it was first given, in the order first given; empty when
    /// every key is distinct.
    /// </returns>
    public static IReadOnlyList<string> Repeated(
        IEnumerable<string> keys,
        IEqualityComparer<string> comparer
    ) => [.. keys.GroupBy(k => k, comparer).Where(g => g.Skip(1).Any()).Select(g => g.Key)];

    /// <summary>
    /// The pairs among <paramref name="raw"/> that have a key and an <c>=</c>. The others are
    /// reported by the shape check, so a rule over the pairs skips them rather than reporting one
    /// typo twice.
    /// </summary>
    /// <param name="raw">The raw option values, or null when the option was not given.</param>
    /// <returns>The well-formed pairs, in the order given.</returns>
    public static IEnumerable<(string Key, string Value)> WellFormed(string[]? raw) =>
        (raw ?? [])
            .Select(v => v.Split('=', 2))
            .Where(p => p is [{ Length: > 0 }, _])
            .Select(p => (p[0], p[1]));

    /// <summary>Splits validated pairs into key/value tuples.</summary>
    /// <param name="raw">The raw option values, or null when the option was not given.</param>
    /// <returns>One tuple per pair, in the order given.</returns>
    /// <remarks>
    /// Anything malformed has already been refused by <see cref="Validate"/>, so this does not
    /// filter - a pair reaching here is a pair the caller meant.
    /// </remarks>
    public static IEnumerable<(string Key, string Value)> Parse(string[]? raw) =>
        (raw ?? []).Select(v => v.Split('=', 2)).Select(p => (p[0], p.Length > 1 ? p[1] : ""));
}
