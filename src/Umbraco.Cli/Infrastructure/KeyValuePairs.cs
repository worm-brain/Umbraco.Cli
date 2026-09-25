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
/// </summary>
public static class KeyValuePairs
{
    /// <summary>
    /// Adds a validator rejecting any token that is not <c>key=value</c>, naming the offenders.
    /// </summary>
    /// <param name="cmd">The command to validate.</param>
    /// <param name="option">The repeatable pair option.</param>
    /// <param name="shape">
    /// The expected shape, shown in the error - e.g. <c>"--value isoCode=translation, e.g.
    /// en-US=Home"</c>.
    /// </param>
    public static void Validate(Command cmd, Option<string[]> option, string shape)
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
