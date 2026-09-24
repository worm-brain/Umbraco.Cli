using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the member, member-type and member-group write paths put on the wire (#187 Phase 2).
/// <para>
/// This noun is where #184 was found, and <c>UpdateMemberAsync</c> is the same read-merge shape
/// that lost the template on documents (#178) - it reads the member, rebuilds the body, and PUTs
/// it back, so any field it forgets to carry is deleted. Those are asserted here rather than by
/// substring.
/// </para>
/// </summary>
public class MembersWireTests
{
    /// <summary>Answers everything with 200 and an empty body.</summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Blank() =>
        new RoutingHandler().When(_ => true, HttpStatusCode.OK, "");

    /// <summary>
    /// A member as the API returns it: two groups, a custom property value, a username, and the
    /// lockout/2FA flags - all of which the update PUT has to carry back.
    /// </summary>
    /// <param name="id">The member id.</param>
    /// <returns>The JSON body.</returns>
    private static readonly Guid SubscribersGroup = Guid.Parse(
        "11111111-1111-1111-1111-111111111111"
    );

    /// <summary>The second group the stub member belongs to.</summary>
    private static readonly Guid EditorsGroup = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>A member as the API returns it.</summary>
    /// <param name="id">The member id.</param>
    /// <returns>The JSON body.</returns>
    private static string ExistingMember(Guid id) =>
        $$"""
            {
              "id": "{{id}}",
              "email": "a@example.com",
              "username": "a@example.com",
              "isApproved": true,
              "isLockedOut": false,
              "isTwoFactorEnabled": false,
              "groups": ["11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"],
              "values": [ { "alias": "company", "culture": null, "segment": null, "value": "Acme" } ],
              "variants": [ { "culture": null, "segment": null, "name": "Ann" } ]
            }
            """;

    // ── create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateMemberAsync_SendsEmailNameMemberTypeAndPassword()
    {
        var typeId = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler)
            .CreateMemberAsync(
                new CreateMemberRequest
                {
                    Email = "new@example.com",
                    Name = "New Member",
                    MemberType = new ContentTypeReference { Id = typeId },
                    Password = "s3cret-passphrase",
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/member");
        Assert.Equal("new@example.com", body["email"]!.GetValue<string>());
        Assert.Equal(typeId.ToString(), body["memberType"]!["id"]!.GetValue<string>());
        Assert.Equal("s3cret-passphrase", body["password"]!.GetValue<string>());
        // #136: an empty password made every create fail, so the field must actually carry one.
        Assert.NotEqual("", body["password"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateMemberAsync_CarriesTheNameOnAVariantNotOnlyAtTheTopLevel()
    {
        var handler = Blank();

        await Wire.Client(handler)
            .CreateMemberAsync(
                new CreateMemberRequest
                {
                    Email = "new@example.com",
                    Name = "New Member",
                    MemberType = new ContentTypeReference { Id = Guid.NewGuid() },
                    Password = "s3cret-passphrase",
                },
                CancellationToken.None
            );

        // #42: the display name lives on variants[], not as a top-level field.
        var variant = Assert.Single(
            handler.BodyOf(HttpMethod.Post, "/member")["variants"]!.AsArray()
        );
        Assert.Equal("New Member", variant!["name"]!.GetValue<string>());
    }

    // ── update: the read-merge that must not drop anything ────────────────────

    [Fact]
    public async Task UpdateMemberAsync_NameOnly_KeepsGroupsAndPropertyValues()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, ExistingMember(id));

        await Wire.Client(handler)
            .UpdateMemberAsync(
                id,
                new UpdateMemberRequest { Name = "Renamed" },
                CancellationToken.None
            );

        // The same failure mode as #178: this PUT replaces, so anything the client forgets to
        // re-send is deleted from the member.
        var body = handler.BodyOf(HttpMethod.Put, $"/member/{id}");
        // Member groups are referenced by id, not name.
        Assert.Equal(
            [SubscribersGroup.ToString(), EditorsGroup.ToString()],
            body["groups"]!.AsArray().Select(g => g!.GetValue<string>())
        );
        Assert.Equal(
            "Acme",
            Assert.Single(body["values"]!.AsArray())!["value"]!.GetValue<string>()
        );
        Assert.Equal("a@example.com", body["username"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateMemberAsync_NameOnly_KeepsTheCurrentEmail()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, ExistingMember(id));

        await Wire.Client(handler)
            .UpdateMemberAsync(
                id,
                new UpdateMemberRequest { Name = "Renamed" },
                CancellationToken.None
            );

        Assert.Equal(
            "a@example.com",
            handler.BodyOf(HttpMethod.Put, $"/member/{id}")["email"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task UpdateMemberAsync_RenamesTheVariantRatherThanAddingOne()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, ExistingMember(id));

        await Wire.Client(handler)
            .UpdateMemberAsync(
                id,
                new UpdateMemberRequest { Name = "Renamed" },
                CancellationToken.None
            );

        var variant = Assert.Single(
            handler.BodyOf(HttpMethod.Put, $"/member/{id}")["variants"]!.AsArray()
        );
        Assert.Equal("Renamed", variant!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateMemberAsync_ApprovedOnly_LeavesTheNameAlone()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, ExistingMember(id));

        await Wire.Client(handler)
            .UpdateMemberAsync(
                id,
                new UpdateMemberRequest { IsApproved = false },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Put, $"/member/{id}");
        Assert.False(body["isApproved"]!.GetValue<bool>());
        Assert.Equal(
            "Ann",
            Assert.Single(body["variants"]!.AsArray())!["name"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task DeleteMemberAsync_DeletesTheMember()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Delete, HttpStatusCode.OK, "")
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.NotFound, "");

        await Wire.Client(handler).DeleteMemberAsync(id, CancellationToken.None);

        handler.UriOf(HttpMethod.Delete, $"/member/{id}");
    }

    // ── member types ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateMemberTypeAsync_SendsNameAliasAndIcon()
    {
        var handler = Blank();

        await Wire.Client(handler)
            .CreateMemberTypeAsync(
                new CreateMemberTypeRequest
                {
                    Name = "Site Member",
                    Alias = "siteMember",
                    Icon = "icon-user",
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/member-type");
        Assert.Equal("Site Member", body["name"]!.GetValue<string>());
        Assert.Equal("siteMember", body["alias"]!.GetValue<string>());
        Assert.Equal("icon-user", body["icon"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateMemberTypeAsync_KeepsThePropertiesItDoesNotModel()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""
                {
                  "id": "{{id}}",
                  "name": "Site Member",
                  "alias": "siteMember",
                  "icon": "icon-user",
                  "properties": [ { "alias": "company", "name": "Company" } ],
                  "containers": [ { "name": "Details" } ]
                }
                """
            );

        await Wire.Client(handler)
            .UpdateMemberTypeAsync(
                id,
                new UpdateMemberTypeRequest { Name = "Renamed" },
                CancellationToken.None
            );

        // ADR 0005: the typed round-trip is lossy, so this goes through the raw scalar patch.
        // Properties and containers are the whole reason - losing them would gut the type.
        var body = handler.BodyOf(HttpMethod.Put, $"/member-type/{id}");
        Assert.Equal("Renamed", body["name"]!.GetValue<string>());
        Assert.Single(body["properties"]!.AsArray());
        Assert.Single(body["containers"]!.AsArray());
    }

    [Fact]
    public async Task DeleteMemberTypeAsync_DeletesTheType()
    {
        var id = Guid.NewGuid();
        var handler = Blank();

        await Wire.Client(handler).DeleteMemberTypeAsync(id, CancellationToken.None);

        handler.UriOf(HttpMethod.Delete, $"/member-type/{id}");
    }
}
