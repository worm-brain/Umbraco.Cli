using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The command-line side of <c>&lt;id|alias&gt;</c> (#250 Phase 3): an argument or option that
/// takes an item's id <i>or</i> its alias, name or key, and knows which kind of item it names, so
/// a command resolves it with one call and never has to say the kind twice. Typing it as
/// <see cref="Guid"/> is what made <c>templates delete blogPost</c> a parse error while
/// <c>templates get blogPost</c> worked.
/// </summary>
public static class Reference
{
    /// <summary>A positional argument naming one item of <paramref name="kind"/>.</summary>
    /// <param name="kind">What the argument names.</param>
    /// <param name="name">The argument name shown in help.</param>
    /// <returns>The argument.</returns>
    public static ReferenceArgument Argument(EntityKind kind, string name = "id") =>
        new(name, kind) { Description = $"The {kind.Noun()}'s id, or its {kind.KeyName()}." };

    /// <summary>An optional option naming one item of <paramref name="kind"/>.</summary>
    /// <param name="name">The option name, e.g. <c>--parent</c>.</param>
    /// <param name="kind">What the option names.</param>
    /// <param name="purpose">What the item is for, e.g. <c>Parent item; omit for the root</c>.</param>
    /// <param name="aliases">Other names for the option.</param>
    /// <returns>The option.</returns>
    public static ReferenceOption Option(
        string name,
        EntityKind kind,
        string purpose,
        params string[] aliases
    ) => new(name, kind, aliases) { Description = $"{purpose}: its id, or its {kind.KeyName()}." };
}

/// <summary>A positional <c>&lt;id|alias&gt;</c> argument; see <see cref="Reference"/>.</summary>
/// <param name="name">The argument name.</param>
/// <param name="kind">What the argument names.</param>
public sealed class ReferenceArgument(string name, EntityKind kind) : Argument<string>(name)
{
    /// <summary>What the argument names.</summary>
    public EntityKind Kind { get; } = kind;

    /// <summary>Resolves the parsed value, then runs <paramref name="call"/> with its id.</summary>
    /// <typeparam name="T">The call's payload type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="resolver">The resolver (the management client).</param>
    /// <param name="call">The id-taking call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The call's response, or the resolution failure.</returns>
    public Task<UmbracoResponse<T>> WithResolvedAsync<T>(
        ParseResult parseResult,
        IReferenceResolver resolver,
        Func<Guid, Task<UmbracoResponse<T>>> call,
        CancellationToken ct
    ) => resolver.WithResolvedAsync(Kind, parseResult.GetValue(this)!, call, ct);

    /// <summary>Resolves the parsed value to its id.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="resolver">The resolver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The id, or the resolution failure.</returns>
    public Task<UmbracoResponse<Guid>> ResolveAsync(
        ParseResult parseResult,
        IReferenceResolver resolver,
        CancellationToken ct
    ) => resolver.ResolveIdAsync(Kind, parseResult.GetValue(this)!, ct);
}

/// <summary>An optional <c>&lt;id|alias&gt;</c> option; see <see cref="Reference"/>.</summary>
/// <param name="name">The option name.</param>
/// <param name="kind">What the option names.</param>
/// <param name="aliases">Other names for the option.</param>
public sealed class ReferenceOption(string name, EntityKind kind, params string[] aliases)
    : Option<string?>(name, aliases)
{
    /// <summary>What the option names.</summary>
    public EntityKind Kind { get; } = kind;

    /// <summary>
    /// Resolves the parsed value when there is one (absent stays null), then runs
    /// <paramref name="call"/> with it.
    /// </summary>
    /// <typeparam name="T">The call's payload type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="resolver">The resolver.</param>
    /// <param name="call">The call, given the id or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The call's response, or the resolution failure.</returns>
    public Task<UmbracoResponse<T>> WithResolvedOptionalAsync<T>(
        ParseResult parseResult,
        IReferenceResolver resolver,
        Func<Guid?, Task<UmbracoResponse<T>>> call,
        CancellationToken ct
    ) => resolver.WithResolvedOptionalAsync(Kind, parseResult.GetValue(this), call, ct);

    /// <summary>Resolves the parsed value to its id, which must be given.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="resolver">The resolver.</param>
    /// <param name="call">The id-taking call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <typeparam name="T">The call's payload type.</typeparam>
    /// <returns>The call's response, or the resolution failure.</returns>
    public Task<UmbracoResponse<T>> WithResolvedAsync<T>(
        ParseResult parseResult,
        IReferenceResolver resolver,
        Func<Guid, Task<UmbracoResponse<T>>> call,
        CancellationToken ct
    ) => resolver.WithResolvedAsync(Kind, parseResult.GetValue(this)!, call, ct);
}
