using System.CommandLine;
using System.Net.Http.Headers;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

/// <summary>
/// Wires <c>auth doctor</c> (issue #66): a first-run diagnostic that checks host resolution,
/// connectivity/TLS, credentials, authentication, the resolved identity, and the instance
/// version — each with a clear pass/fail and a remediation hint. Unlike normal commands it is
/// tolerant of failure: a failed check is reported as data, never a crash or an early abort, so
/// the whole picture is shown in one run.
/// </summary>
public static class AuthDoctorCommand
{
    /// <summary>One diagnostic check's outcome, rendered as a row / JSON object.</summary>
    /// <param name="Check">The check name.</param>
    /// <param name="Status">One of <c>pass</c>, <c>fail</c>, <c>warn</c>, <c>skip</c>.</param>
    /// <param name="Detail">A human detail or remediation hint.</param>
    public sealed record DoctorCheck(string Check, string Status, string Detail);

    /// <summary>Builds the <c>auth doctor</c> command.</summary>
    /// <param name="global">The recursive global options (host/token/output/config/profile).</param>
    /// <param name="configStore">The config store (default path / env).</param>
    /// <param name="authService">The client-credentials token service.</param>
    /// <param name="httpClientFactory">Factory for the diagnostic HTTP probes.</param>
    /// <param name="clientFactory">Factory for the typed client (identity check).</param>
    /// <returns>The configured command.</returns>
    public static Command Build(
        GlobalOptions global,
        ConfigStore configStore,
        UmbracoAuthService authService,
        IHttpClientFactory httpClientFactory,
        IUmbracoManagementClientFactory clientFactory
    )
    {
        var cmd = new Command(
            "doctor",
            "Diagnose connectivity, TLS, credentials, authentication, and the target instance version.\n\nExamples:\n  umbraco auth doctor\n  umbraco auth doctor --output json"
        );

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var writer = OutputWriterFactory.Create(
                    OutputFormatParser.Parse(parseResult.GetValue(global.Output))
                );
                var store = ConfigStore.Resolve(parseResult.GetValue(global.Config), configStore);
                var config = store.Load(parseResult.GetValue(global.Profile));
                var host = parseResult.GetValue(global.Host) ?? config.Host;
                var tokenOverride = parseResult.GetValue(global.Token);

                var checks = await RunChecksAsync(
                    host,
                    tokenOverride,
                    config,
                    httpClientFactory,
                    authService,
                    clientFactory,
                    ct
                );

                writer.WriteTable(
                    ["Check", "Status", "Detail"],
                    checks.Select(c => new[] { c.Check, c.Status, c.Detail }),
                    CommandPath.Of(parseResult)
                );

                // Exit 1 when any check hard-failed so a script/agent can gate on it; warnings
                // (best-effort checks like version) do not fail the run.
                return checks.Any(c => c.Status == "fail") ? 1 : 0;
            }
        );

        return cmd;
    }

    /// <summary>
    /// Runs the diagnostic checks in order, short-circuiting only when a prerequisite is missing
    /// (no host means nothing else can run). Never throws for an expected failure — each failure
    /// becomes a <see cref="DoctorCheck"/>. Extracted from the command action so it is testable.
    /// </summary>
    /// <param name="host">The resolved host, or null/empty when none is configured.</param>
    /// <param name="tokenOverride">A raw bearer token from <c>--token</c>, or null.</param>
    /// <param name="config">The resolved credentials (client id/secret).</param>
    /// <param name="httpClientFactory">Factory for the diagnostic HTTP probes.</param>
    /// <param name="authService">The client-credentials token service.</param>
    /// <param name="clientFactory">Factory for the typed client (identity check).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ordered check results.</returns>
    public static async Task<IReadOnlyList<DoctorCheck>> RunChecksAsync(
        string? host,
        string? tokenOverride,
        CliConfig config,
        IHttpClientFactory httpClientFactory,
        UmbracoAuthService authService,
        IUmbracoManagementClientFactory clientFactory,
        CancellationToken ct
    )
    {
        var checks = new List<DoctorCheck>();

        // 1. Host — a prerequisite for everything else.
        if (string.IsNullOrEmpty(host))
        {
            checks.Add(
                new DoctorCheck(
                    "Host configured",
                    "fail",
                    "No host. Run 'umbraco auth login --host <url>' or set UMBRACO_HOST."
                )
            );
            return checks;
        }
        // A scheme-less or malformed host (e.g. "localhost:44300" without https://) is the exact
        // misconfiguration doctor should diagnose — validate it here so the later HTTP probes,
        // which only guard HttpRequestException, don't crash with a raw NotSupportedException /
        // InvalidOperationException from HttpClient.GetAsync.
        if (
            !Uri.TryCreate(host, UriKind.Absolute, out var hostUri)
            || (hostUri.Scheme != Uri.UriSchemeHttp && hostUri.Scheme != Uri.UriSchemeHttps)
        )
        {
            checks.Add(
                new DoctorCheck(
                    "Host configured",
                    "fail",
                    $"'{host}' is not an absolute http(s) URL. Include the scheme, e.g. https://{host}."
                )
            );
            return checks;
        }
        checks.Add(new DoctorCheck("Host configured", "pass", host));

        // 2. Credentials present.
        var hasToken = !string.IsNullOrEmpty(tokenOverride);
        var hasCreds =
            !string.IsNullOrEmpty(config.ClientId) && !string.IsNullOrEmpty(config.ClientSecret);
        checks.Add(
            (hasToken, hasCreds) switch
            {
                (true, _) => new DoctorCheck("Credentials present", "pass", "Using --token."),
                (false, true) => new DoctorCheck(
                    "Credentials present",
                    "pass",
                    "Client id/secret found."
                ),
                _ => new DoctorCheck(
                    "Credentials present",
                    "fail",
                    "No credentials. Run 'umbraco auth login' or set UMBRACO_CLIENT_ID / "
                        + "UMBRACO_CLIENT_SECRET (or pass --token)."
                ),
            }
        );

        // 3. Connectivity / TLS — any HTTP response (even 401) proves we reached the instance.
        var infoUrl = host.TrimEnd('/') + "/umbraco/management/api/v1/server/information";
        try
        {
            using var probe = httpClientFactory.CreateClient();
            // Bound the probe so a black-holed/firewalled host reports quickly instead of
            // hanging on the default 100s HttpClient timeout.
            probe.Timeout = TimeSpan.FromSeconds(10);
            using var resp = await probe.GetAsync(infoUrl, ct);
            checks.Add(
                new DoctorCheck(
                    "Connectivity / TLS",
                    "pass",
                    $"Reachable (HTTP {(int)resp.StatusCode})."
                )
            );
        }
        catch (HttpRequestException ex)
        {
            var looksTls =
                ex.InnerException is System.Security.Authentication.AuthenticationException
                || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase);
            checks.Add(
                new DoctorCheck(
                    "Connectivity / TLS",
                    "fail",
                    looksTls
                        ? $"TLS/certificate error: {ex.Message} If this is a local dev instance, "
                            + "trust the dev cert: dotnet dev-certs https --trust."
                        : $"Could not reach {host}: {ex.Message}"
                )
            );
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            checks.Add(
                new DoctorCheck("Connectivity / TLS", "fail", "Timed out reaching the host.")
            );
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Defensive: any other failure (a malformed URL that slipped through, a DNS quirk)
            // becomes a reported check, never a raw crash — doctor must always produce output.
            checks.Add(
                new DoctorCheck("Connectivity / TLS", "fail", $"Probe failed: {ex.Message}")
            );
        }

        // 4. Authentication — obtain a token (unless one was supplied directly).
        string? bearer = tokenOverride;
        if (hasToken)
        {
            checks.Add(
                new DoctorCheck(
                    "Authentication",
                    "skip",
                    "Using --token; token exchange not performed."
                )
            );
        }
        else if (hasCreds)
        {
            try
            {
                // Fresh: a cached token would pass this check without exercising the exchange.
                bearer = await authService.GetTokenAsync(
                    host,
                    config.ClientId!,
                    config.ClientSecret!,
                    ct,
                    fresh: true
                );
                checks.Add(new DoctorCheck("Authentication", "pass", "Obtained an access token."));
            }
            catch (UmbracoAuthException ex)
            {
                checks.Add(
                    new DoctorCheck(
                        "Authentication",
                        "fail",
                        ex.StatusCode == 401
                            ? "Token request rejected (401). Check the client id/secret and that "
                                + "the API user is enabled."
                            : ex.Message
                    )
                );
            }
        }
        else
        {
            checks.Add(
                new DoctorCheck("Authentication", "skip", "No credentials to authenticate with.")
            );
        }

        // 5. Authenticated identity — resolve who the token belongs to.
        if (!string.IsNullOrEmpty(bearer))
        {
            try
            {
                using var http = httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                http.BaseAddress = new Uri(host.TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    bearer
                );
                var me = await clientFactory.Create(http).GetCurrentUserAsync(ct);
                checks.Add(
                    me.IsSuccess
                        ? new DoctorCheck(
                            "Authenticated identity",
                            "pass",
                            $"{me.Data!.Name} <{me.Data.Email}>".Trim()
                        )
                        : new DoctorCheck(
                            "Authenticated identity",
                            "fail",
                            me.StatusCode == 403
                                ? "The token has no access to user/current (403). Check the API "
                                    + "user's permissions."
                                : me.ErrorMessage ?? "Could not resolve the identity."
                        )
                );
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The client swallows transport errors into a failed response, but a malformed
                // 200 body (e.g. a proxy login page) can still throw — report it, don't crash.
                checks.Add(
                    new DoctorCheck(
                        "Authenticated identity",
                        "fail",
                        $"Could not resolve the identity: {ex.Message}"
                    )
                );
            }
        }
        else
        {
            checks.Add(new DoctorCheck("Authenticated identity", "skip", "No token available."));
        }

        // 6. Instance version — best-effort (warn, never fail the run).
        try
        {
            using var http = httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, infoUrl);
            if (!string.IsNullOrEmpty(bearer))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var version =
                    doc.RootElement.TryGetProperty("version", out var v)
                    && v.ValueKind == JsonValueKind.String
                        ? v.GetString()
                        : null;
                checks.Add(
                    new DoctorCheck(
                        "Instance version",
                        version is null ? "warn" : "pass",
                        version ?? "Version not reported by server/information."
                    )
                );
            }
            else
            {
                checks.Add(
                    new DoctorCheck(
                        "Instance version",
                        "warn",
                        $"server/information returned HTTP {(int)resp.StatusCode}."
                    )
                );
            }
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            checks.Add(
                new DoctorCheck("Instance version", "warn", $"Could not read version: {ex.Message}")
            );
        }

        return checks;
    }
}
