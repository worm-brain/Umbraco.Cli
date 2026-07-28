using System.Text.Json;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Contract tests (issue #52): assert that every Management API endpoint the CLI depends
/// on exists — by path template and HTTP verb — in the committed OpenAPI document
/// (<c>spec/management.json</c>). This catches endpoint drift when the spec is regenerated
/// against a newer Umbraco, without needing a live instance (that is the integration
/// harness, #51).
///
/// The set below is the CLI's API contract. It lists the hand-written
/// <see cref="System.Net.Http.HttpClient"/> endpoints (the real drift risk, since those
/// URLs are string literals nothing else checks) plus the key Kiota read endpoints for
/// documentation. Keep it in sync when the client's endpoints change. See
/// <c>docs/adr/0001-contract-test-approach.md</c>.
/// </summary>
public class ContractTests
{
    /// <summary>Lazily-loaded set of "METHOD /path" pairs declared by the spec.</summary>
    private static readonly Lazy<IReadOnlySet<string>> SpecOperations = new(LoadSpecOperations);

    /// <summary>
    /// The endpoints the CLI depends on, as "METHOD /path-template" strings that must match
    /// the OpenAPI document verbatim (path templates use the spec's <c>{param}</c> names).
    /// </summary>
    public static readonly string[] ClientEndpoints =
    [
        // ── Hand-written HttpClient paths (drift risk) ──────────────────────────
        "GET /umbraco/management/api/v1/data-type/{id}",
        "POST /umbraco/management/api/v1/data-type",
        "PUT /umbraco/management/api/v1/data-type/{id}",
        "DELETE /umbraco/management/api/v1/data-type/{id}",
        "GET /umbraco/management/api/v1/dictionary",
        "POST /umbraco/management/api/v1/dictionary",
        "GET /umbraco/management/api/v1/dictionary/{id}",
        "DELETE /umbraco/management/api/v1/dictionary/{id}",
        "POST /umbraco/management/api/v1/document",
        "PUT /umbraco/management/api/v1/document/{id}",
        "DELETE /umbraco/management/api/v1/document/{id}",
        "PUT /umbraco/management/api/v1/document/{id}/publish",
        "PUT /umbraco/management/api/v1/document/{id}/unpublish",
        "GET /umbraco/management/api/v1/document-version",
        "POST /umbraco/management/api/v1/document-version/{id}/rollback",
        "PUT /umbraco/management/api/v1/document/{id}/move-to-recycle-bin",
        "PUT /umbraco/management/api/v1/document/{id}/move",
        "POST /umbraco/management/api/v1/document/{id}/copy",
        "PUT /umbraco/management/api/v1/document/{id}/publish-with-descendants",
        "DELETE /umbraco/management/api/v1/recycle-bin/document",
        "PUT /umbraco/management/api/v1/recycle-bin/document/{id}/restore",
        "GET /umbraco/management/api/v1/document-type/{id}",
        "POST /umbraco/management/api/v1/document-type",
        "DELETE /umbraco/management/api/v1/document-type/{id}",
        "POST /umbraco/management/api/v1/media",
        "PUT /umbraco/management/api/v1/media/{id}/move-to-recycle-bin",
        "PUT /umbraco/management/api/v1/media/{id}/move",
        "DELETE /umbraco/management/api/v1/recycle-bin/media",
        "PUT /umbraco/management/api/v1/recycle-bin/media/{id}/restore",
        "POST /umbraco/management/api/v1/temporary-file",
        "GET /umbraco/management/api/v1/item/media-type/search",
        "DELETE /umbraco/management/api/v1/media/{id}",
        "POST /umbraco/management/api/v1/media-type",
        "GET /umbraco/management/api/v1/media-type/{id}",
        "DELETE /umbraco/management/api/v1/media-type/{id}",
        "GET /umbraco/management/api/v1/tree/media-type/root",
        "POST /umbraco/management/api/v1/language",
        "GET /umbraco/management/api/v1/language/{isoCode}",
        "PUT /umbraco/management/api/v1/language/{isoCode}",
        "DELETE /umbraco/management/api/v1/language/{isoCode}",
        "POST /umbraco/management/api/v1/member",
        "PUT /umbraco/management/api/v1/member/{id}",
        "DELETE /umbraco/management/api/v1/member/{id}",
        "POST /umbraco/management/api/v1/member-type",
        "GET /umbraco/management/api/v1/member-type/{id}",
        "DELETE /umbraco/management/api/v1/member-type/{id}",
        "GET /umbraco/management/api/v1/tree/member-type/root",
        "GET /umbraco/management/api/v1/user",
        "GET /umbraco/management/api/v1/user/{id}",
        "POST /umbraco/management/api/v1/user/invite",
        "GET /umbraco/management/api/v1/webhook",
        "POST /umbraco/management/api/v1/webhook",
        "DELETE /umbraco/management/api/v1/webhook/{id}",
        // ── Key Kiota read endpoints (compiler-guarded, listed for documentation) ─
        "GET /umbraco/management/api/v1/user/current",
        "GET /umbraco/management/api/v1/language",
        "GET /umbraco/management/api/v1/tree/document/root",
        "GET /umbraco/management/api/v1/tree/document/children",
        "GET /umbraco/management/api/v1/tree/media/root",
        "GET /umbraco/management/api/v1/tree/media/children",
        "GET /umbraco/management/api/v1/tree/document-type/root",
        "GET /umbraco/management/api/v1/tree/data-type/root",
        "GET /umbraco/management/api/v1/tree/template/root",
        "GET /umbraco/management/api/v1/filter/member",
        "GET /umbraco/management/api/v1/document/{id}",
        "GET /umbraco/management/api/v1/media/{id}",
        "GET /umbraco/management/api/v1/member/{id}",
        "GET /umbraco/management/api/v1/template/{id}",
        "POST /umbraco/management/api/v1/template",
        "PUT /umbraco/management/api/v1/template/{id}",
        "DELETE /umbraco/management/api/v1/template/{id}",
        "GET /umbraco/management/api/v1/item/template/search",
    ];

    /// <summary>xUnit member-data source: one row per contract endpoint.</summary>
    /// <returns>Each endpoint wrapped as a single theory argument.</returns>
    public static IEnumerable<object[]> Endpoints() =>
        ClientEndpoints.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void ClientEndpoint_ExistsInSpec(string operation)
    {
        Assert.True(
            SpecOperations.Value.Contains(operation),
            $"Endpoint '{operation}' is not present in spec/management.json. Either the client "
                + "calls an endpoint the target Umbraco no longer exposes (drift), or the contract "
                + "list in ContractTests is out of date."
        );
    }

    [Fact]
    public void Spec_LoadsAndHasOperations()
    {
        // Sanity: guards against a mis-located or empty spec silently passing the theory.
        Assert.NotEmpty(SpecOperations.Value);
    }

    /// <summary>
    /// Loads <c>spec/management.json</c> and flattens it into a set of "METHOD /path" strings.
    /// </summary>
    /// <returns>The set of operations declared by the spec.</returns>
    private static IReadOnlySet<string> LoadSpecOperations()
    {
        var specPath = FindSpec();
        using var doc = JsonDocument.Parse(File.ReadAllText(specPath));
        var ops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var verb in path.Value.EnumerateObject())
            ops.Add($"{verb.Name.ToUpperInvariant()} {path.Name}");

        return ops;
    }

    /// <summary>
    /// Locates <c>spec/management.json</c> by walking up from the test assembly's directory
    /// to the repo root (works regardless of the bin/Debug depth).
    /// </summary>
    /// <returns>The absolute path to the spec file.</returns>
    /// <exception cref="FileNotFoundException">If the spec cannot be found in any ancestor.</exception>
    private static string FindSpec()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "spec", "management.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate spec/management.json by walking up from " + AppContext.BaseDirectory
        );
    }
}
