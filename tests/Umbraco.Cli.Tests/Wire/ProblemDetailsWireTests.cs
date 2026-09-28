using System.Net;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Umbraco's ProblemDetails reach the caller (#286): the message says what failed and what to fix,
/// and the body travels as the failure's details. Driven through <c>user invite</c>, a typed call
/// whose 500 the OpenAPI spec does not declare - the case where the body used to be thrown away.
/// </summary>
public class ProblemDetailsWireTests
{
    /// <summary>Invites a user against a server that answers every request with <paramref name="body"/>.</summary>
    /// <param name="status">The HTTP status to answer with.</param>
    /// <param name="body">The error body.</param>
    /// <returns>The client's response.</returns>
    private static Task<UmbracoResponse<Empty>> InviteAnswered(
        HttpStatusCode status,
        string body
    ) =>
        Wire.Client(new RoutingHandler().When(_ => true, status, body))
            .InviteUserAsync(
                new InviteUserRequest
                {
                    Email = "new@example.com",
                    Name = "New User",
                    UserGroupIds = [new ReferenceById { Id = Guid.NewGuid() }],
                },
                CancellationToken.None
            );

    private const string CannotInvite = """
        {"type":"Error","title":"Cannot send user invitation","status":500,"operationStatus":"CannotInvite"}
        """;

    [Fact]
    public async Task UndeclaredServerError_MessageCarriesTheTitleAndOperationStatus()
    {
        var result = await InviteAnswered(HttpStatusCode.InternalServerError, CannotInvite);

        Assert.StartsWith("Cannot send user invitation (CannotInvite).", result.ErrorMessage);
    }

    [Fact]
    public async Task CannotInvite_MessageHintsAtSmtp()
    {
        var result = await InviteAnswered(HttpStatusCode.InternalServerError, CannotInvite);

        Assert.Contains("configure SMTP", result.ErrorMessage);
    }

    [Fact]
    public async Task UndeclaredServerError_DetailsAreTheBodyAsSent()
    {
        var result = await InviteAnswered(HttpStatusCode.InternalServerError, CannotInvite);

        Assert.Equal(
            """{"type":"Error","title":"Cannot send user invitation","status":500,"operationStatus":"CannotInvite"}""",
            result.Details?.ToJsonString()
        );
    }

    [Fact]
    public async Task InvalidProperties_AreNamedInTheMessage()
    {
        var result = await InviteAnswered(
            HttpStatusCode.BadRequest,
            """
            {"title":"Invalid document","detail":"The specified document had an invalid configuration.",
             "status":400,"operationStatus":"ContentInvalid","invalidProperties":["author","excerpt"]}
            """
        );

        Assert.Equal(
            "Invalid document (ContentInvalid): The specified document had an invalid configuration. "
                + "Invalid properties: author, excerpt.",
            result.ErrorMessage
        );
    }

    [Fact]
    public async Task StackTraceDetail_IsKeptOutOfTheMessage()
    {
        // The raw-path 500 from the round-3 test: the exception message is the title and the
        // trace is the detail, and the trace used to be the whole message.
        var result = await InviteAnswered(
            HttpStatusCode.InternalServerError,
            """
            {"title":"SQLite Error 19: 'UNIQUE constraint failed: cmsPropertyTypeGroup.uniqueID'.",
             "status":500,
             "detail":"   at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC(Int32 rc, sqlite3 db)\n   at Microsoft.Data.Sqlite.SqliteDataReader.NextResult()"}
            """
        );

        Assert.Equal(
            "SQLite Error 19: 'UNIQUE constraint failed: cmsPropertyTypeGroup.uniqueID'.",
            result.ErrorMessage
        );
    }

    [Fact]
    public async Task StackTraceDetail_StaysInTheDetails()
    {
        var result = await InviteAnswered(
            HttpStatusCode.InternalServerError,
            """{"title":"Boom","status":500,"detail":"   at Some.Frame()"}"""
        );

        Assert.Equal("   at Some.Frame()", result.Details?["detail"]?.GetValue<string>());
    }

    [Fact]
    public async Task DetailOnly_LeadsWithTheDetail()
    {
        var result = await InviteAnswered(
            HttpStatusCode.NotFound,
            """{"status":404,"detail":"The user group could not be found"}"""
        );

        Assert.Equal("The user group could not be found.", result.ErrorMessage);
    }

    [Fact]
    public async Task ServerErrorWithoutABody_HasNoDetails()
    {
        var result = await InviteAnswered(HttpStatusCode.InternalServerError, "");

        Assert.Null(result.Details);
    }

    [Fact]
    public async Task UnreadableErrorBody_IsStillAFailureWithItsStatus()
    {
        // A proxy in front of Umbraco can answer with an HTML page; reading it as ProblemDetails
        // must not turn a 502 into a crash. The CLI's pipeline drops such a body first.
        var routing = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.BadGateway,
            "<html>Bad gateway</html>"
        );
        var http = new HttpClient(new UnreadableErrorBodyHandler { InnerHandler = routing })
        {
            BaseAddress = new Uri("https://example.com/"),
        };

        var result = await new UmbracoManagementClient(http).InviteUserAsync(
            new InviteUserRequest { Email = "new@example.com", Name = "New User" },
            CancellationToken.None
        );

        Assert.Equal((false, 502), (result.IsSuccess, result.StatusCode));
    }

    [Fact]
    public async Task ReadableErrorBody_PassesThroughTheHandler()
    {
        var routing = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.InternalServerError,
            CannotInvite
        );
        var http = new HttpClient(new UnreadableErrorBodyHandler { InnerHandler = routing })
        {
            BaseAddress = new Uri("https://example.com/"),
        };

        var result = await new UmbracoManagementClient(http).InviteUserAsync(
            new InviteUserRequest { Email = "new@example.com", Name = "New User" },
            CancellationToken.None
        );

        Assert.Equal("CannotInvite", result.Details?["operationStatus"]?.GetValue<string>());
    }
}
