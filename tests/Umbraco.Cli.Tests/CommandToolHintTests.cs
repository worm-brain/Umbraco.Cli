using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Commands.Extensions;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The <c>commandTool</c> discovery hint (#438, ADR 0010 on ADR 0009): a package declares the .NET
/// tool that adds its commands, the capabilities reader collects the declarations, and
/// <c>auth doctor</c> says which are installed and how to install the rest.
/// </summary>
public class CommandToolHintTests
{
    /// <summary>A manifest whose <c>umbracoCli</c> entry declares <paramref name="meta"/>.</summary>
    /// <param name="id">The package id.</param>
    /// <param name="meta">The declared meta, as JSON.</param>
    /// <returns>The manifest.</returns>
    private static ManifestResponse Declaring(string id, string meta) =>
        new()
        {
            Id = id,
            Name = id,
            CliCapabilities = JsonNode.Parse(meta)!.AsObject(),
        };

    /// <summary>A locator that finds exactly the executables named in <paramref name="installed"/>.</summary>
    /// <param name="installed">The executable file names on the made-up PATH.</param>
    /// <returns>The locator.</returns>
    private static ExtensionLocator PathWith(params string[] installed) =>
        new(Path.GetTempPath(), windows: false, p => installed.Contains(Path.GetFileName(p)));

    // ── SiteCapabilities.CommandTools ────────────────────────────────────────

    [Fact]
    public void From_CommandToolDeclared_CollectsNounAndPackage()
    {
        // Arrange
        ManifestResponse[] manifests =
        [
            Declaring(
                "Foo.Package",
                """{"commandTool":{"noun":"foo","package":"Umbraco.Foo.Cli"}}"""
            ),
        ];

        // Act
        var tools = SiteCapabilities.From(manifests).CommandTools;

        // Assert
        Assert.Equal(
            new DeclaredCommandTool("Foo.Package", "foo", "Umbraco.Foo.Cli"),
            Assert.Single(tools)
        );
    }

    [Fact]
    public void From_TwoPackagesDeclareTools_CollectsBoth()
    {
        // Arrange
        ManifestResponse[] manifests =
        [
            Declaring("A.Package", """{"commandTool":{"noun":"alpha","package":"A.Cli"}}"""),
            Declaring("B.Package", """{"commandTool":{"noun":"beta","package":"B.Cli"}}"""),
        ];

        // Act
        var tools = SiteCapabilities.From(manifests).CommandTools;

        // Assert
        Assert.Equal(["alpha", "beta"], tools.Select(t => t.Noun));
    }

    [Theory]
    [InlineData("""{"commandTool":"Umbraco.Foo.Cli"}""")] // not an object
    [InlineData("""{"commandTool":{"noun":"foo"}}""")] // no package
    [InlineData("""{"commandTool":{"package":"Umbraco.Foo.Cli"}}""")] // no noun
    [InlineData("""{"commandTool":{"noun":42,"package":"Umbraco.Foo.Cli"}}""")] // not a string
    [InlineData("""{"commandTool":{"noun":" ","package":"Umbraco.Foo.Cli"}}""")] // blank
    public void From_MalformedCommandTool_IsIgnored(string meta)
    {
        // Act
        var tools = SiteCapabilities.From([Declaring("Foo.Package", meta)]).CommandTools;

        // Assert
        Assert.Empty(tools);
    }

    [Fact]
    public void From_CommandToolDeclared_LeavesTheDictionaryFormatAlone()
    {
        // Arrange
        ManifestResponse[] manifests =
        [
            Declaring(
                "Foo.Package",
                """{"dictionaryValueFormat":"html","commandTool":{"noun":"foo","package":"Umbraco.Foo.Cli"}}"""
            ),
        ];

        // Act
        var capabilities = SiteCapabilities.From(manifests);

        // Assert
        Assert.Equal("html", capabilities.DictionaryValueFormat);
    }

    // ── auth doctor ──────────────────────────────────────────────────────────

    [Fact]
    public void ExtensionCommandChecks_NoneDeclared_IsOneSkip()
    {
        // Act
        var checks = AuthDoctorCommand.ExtensionCommandChecks([], PathWith());

        // Assert
        Assert.Equal(
            ("Extension commands", "skip"),
            (Assert.Single(checks).Check, checks[0].Status)
        );
    }

    [Fact]
    public void ExtensionCommandChecks_ToolInstalled_Passes()
    {
        // Arrange
        DeclaredCommandTool[] tools = [new("Foo.Package", "foo", "Umbraco.Foo.Cli")];

        // Act
        var check = Assert.Single(
            AuthDoctorCommand.ExtensionCommandChecks(tools, PathWith("umbraco-foo"))
        );

        // Assert
        Assert.Equal(("Extension command 'foo'", "pass"), (check.Check, check.Status));
    }

    [Fact]
    public void ExtensionCommandChecks_ToolMissing_WarnsWithTheInstallLine()
    {
        // Arrange
        DeclaredCommandTool[] tools = [new("Foo.Package", "foo", "Umbraco.Foo.Cli")];

        // Act
        var check = Assert.Single(AuthDoctorCommand.ExtensionCommandChecks(tools, PathWith()));

        // Assert
        Assert.Equal(
            (
                "warn",
                "Foo.Package adds 'umbraco foo'. Install it with: dotnet tool install -g Umbraco.Foo.Cli"
            ),
            (check.Status, check.Detail)
        );
    }

    [Fact]
    public void ExtensionCommandChecks_NounThatCannotNameACommand_IsIgnored()
    {
        // Arrange: upper case and a path separator are not a noun (ADR 0010).
        DeclaredCommandTool[] tools = [new("Odd.Package", "Foo/../bar", "Odd.Cli")];

        // Act
        var checks = AuthDoctorCommand.ExtensionCommandChecks(
            tools,
            PathWith("umbraco-Foo/../bar")
        );

        // Assert
        Assert.Equal("skip", Assert.Single(checks).Status);
    }

    /// <summary>Answers every request with <paramref name="body"/>.</summary>
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
    }

    /// <summary>Hands out clients over one handler.</summary>
    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Always returns the same fake client.</summary>
    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    [Fact]
    public async Task RunChecksAsync_SiteDeclaresAMissingTool_EndsWithAWarning()
    {
        // Arrange: the doctor's own probes get a 200; the typed client is a fake whose site
        // declares umbraco-foo, which the made-up PATH does not have.
        var http = new SingleClientFactory(new StubHandler("""{"version":"17.7.0"}"""));
        var fake = new FakeUmbracoManagementClient();
        fake.Manifests.Add(
            Declaring(
                "Foo.Package",
                """{"commandTool":{"noun":"foo","package":"Umbraco.Foo.Cli"}}"""
            )
        );

        // Act
        var checks = await AuthDoctorCommand.RunChecksAsync(
            host: "https://localhost:44300",
            tokenOverride: "t",
            config: new CliConfig(),
            httpClientFactory: http,
            authService: new UmbracoAuthService(http),
            clientFactory: new FakeClientFactory(fake),
            ct: CancellationToken.None,
            extensions: PathWith()
        );

        // Assert
        Assert.Equal(("Extension command 'foo'", "warn"), (checks[^1].Check, checks[^1].Status));
    }
}
