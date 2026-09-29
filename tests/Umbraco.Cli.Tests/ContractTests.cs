namespace Umbraco.Cli.Tests;

/// <summary>
/// Contract tests (#52, #76): every Management API request the client sends must be an operation
/// in the committed OpenAPI document (<c>spec/management.json</c>), by HTTP method and path. This
/// catches endpoint drift when the spec is regenerated against a newer Umbraco, without a live
/// instance.
/// <para>
/// The check is not a list kept here. The test HTTP handlers (<see cref="RoutingHandler"/> and the
/// client tests' own stubs) run <see cref="ManagementSpec.AssertDeclared"/> on every request they
/// answer, so the contract is whatever the client actually puts on the wire in its tests. That
/// covers the raw-JSON paths - URLs built as strings, which the compiler cannot check - and needs
/// no upkeep when one is added. The Kiota request builders are generated from the same spec, so
/// the compiler already guards them. These tests pin the matcher itself. See
/// <c>docs/adr/0001-contract-test-approach.md</c>.
/// </para>
/// </summary>
public class ContractTests
{
    private const string Api = "/umbraco/management/api/v1";

    /// <summary>Guards against a mis-located or empty spec letting every request through.</summary>
    [Fact]
    public void Spec_LoadsAndHasOperations()
    {
        Assert.NotEqual(0, ManagementSpec.OperationCount);
    }

    /// <summary>A GUID fills an <c>{id}</c> segment.</summary>
    [Fact]
    public void Declares_IdTemplateWithAGuid_IsTrue()
    {
        var declared = ManagementSpec.Declares(HttpMethod.Put, $"{Api}/document/{Guid.NewGuid()}");

        Assert.True(declared);
    }

    /// <summary>A named parameter such as <c>{isoCode}</c> takes any segment.</summary>
    [Fact]
    public void Declares_NamedParameterWithAnyValue_IsTrue()
    {
        var declared = ManagementSpec.Declares(HttpMethod.Get, $"{Api}/language/en-US");

        Assert.True(declared);
    }

    /// <summary>
    /// The OAuth token endpoint is declared by the security scheme's <c>tokenUrl</c>, not under
    /// <c>paths</c>, and a command run on client credentials sends to it.
    /// </summary>
    [Fact]
    public void Declares_TokenUrlOfTheSecurityScheme_IsTrue()
    {
        var declared = ManagementSpec.Declares(
            HttpMethod.Post,
            $"{Api}/security/back-office/token"
        );

        Assert.True(declared);
    }

    /// <summary>A literal that is not a GUID does not pass for <c>{id}</c>, so a typo is caught.</summary>
    [Fact]
    public void Declares_MisspeltLiteralInAnIdPosition_IsFalse()
    {
        var declared = ManagementSpec.Declares(HttpMethod.Get, $"{Api}/document/urlz");

        Assert.False(declared);
    }

    /// <summary>The path must be declared for the method used.</summary>
    [Fact]
    public void Declares_UndeclaredMethodOnARealPath_IsFalse()
    {
        var declared = ManagementSpec.Declares(
            HttpMethod.Patch,
            $"{Api}/document/{Guid.NewGuid()}"
        );

        Assert.False(declared);
    }

    /// <summary>The test handlers fail a test whose client sends an undeclared request.</summary>
    [Fact]
    public async Task RoutingHandler_UndeclaredManagementApiRequest_Throws()
    {
        // Arrange
        using var http = new HttpClient(Wire.Blank()) { BaseAddress = new Uri("https://x/") };

        // Act
        var send = () => http.GetAsync("umbraco/management/api/v1/no-such-endpoint");

        // Assert
        await Assert.ThrowsAsync<WireAssertionException>(send);
    }

    /// <summary>Requests outside the Management API are not the spec's to judge.</summary>
    [Fact]
    public async Task RoutingHandler_RequestOutsideTheManagementApi_IsAnswered()
    {
        // Arrange
        using var http = new HttpClient(Wire.Blank()) { BaseAddress = new Uri("https://x/") };

        // Act
        var response = await http.GetAsync("some/other/path");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }
}
