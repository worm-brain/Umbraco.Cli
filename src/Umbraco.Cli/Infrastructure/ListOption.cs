using System.CommandLine;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The one shape for an option that takes several values (#231, #232): repeat the option, give
/// several values after it, <b>or</b> separate them with commas - <c>--cultures en-US,da-DK</c>,
/// <c>--cultures en-US da-DK</c> and <c>--cultures en-US --cultures da-DK</c> all mean the same.
/// The docs had always said <c>--cultures &lt;csv&gt;</c>, but a comma list was sent as one culture
/// (a 400), and <c>--children id1,id2</c> was a GUID parse error.
/// <para>
/// Splitting on commas is safe for every value these options take - culture codes, GUIDs, aliases,
/// section and permission aliases, log levels, event names - none of which can contain one. The
/// <c>key=value</c> options (<c>--value</c>, <c>--values</c>, <c>--domain</c>) deliberately do not
/// use this: a value may contain a comma.
/// </para>
/// </summary>
public static class ListOption
{
    /// <summary>Parses one item; false when it is not valid.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    public delegate bool ItemParser<T>(string text, out T value);

    private const string Several = "Repeat the option, or separate values with commas.";

    /// <summary>A list of strings.</summary>
    /// <param name="name">The option name.</param>
    /// <param name="description">What the values are.</param>
    /// <param name="aliases">Other names for the option.</param>
    /// <returns>The option.</returns>
    public static Option<string[]> Strings(
        string name,
        string description,
        params string[] aliases
    ) => Of(name, description, (string s, out string v) => (v = s).Length > 0, "a value", aliases);

    /// <summary>A list of GUID ids.</summary>
    /// <param name="name">The option name.</param>
    /// <param name="description">What the ids are.</param>
    /// <param name="aliases">Other names for the option.</param>
    /// <returns>The option.</returns>
    public static Option<Guid[]> Guids(string name, string description, params string[] aliases) =>
        Of<Guid>(name, description, Guid.TryParse, "GUID ids", aliases);

    /// <summary>A list of enum values, matched ignoring case.</summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="name">The option name.</param>
    /// <param name="description">What the values are.</param>
    /// <param name="aliases">Other names for the option.</param>
    /// <returns>The option.</returns>
    public static Option<T[]> Enums<T>(string name, string description, params string[] aliases)
        where T : struct, Enum =>
        Of(
            name,
            description,
            (string s, out T v) => Enum.TryParse(s, ignoreCase: true, out v) && Enum.IsDefined(v),
            $"one of {string.Join(", ", Enum.GetNames<T>())}",
            aliases
        );

    /// <summary>A list of <typeparamref name="T"/> parsed by <paramref name="parse"/>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="name">The option name.</param>
    /// <param name="description">What the values are.</param>
    /// <param name="parse">Parses one item.</param>
    /// <param name="expected">How the parse error names a valid item, e.g. <c>GUID ids</c>.</param>
    /// <param name="aliases">Other names for the option.</param>
    /// <returns>The option.</returns>
    public static Option<T[]> Of<T>(
        string name,
        string description,
        ItemParser<T> parse,
        string expected,
        params string[] aliases
    )
    {
        var option = new Option<T[]>(name, aliases)
        {
            Description = $"{description.TrimEnd()} {Several}",
            AllowMultipleArgumentsPerToken = true,
        };
        option.CustomParser = result =>
        {
            var values = new List<T>();
            var bad = new List<string>();
            foreach (var token in result.Tokens)
            foreach (
                var item in token.Value.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (parse(item, out var value))
                    values.Add(value);
                else
                    bad.Add(item);
            }

            // Ends with a full stop: the parse-error reporter appends "Run '... --help' for usage."
            if (bad.Count > 0)
                result.AddError(
                    $"{name} expects {expected}. Not understood: {string.Join(", ", bad)}."
                );
            return [.. values];
        };
        return option;
    }
}
