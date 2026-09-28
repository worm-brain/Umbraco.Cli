using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The one redaction policy shared by <c>-v</c> and <c>--dry-run</c> (#349, #352, #373).
/// </summary>
public class SecretRedactorTests
{
    private static readonly MediaTypeHeaderValue JsonType = new("application/json");

    /// <summary>Redacts a JSON body and parses the result, for asserting on single values.</summary>
    /// <param name="json">The body.</param>
    /// <returns>The redacted body, parsed.</returns>
    private static JsonNode Redact(string json) =>
        JsonNode.Parse(SecretRedactor.RedactBody(json, JsonType))!;

    [Theory]
    [InlineData("password")]
    [InlineData("newPassword")]
    [InlineData("clientSecret")]
    [InlineData("access_token")]
    [InlineData("apiKey")]
    [InlineData("api-key")]
    [InlineData("X-Api-Key")]
    [InlineData("Authorization")]
    [InlineData("auth")]
    [InlineData("Cookie")]
    [InlineData("credentials")]
    [InlineData("privateKey")]
    [InlineData("passphrase")]
    [InlineData("pwd")]
    [InlineData("connectionString")]
    [InlineData("sessionId")]
    public void IsSecretName_CredentialName_ReturnsTrue(string name)
    {
        Assert.True(SecretRedactor.IsSecretName(name));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("author")]
    [InlineData("email")]
    [InlineData("url")]
    public void IsSecretName_OrdinaryName_ReturnsFalse(string name)
    {
        Assert.False(SecretRedactor.IsSecretName(name));
    }

    [Fact]
    public void RedactBody_WebhookAuthorizationHeader_IsReplaced()
    {
        // #349: the round-5 repro.
        var result = Redact("""{"headers":{"Authorization":"Bearer r5d-secret"}}""");

        Assert.Equal("[redacted]", result["headers"]!["Authorization"]!.GetValue<string>());
    }

    [Fact]
    public void RedactBody_WebhookCustomHeader_IsReplaced()
    {
        // Webhook headers exist to carry credentials, whatever they are called.
        var result = Redact("""{"headers":{"X-Custom":"custom-secret"}}""");

        Assert.Equal("[redacted]", result["headers"]!["X-Custom"]!.GetValue<string>());
    }

    [Fact]
    public void RedactBody_RequestHeadersText_HidesEachValue()
    {
        // #349: a webhook log's requestHeaders is header text, not an object.
        var result = Redact(
            """{"requestHeaders":"Authorization: Bearer abc123\r\nX-Api-Key: k1"}"""
        );

        Assert.Equal(
            "Authorization: [redacted]\r\nX-Api-Key: [redacted]",
            result["requestHeaders"]!.GetValue<string>()
        );
    }

    [Fact]
    public void RedactBody_AuthorizationProperty_IsReplaced()
    {
        var result = Redact("""{"Authorization":"Bearer abc"}""");

        Assert.Equal("[redacted]", result["Authorization"]!.GetValue<string>());
    }

    [Fact]
    public void RedactBody_SecretAliasValuePair_HidesTheValue()
    {
        // #349: property values and data type configuration are {alias, value} pairs.
        var result = Redact("""{"values":[{"alias":"apiKey","value":"k-123"}]}""");

        Assert.Equal("[redacted]", result["values"]![0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void RedactBody_OrdinaryAliasValuePair_KeepsTheValue()
    {
        var result = Redact("""{"values":[{"alias":"title","value":"Hello"}]}""");

        Assert.Equal("Hello", result["values"]![0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void RedactBody_NumberUnderSecretName_IsKept()
    {
        // #373: failedPasswordAttempts is lockout debug info, not a secret.
        var result = Redact("""{"failedPasswordAttempts":3}""");

        Assert.Equal(3, result["failedPasswordAttempts"]!.GetValue<int>());
    }

    [Fact]
    public void RedactBody_DateUnderSecretName_IsKept()
    {
        var result = Redact("""{"lastPasswordChangeDate":"2026-09-28T10:11:12.123+00:00"}""");

        Assert.Equal(
            "2026-09-28T10:11:12.123+00:00",
            result["lastPasswordChangeDate"]!.GetValue<string>()
        );
    }

    [Fact]
    public void RedactBody_BooleanUnderSecretName_IsKept()
    {
        var result = Redact("""{"hasSecret":true}""");

        Assert.True(result["hasSecret"]!.GetValue<bool>());
    }

    [Fact]
    public void RedactBody_ObjectUnderSecretName_HidesItsStrings()
    {
        var result = Redact("""{"credentials":{"user":"u1","pass":"p1"}}""");

        Assert.Equal("[redacted]", result["credentials"]!["pass"]!.GetValue<string>());
    }

    [Fact]
    public void RedactBody_UrlWithUserInfo_HidesTheUserInfo()
    {
        var result = Redact("""{"url":"https://bob:hunter2@example.com/hook"}""");

        Assert.Equal("https://[redacted]@example.com/hook", result["url"]!.GetValue<string>());
    }

    [Fact]
    public void RedactBody_NothingToHide_ReturnsTheTextUnchanged()
    {
        const string body = """{ "name": "Hook" }""";

        Assert.Equal(body, SecretRedactor.RedactBody(body, JsonType));
    }

    [Fact]
    public void RedactBody_InvalidJson_ReturnsTextUnchanged()
    {
        Assert.Equal("{not json", SecretRedactor.RedactBody("{not json", JsonType));
    }

    [Fact]
    public void RedactBody_FormClientSecret_IsReplaced()
    {
        var result = SecretRedactor.RedactBody(
            "grant_type=client_credentials&client_id=cli&client_secret=s3cr3t",
            new("application/x-www-form-urlencoded")
        );

        Assert.Equal(
            "grant_type=client_credentials&client_id=cli&client_secret=[redacted]",
            result
        );
    }

    [Fact]
    public void RedactUrl_SecretQueryParameter_IsReplaced()
    {
        var result = SecretRedactor.RedactUrl("https://x/hook?id=1&api_key=k1");

        Assert.Equal("https://x/hook?id=1&api_key=[redacted]", result);
    }

    [Fact]
    public void RedactUrl_NotAUrl_IsReturnedUnchanged()
    {
        Assert.Equal("token=abc", SecretRedactor.RedactUrl("token=abc"));
    }

    [Fact]
    public void RedactHeader_Cookie_IsReplaced()
    {
        Assert.Equal("[redacted]", SecretRedactor.RedactHeader("Cookie", ["sess=1"]));
    }

    [Fact]
    public void RedactHeader_ContentType_IsKept()
    {
        Assert.Equal(
            "application/json",
            SecretRedactor.RedactHeader("Accept", ["application/json"])
        );
    }
}
