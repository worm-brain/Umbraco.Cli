using System.CommandLine;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Extensions;

/// <summary>
/// The context options an extension command line was given (ADR 0010): where and how its calls
/// back into the CLI should run. Each is null or false when the line did not give it, so the
/// extension inherits whatever the environment already says.
/// </summary>
/// <param name="Host">The <c>--host</c> value.</param>
/// <param name="Token">The <c>--token</c> value: the one way a token reaches an extension.</param>
/// <param name="Config">The <c>--config</c> value, as typed.</param>
/// <param name="Profile">The <c>--profile</c> value.</param>
/// <param name="Output">The <c>--output</c> value, already checked to be a format.</param>
/// <param name="ReadOnly">Whether <c>--readonly</c> was given.</param>
/// <param name="DryRun">Whether <c>--dry-run</c> was given.</param>
public sealed record ExtensionContext(
    string? Host = null,
    string? Token = null,
    string? Config = null,
    string? Profile = null,
    string? Output = null,
    bool ReadOnly = false,
    bool DryRun = false
)
{
    /// <summary>
    /// The variables to add to the extension's environment, on top of the CLI's own (which it
    /// inherits whole, so <c>UMBRACO_ALLOWED_COMMANDS</c> and any <c>UMBRACO_READONLY</c> reach it
    /// unchanged). Each is read back by <see cref="GlobalOptions"/> as that option's default, so a
    /// call the extension makes runs as if the same options had been typed on it. Only options the
    /// line gave are set: nothing here can clear a variable the environment already holds.
    /// <para>
    /// <c>UMBRACO_TOKEN</c> is set only when <c>--token</c> was given. A token the CLI obtains from
    /// client credentials never leaves it: the extension's calls obtain their own (the token cache
    /// makes that free).
    /// </para>
    /// </summary>
    /// <returns>Variable name to value.</returns>
    public IReadOnlyDictionary<string, string> ChildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Host is not null)
            env["UMBRACO_HOST"] = Host;
        if (Token is not null)
            env[GlobalOptions.TokenVariable] = Token;
        // Absolute, because the extension may run its calls from another working directory.
        if (Config is not null)
            env[GlobalOptions.ConfigVariable] = Path.GetFullPath(Config);
        if (Profile is not null)
            env["UMBRACO_PROFILE"] = Profile;
        if (Output is not null)
            env[GlobalOptions.OutputVariable] = Output.ToLowerInvariant();
        if (ReadOnly)
            env[GlobalOptions.ReadOnlyVariable] = "1";
        if (DryRun)
            env[GlobalOptions.DryRunVariable] = "1";
        return env;
    }
}

