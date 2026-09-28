using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

/// <summary>Tests for the <c>auth doctor</c> diagnostic check logic (issue #66).</summary>
public class AuthDoctorTests
{
    private sealed class StubHandler(System.Net.HttpStatusCode status, string body)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    [Fact]
    public async Task RunChecksAsync_NoHost_FailsFastWithSingleHostCheck()
    {
        // #66: with no host resolved, doctor reports a single failing host check and does not
        // attempt connectivity/auth (nothing else can run).
        var factory = new SingleClientFactory(new StubHandler(System.Net.HttpStatusCode.OK, "{}"));

        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: null,
            tokenOverride: null,
            config: new CliConfig(),
            httpClientFactory: factory,
            authService: new UmbracoAuthService(factory),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        var only = Assert.Single(checks);
        Assert.Equal("Host configured", only.Check);
        Assert.Equal("fail", only.Status);
    }

    [Fact]
    public async Task RunChecksAsync_SchemeLessHost_DiagnosesInsteadOfCrashing()
    {
        // #66 review fix: a scheme-less host ("localhost:44300") is exactly the misconfiguration
        // doctor exists to catch — it must fail the host check cleanly, not crash the HTTP probe.
        var factory = new SingleClientFactory(new StubHandler(System.Net.HttpStatusCode.OK, "{}"));

        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: "localhost:44300",
            tokenOverride: null,
            config: new CliConfig(),
            httpClientFactory: factory,
            authService: new UmbracoAuthService(factory),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        var only = Assert.Single(checks);
        Assert.Equal("Host configured", only.Check);
        Assert.Equal("fail", only.Status);
        Assert.Contains("https://", only.Detail);
    }

    [Fact]
    public async Task RunChecksAsync_HostButNoCredentials_ReportsReachableAndSkipsAuth()
    {
        // #66: host set but no credentials — connectivity passes (stub 200), credentials fail,
        // auth/identity are skipped (no creds), and the version is read best-effort.
        var factory = new SingleClientFactory(
            new StubHandler(System.Net.HttpStatusCode.OK, """{"version":"17.3.5"}""")
        );

        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: "https://localhost:44300",
            tokenOverride: null,
            config: new CliConfig(),
            httpClientFactory: factory,
            authService: new UmbracoAuthService(factory),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        string Status(string check) => checks.Single(c => c.Check == check).Status;
        Assert.Equal("pass", Status("Host configured"));
        Assert.Equal("fail", Status("Credentials present"));
        Assert.Equal("pass", Status("Connectivity / TLS"));
        Assert.Equal("skip", Status("Authentication"));
        Assert.Equal("skip", Status("Authenticated identity"));
        Assert.Equal("pass", Status("Instance version")); // parsed "17.3.5"
        Assert.Equal("17.3.5", checks.Single(c => c.Check == "Instance version").Detail);
    }

    [Fact]
    public async Task RunChecksAsync_VersionInRange_PassesTheSupportedVersionCheck()
    {
        // #153: the version read in check 6 is compared with the tested range.
        var factory = new SingleClientFactory(
            new StubHandler(System.Net.HttpStatusCode.OK, """{"version":"17.3.5"}""")
        );

        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: "https://localhost:44300",
            tokenOverride: null,
            config: new CliConfig(),
            httpClientFactory: factory,
            authService: new UmbracoAuthService(factory),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        Assert.Equal("pass", checks.Single(c => c.Check == "Supported version").Status);
    }

    [Fact]
    public async Task RunChecksAsync_VersionOutOfRange_WarnsWithoutFailingTheRun()
    {
        var factory = new SingleClientFactory(
            new StubHandler(System.Net.HttpStatusCode.OK, """{"version":"99.0.0"}""")
        );

        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: "https://localhost:44300",
            tokenOverride: "tok",
            config: new CliConfig(),
            httpClientFactory: factory,
            authService: new UmbracoAuthService(factory),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        Assert.Equal("warn", checks.Single(c => c.Check == "Supported version").Status);
    }

    [Fact]
    public void SupportedVersionCheck_OutOfRange_NamesBothVersions()
    {
        var check = AuthDoctorCommand.SupportedVersionCheck("19.0.0");

        Assert.Equal(VersionSupport.OutOfRangeMessage("19.0.0"), check.Detail);
    }

    [Fact]
    public void SupportedVersionCheck_UnknownVersion_IsSkipped()
    {
        var check = AuthDoctorCommand.SupportedVersionCheck(null);

        Assert.Equal("skip", check.Status);
    }

    [Fact]
    public async Task RunChecksAsync_HostUnreachable_ReportsConnectivityFailure()
    {
        // #66: a transport failure on the connectivity probe is reported as a failing check with
        // a remediation hint, not an unhandled exception.
        var factory = new ThrowingClientFactory(new HttpRequestException("connection refused"));

        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: "https://localhost:44300",
            tokenOverride: null,
            config: new CliConfig(),
            httpClientFactory: factory,
            authService: new UmbracoAuthService(factory),
            clientFactory: new UmbracoManagementClientFactory(),
            ct: CancellationToken.None
        );

        Assert.Equal("fail", checks.Single(c => c.Check == "Connectivity / TLS").Status);
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => throw ex;
    }

    private sealed class ThrowingClientFactory(Exception ex) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler(ex));
    }
}
