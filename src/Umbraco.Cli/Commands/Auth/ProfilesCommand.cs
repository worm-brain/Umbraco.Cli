using System.CommandLine;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

/// <summary>
/// The <c>auth profile list</c> command (#64): lists the saved credential profiles and marks which
/// one is the default.
/// </summary>
public static class ProfilesCommand
{
    /// <summary>One saved profile, as structured output shows it (#283).</summary>
    /// <param name="Profile">The profile name.</param>
    /// <param name="Default">Whether commands without <c>--profile</c> use it.</param>
    public sealed record ProfileRow(string Profile, bool Default);

    /// <summary>Builds the <c>auth profile list</c> command.</summary>
    /// <param name="global">The global options (<c>--output</c>, <c>--config</c>, <c>--fields</c>...).</param>
    /// <param name="configStore">The fallback config store.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(GlobalOptions global, ConfigStore configStore)
    {
        var cmd = new Command(
            "list",
            "List saved credential profiles and which one is the default.\n\n"
                + "Examples:\n  umbraco auth profile list\n  umbraco auth profile list --output json"
        );

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var writer = global.CreateWriter(parseResult);
                var store = ConfigStore.Resolve(parseResult.GetValue(global.Config), configStore);
                var (names, defaultProfile) = store.ListProfiles();

                var rows = names
                    .Select(name => new ProfileRow(
                        name,
                        string.Equals(name, defaultProfile, StringComparison.OrdinalIgnoreCase)
                    ))
                    .ToList();

                // Structured output gets a real boolean; only the human table marks the
                // default with "*" (#283).
                writer.WriteList(
                    [.. rows.Cast<object>()],
                    ["Profile", "Default"],
                    rows.Select(r => new[] { r.Profile, r.Default ? "*" : "" }),
                    ListPaging.Complete(rows.Count),
                    CommandPath.Of(parseResult)
                );
                return Task.CompletedTask;
            }
        );

        return cmd;
    }
}