/// <summary>
/// A command line that names an extension command rather than a built-in one (ADR 0010):
/// <c>umbraco [options] &lt;noun&gt; [arguments]</c>, where no built-in noun is called
/// <c>&lt;noun&gt;</c>.
/// <para>
/// The context options (<see cref="GlobalOptions.Context"/>) are the CLI's wherever they appear
/// before a <c>--</c>, as they are on a built-in command, so <c>umbraco foo purge --dry-run</c>
/// previews the extension's writes whether or not the extension knows the option. They are taken
/// out of the arguments and reach the extension as its <see cref="ExtensionContext"/>. Every other
/// token, in order, is the extension's, including the other global options (<c>--yes</c>,
/// <c>--quiet</c>, <c>--verbose</c>, <c>--fields</c>) and <c>--help</c>. A <c>--</c> ends the
/// CLI's options: it and everything after it are passed as they are.
/// </para>
/// </summary>
/// <param name="Noun">The noun, e.g. <c>foo</c>, which runs <c>umbraco-foo</c>.</param>
/// <param name="Arguments">The arguments for the executable, in order.</param>
/// <param name="Context">The context options the line gave.</param>
/// <param name="Problem">
/// What is wrong with a context option (no value, an unknown <c>--output</c> format), or null.
/// Reported only once the noun is known to be an extension, so a built-in line is never judged
/// by these rules.
/// </param>
public sealed record ExtensionInvocation(
    string Noun,
    IReadOnlyList<string> Arguments,
    ExtensionContext Context,
    string? Problem = null
)
{
    /// <summary>
    /// Reads <paramref name="args"/> as an extension command line, or returns null when it is not
    /// one: the first word that is not a global option names a built-in noun, is not a noun at all
    /// (an option such as <c>--help</c>, or a word that is not lower-case kebab-case), or there is
    /// no such word. It does not look at PATH.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="root">The command tree, whose top-level nouns always win.</param>
    /// <param name="globals">The global options.</param>
    /// <returns>The invocation, or null when the line is not an extension command.</returns>
    public static ExtensionInvocation? From(
        IReadOnlyList<string> args,
        Command root,
        GlobalOptions globals
    )
    {
        var values = new Dictionary<Option, string?>();
        var arguments = new List<string>();
        string? noun = null;
        string? problem = null;

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (token == "--")
            {
                if (noun is null)
                    return null;
                arguments.AddRange(args.Skip(i));
                break;
            }

            if (Match(token, globals.Context) is (var option, var inline))
            {
                // A flag takes no separate value (a following "false" is the extension's, so the
                // flag errs on the side of the guardrail); an option takes the next token.
                var value = IsFlag(option)
                    ? inline ?? "true"
                    : inline ?? (i + 1 < args.Count ? args[++i] : null);
                if (value is null)
                    problem ??= $"{option.Name} needs a value.";
                values[option] = value;
                continue;
            }

            if (noun is null)
            {
                // The other global options may come before the noun too; they are passed on.
                if (Match(token, globals.All) is (var other, var otherInline))
                {
                    arguments.Add(token);
                    if (!IsFlag(other) && otherInline is null && i + 1 < args.Count)
                        arguments.Add(args[++i]);
                    continue;
                }
                if (!ExtensionLocator.IsNoun(token) || IsBuiltIn(root, token))
                    return null;
                noun = token;
                continue;
            }

            arguments.Add(token);
        }

        if (noun is null)
            return null;

        var output = Value(values, globals.Output);
        if (output is not null && OutputFormatParser.Parse(output) is null)
            problem ??=
                $"'{output}' is not a valid --output format: expected one of "
                + $"{string.Join(", ", GlobalOptions.OutputFormats)}.";

        var context = new ExtensionContext(
            Host: Value(values, globals.Host),
            Token: Value(values, globals.Token),
            Config: Value(values, globals.Config),
            Profile: Value(values, globals.Profile),
            Output: output,
            ReadOnly: Flag(values, globals.ReadOnly, ref problem),
            DryRun: Flag(values, globals.DryRun, ref problem)
        );
        return new ExtensionInvocation(noun, arguments, context, problem);
    }

    /// <summary>
    /// The option in <paramref name="options"/> that <paramref name="token"/> is, by name or alias,
    /// alone (<c>--host</c>) or with its value attached (<c>--host=x</c>, <c>--host:x</c>, the two
    /// forms the parser accepts).
    /// </summary>
    /// <param name="token">One argument.</param>
    /// <param name="options">The options to match.</param>
    /// <returns>The option and any attached value, or null when it is none of them.</returns>
    private static (Option Option, string? Inline)? Match(string token, IEnumerable<Option> options)
    {
        foreach (var option in options)
        foreach (var name in option.Aliases.Prepend(option.Name))
        {
            if (token == name)
                return (option, null);
            if (token.Length > name.Length && token.StartsWith(name, StringComparison.Ordinal))
                if (token[name.Length] is '=' or ':')
                    return (option, token[(name.Length + 1)..]);
        }
        return null;
    }

    /// <summary>Whether an option is an on/off switch rather than one that takes a value.</summary>
    private static bool IsFlag(Option option) => option.ValueType == typeof(bool);

    /// <summary>Whether <paramref name="word"/> is a top-level noun of the tree, by name or alias.</summary>
    private static bool IsBuiltIn(Command root, string word) =>
        root.Subcommands.Any(c => c.Name == word || c.Aliases.Contains(word));

    /// <summary>The value given for <paramref name="option"/>, or null when it was not given.</summary>
    private static string? Value(Dictionary<Option, string?> values, Option option) =>
        values.GetValueOrDefault(option);

    /// <summary>
    /// Whether a flag is on: given alone, or with an attached <c>true</c>. An attached value that
    /// is not a boolean is a problem, and leaves the flag on, the safer reading for a guardrail.
    /// </summary>
    private static bool Flag(Dictionary<Option, string?> values, Option flag, ref string? problem)
    {
        if (!values.TryGetValue(flag, out var raw))
            return false;
        if (bool.TryParse(raw, out var on))
            return on;
        problem ??= $"'{raw}' is not valid for {flag.Name}: expected true or false.";
        return true;
    }
}
