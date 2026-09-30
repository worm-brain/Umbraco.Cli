using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>
/// The translation inputs of <c>dictionary create</c> and <c>dictionary update</c>: inline
/// <c>--value isoCode=text</c> pairs, and <c>--value-file isoCode=path</c> pairs whose text comes
/// from a file, or from stdin for <c>-</c>. A multi-line value (Markdown, HTML) then needs no shell
/// quoting, which differs between bash, PowerShell and cmd.
/// <para>
/// Each ISO code has one source (docs/conventions.md 4.3 and 4.5): one named by a
/// <c>--value-file</c> cannot be named again, by <c>--value</c> or by another <c>--value-file</c>.
/// Stdin can be read once, so at most one path is <c>-</c>. These rules, and a path that is empty,
/// are checked at parse time; the files are read
/// later, inside the executor, so a missing one is an <c>invalid_argument</c> error envelope rather
/// than a crash.
/// </para>
/// </summary>
public static class DictionaryTranslationInput
{
    /// <summary>Builds the <c>--value-file</c> option, for the command to add after <c>--value</c>.</summary>
    /// <returns>The option. Pass it to <see cref="Validate"/> and <see cref="ReadAsync"/>.</returns>
    public static Option<string[]> FileOption() =>
        new("--value-file")
        {
            Description =
                "Translation pairs in isoCode=path format: the file is read as UTF-8 and stored exactly as it is, or - reads stdin (one - per command). Repeat for multiple languages: --value-file en-US=intro.md --value-file da-DK=intro.da.md. An ISO code given here cannot be given again, here or with --value. An empty path (en-US=) is refused.",
            AllowMultipleArgumentsPerToken = true,
        };

    /// <summary>
    /// Adds the parse-time checks for <paramref name="files"/>: each token is <c>isoCode=path</c>
    /// with a path, no ISO code is given twice across the two options, and at most one path is
    /// <c>-</c>.
    /// </summary>
    /// <param name="cmd">The <c>dictionary create</c> or <c>update</c> command.</param>
    /// <param name="values">The command's <c>--value</c> option, for ISO codes it shares with <paramref name="files"/>.</param>
    /// <param name="files">The command's <c>--value-file</c> option.</param>
    public static void Validate(Command cmd, Option<string[]> values, Option<string[]> files)
    {
        KeyValuePairs.Validate(
            cmd,
            files,
            "--value-file must be isoCode=path, e.g. en-US=intro.md"
        );
        cmd.Validators.Add(result =>
        {
            foreach (var problem in Problems(result.GetValue(values), result.GetValue(files)))
                result.AddError(problem);
        });
    }

    /// <summary>What is wrong with the two options taken together.</summary>
    /// <param name="values">The raw <c>--value</c> tokens, or null when not given.</param>
    /// <param name="files">The raw <c>--value-file</c> tokens, or null when not given.</param>
    /// <returns>
    /// One message per problem, each ending with a full stop (the parse-error reporter appends a
    /// sentence straight after); empty when the pair is sound.
    /// </returns>
    internal static IEnumerable<string> Problems(string[]? values, string[]? files)
    {
        var filePairs = WellFormed(files).ToList();

        foreach (var (iso, _) in filePairs.Where(p => p.Value.Length == 0))
            yield return $"--value-file {iso}= names no file. Give a path, or use --value {iso}= for an empty translation.";

        // ISO codes are compared the way Umbraco merges translations: ignoring case.
        var named = filePairs.Select(p => p.Key).Concat(WellFormed(values).Select(p => p.Key));
        var repeated = filePairs
            .Select(p => p.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(iso =>
                named.Count(n => string.Equals(n, iso, StringComparison.OrdinalIgnoreCase)) > 1
            )
            .ToList();
        if (repeated.Count > 0)
            yield return $"Each ISO code takes one translation, from --value or --value-file. Given more than once: {string.Join(", ", repeated)}.";

        var stdin = filePairs
            .Where(p => p.Value == JsonBodyInput.StdinToken)
            .Select(p => p.Key)
            .ToList();
        if (stdin.Count > 1)
            yield return $"Stdin can be read once, so only one --value-file can be -. Given for: {string.Join(", ", stdin)}.";
    }

    /// <summary>
    /// The translations to send: the <c>--value</c> pairs, then the <c>--value-file</c> pairs with
    /// each file's text. Call it inside the executor's call, so a file that is not there is
    /// reported as an error envelope.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="values">The command's <c>--value</c> option.</param>
    /// <param name="files">The command's <c>--value-file</c> option.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One translation per pair; empty when neither option was given.</returns>
    /// <exception cref="FileNotFoundException">A <c>--value-file</c> path does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">A <c>--value-file</c> path's folder does not exist.</exception>
    public static async Task<IReadOnlyList<DictionaryTranslation>> ReadAsync(
        ParseResult parseResult,
        Option<string[]> values,
        Option<string[]> files,
        CancellationToken ct
    )
    {
        var translations = KeyValuePairs
            .Parse(parseResult.GetValue(values))
            .Select(p => new DictionaryTranslation { IsoCode = p.Key, Translation = p.Value })
            .ToList();

        // The same reader as --json-body: a file is read like --content-file reads one (UTF-8, a
        // byte-order mark dropped, nothing trimmed, so a final newline stays), and stdin is decoded
        // as UTF-8 too, rather than through Console.In, which uses the console code page on Windows.
        foreach (var (iso, path) in KeyValuePairs.Parse(parseResult.GetValue(files)))
            translations.Add(
                new DictionaryTranslation
                {
                    IsoCode = iso,
                    Translation = await JsonBodyInput.ReadAsync(path, ct),
                }
            );

        return translations;
    }

    /// <summary>
    /// The pairs among <paramref name="raw"/> that have an ISO code and an <c>=</c>. The others are
    /// reported by the <see cref="KeyValuePairs.Validate"/> shape check; checking them here too would
    /// report one typo twice.
    /// </summary>
    /// <param name="raw">The raw option tokens, or null.</param>
    /// <returns>The well-formed pairs, in the order given.</returns>
    private static IEnumerable<(string Key, string Value)> WellFormed(string[]? raw) =>
        (raw ?? [])
            .Select(v => v.Split('=', 2))
            .Where(p => p is [{ Length: > 0 }, _])
            .Select(p => (p[0], p[1]));
}
