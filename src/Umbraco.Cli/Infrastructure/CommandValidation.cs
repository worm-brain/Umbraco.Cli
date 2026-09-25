using System.CommandLine;
using System.CommandLine.Parsing;

namespace Umbraco.Cli.Infrastructure;

/// <summary>Helpers for cross-option validators and for options built by factories.</summary>
public static class CommandValidation
{
    /// <summary>
    /// Reads an option inside a validator, or reports false when the value did not parse.
    /// System.CommandLine converts a value lazily, when it is first read, so reading one that is
    /// malformed throws; its own parse error is already reported, so a cross-option rule should
    /// simply not apply. Only the read is guarded - an error in the rule itself still surfaces.
    /// </summary>
    /// <typeparam name="T">The option value type.</typeparam>
    /// <param name="result">The command result the validator is given.</param>
    /// <param name="option">The option to read.</param>
    /// <param name="value">The value, when it parsed.</param>
    /// <returns>True when the value parsed.</returns>
    public static bool TryGetValue<T>(this CommandResult result, Option<T> option, out T? value)
    {
        try
        {
            value = result.GetValue(option);
            return true;
        }
        catch (InvalidOperationException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>The <see cref="TryGetValue{T}(CommandResult, Option{T}, out T)"/> for an argument.</summary>
    /// <typeparam name="T">The argument value type.</typeparam>
    /// <param name="result">The command result the validator is given.</param>
    /// <param name="argument">The argument to read.</param>
    /// <param name="value">The value, when it parsed.</param>
    /// <returns>True when the value parsed.</returns>
    public static bool TryGetValue<T>(this CommandResult result, Argument<T> argument, out T? value)
    {
        try
        {
            value = result.GetValue(argument);
            return true;
        }
        catch (InvalidOperationException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Marks an option required, for options built by a factory rather than an initializer.</summary>
    /// <typeparam name="TOption">The option type, kept so a subclass keeps its members.</typeparam>
    /// <param name="option">The option.</param>
    /// <returns>The same option.</returns>
    public static TOption AsRequired<TOption>(this TOption option)
        where TOption : Option
    {
        option.Required = true;
        return option;
    }
}
