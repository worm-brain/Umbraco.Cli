using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Umbraco.Cli.Infrastructure.Http;

/// <summary>
/// The one redaction policy for everything the CLI echoes of an HTTP exchange: the <c>-v</c> log
/// (<see cref="VerboseHttpHandler"/>) and the <c>--dry-run</c> preview
/// (<see cref="MutationInterceptorHandler"/>), so the two can never disagree about what is a
/// secret (#349, #352).
/// <para>
/// The rules, applied to JSON bodies at any depth, form fields, request headers and URLs:
/// <list type="bullet">
/// <item>A name is secret when, lower-cased with <c>-</c>, <c>_</c> and other separators dropped,
/// it contains a credential word (<see cref="IsSecretName"/>) - so <c>X-Api-Key</c>,
/// <c>api_key</c> and <c>apiKey</c> all match.</item>
/// <item>Under a secret name only string values are hidden. Numbers, booleans and ISO dates stay,
/// so <c>failedPasswordAttempts</c> and <c>lastPasswordChangeDate</c> are still readable (#373).
/// An object or array under a secret name has every string inside it hidden.</item>
/// <item>Every value under a <c>headers</c> key (webhook headers, a log's <c>requestHeaders</c>
/// text) is hidden: those headers exist to carry credentials.</item>
/// <item>A <c>{"alias": "apiKey", "value": ...}</c> pair (property values, data type
/// configuration) is judged by its alias.</item>
/// <item>A URL keeps its shape but loses any <c>user:pass@</c> and any secret-named query value.</item>
/// </list>
/// Deliberately broad: a hidden non-secret costs a re-run; a printed secret cannot be taken back.
/// </para>
/// </summary>
public static class SecretRedactor
{
    /// <summary>The placeholder printed instead of a secret.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>
    /// Words that mark a normalised name as a credential. Matched as substrings of the name with
    /// separators removed, so the list holds each word once, in its joined-up form.
    /// </summary>
    private static readonly string[] SecretWords =
    [
        "password",
        "passwd",
        "pwd",
        "passphrase",
        "secret",
        "token",
        "apikey",
        "authorization",
        "cookie",
        "credential",
        "privatekey",
        "connectionstring",
        "sessionid",
    ];

    // Readable in a terminal: no \uXXXX escaping of non-ASCII text or of < > in markup.
    private static readonly JsonSerializerOptions LogJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // An ISO-8601 date or date-time, which is never a secret even under a secret-sounding name.
    private static readonly Regex IsoDate = new(
        @"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?)?(Z|[+-]\d{2}:?\d{2})?$",
        RegexOptions.CultureInvariant
    );

    // The user:pass@ part of an http(s) URL.
    private static readonly Regex UrlUserInfo = new(
        @"^(https?://)[^/?#@]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    // One name=value pair of a URL query string.
    private static readonly Regex QueryPair = new(
        @"([?&])([^=&#]+)=([^&#]*)",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Whether a JSON property, form field, header or query parameter name holds a credential:
    /// it contains a password, secret, token, API key, authorization, cookie, credential,
    /// private key, passphrase, connection string or session id word once separators are
    /// dropped, or it is exactly <c>auth</c>. Case is ignored.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>True when its value must not be printed.</returns>
    public static bool IsSecretName(string name)
    {
        var normalised = Normalise(name);
        return normalised == "auth" || SecretWords.Any(normalised.Contains);
    }

    /// <summary>
    /// Replaces credential values in a body: form fields by name, JSON by the rules on
    /// <see cref="SecretRedactor"/>. Any other text is returned as it is, as is JSON that does
    /// not parse. JSON with nothing to hide is returned byte for byte as it was.
    /// </summary>
    /// <param name="text">The body text.</param>
    /// <param name="contentType">The body's content type, or null.</param>
    /// <returns>The body with secrets replaced by <see cref="Placeholder"/>.</returns>
    public static string RedactBody(string text, MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType ?? "";
        if (
            mediaType.Equals(
                "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return RedactForm(text);

        if (!mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase))
            return text;

        try
        {
            var node = JsonNode.Parse(text);
            return RedactNode(node) ? node!.ToJsonString(LogJson) : text;
        }
        catch (JsonException)
        {
            // Not valid JSON despite its type: there are no property names to redact by.
            return text;
        }
    }

    /// <summary>
    /// Hides the user info (<c>user:pass@</c>) and every secret-named query value of an http(s)
    /// URL. Anything that is not an http(s) URL is returned as it is.
    /// </summary>
    /// <param name="url">The URL text.</param>
    /// <returns>The URL with its credentials replaced by <see cref="Placeholder"/>.</returns>
    public static string RedactUrl(string url)
    {
        if (
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
            return url;

        var result = UrlUserInfo.Replace(url, $"$1{Placeholder}@");
        return QueryPair.Replace(
            result,
            m =>
                IsSecretName(Uri.UnescapeDataString(m.Groups[2].Value))
                    ? $"{m.Groups[1].Value}{m.Groups[2].Value}={Placeholder}"
                    : m.Value
        );
    }

    /// <summary>Renders a request header's values for a log, hiding a secret-named header's.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="values">The header values.</param>
    /// <returns>The values joined with commas, or <see cref="Placeholder"/>.</returns>
    public static string RedactHeader(string name, IEnumerable<string> values) =>
        IsSecretName(name) ? Placeholder : string.Join(", ", values);

    /// <summary>Lower-cases a name and drops everything but letters and digits.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The normalised name.</returns>
    private static string Normalise(string name) =>
        new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    /// <summary>Whether a name is a header collection: <c>headers</c> or ends in it.</summary>
    /// <param name="name">The property name.</param>
    /// <returns>True for <c>headers</c>, <c>requestHeaders</c>, <c>response_headers</c> and so on.</returns>
    private static bool IsHeadersName(string name) => Normalise(name).EndsWith("headers");

    /// <summary>Hides the value of every secret-named field of a form body.</summary>
    /// <param name="text">The <c>application/x-www-form-urlencoded</c> body.</param>
    /// <returns>The body with secret values replaced.</returns>
    private static string RedactForm(string text) =>
        string.Join(
            "&",
            text.Split('&')
                .Select(pair =>
                {
                    var name = pair.Split('=', 2)[0];
                    return IsSecretName(Uri.UnescapeDataString(name))
                        ? $"{name}={Placeholder}"
                        : pair;
                })
        );

    /// <summary>
    /// Applies the redaction rules to a JSON tree, in place: secret names, header collections,
    /// alias/value pairs and URLs.
    /// </summary>
    /// <param name="node">The node to walk.</param>
    /// <returns>True when anything was replaced.</returns>
    private static bool RedactNode(JsonNode? node)
    {
        var redacted = false;
        switch (node)
        {
            case JsonObject obj:
                // A property value is judged by its alias, not by the "value" key (#349).
                var aliasPair =
                    obj["alias"] is JsonValue alias
                    && alias.TryGetValue<string>(out var aliasName)
                    && IsSecretName(aliasName);

                // ToList: the loop replaces values, which would otherwise invalidate the iterator.
                foreach (var (name, value) in obj.ToList())
                {
                    if (IsHeadersName(name))
                        redacted |= RedactHeaders(value);
                    else if (IsSecretName(name) || (aliasPair && name == "value"))
                        redacted |= RedactStrings(value);
                    else
                        redacted |= RedactNode(value);
                }
                break;
            case JsonArray array:
                foreach (var item in array.ToList())
                    redacted |= RedactNode(item);
                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                var url = RedactUrl(text);
                if (url != text)
                {
                    value.ReplaceWith(url);
                    redacted = true;
                }
                break;
        }
        return redacted;
    }

    /// <summary>
    /// Hides every string under a secret name: the value itself, or each string inside an object
    /// or array. Numbers, booleans, nulls and ISO dates are kept (#373).
    /// </summary>
    /// <param name="node">The value under the secret name.</param>
    /// <returns>True when anything was replaced.</returns>
    private static bool RedactStrings(JsonNode? node)
    {
        var redacted = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj.ToList())
                    redacted |= RedactStrings(value);
                break;
            case JsonArray array:
                foreach (var item in array.ToList())
                    redacted |= RedactStrings(item);
                break;
            case JsonValue value
                when value.TryGetValue<string>(out var text) && !IsoDate.IsMatch(text):
                value.ReplaceWith(Placeholder);
                redacted = true;
                break;
        }
        return redacted;
    }

    /// <summary>
    /// Hides every header value in a header collection: each string in an object or array, or
    /// the value part of every <c>Name: value</c> line of header text (a webhook log's
    /// <c>requestHeaders</c>).
    /// </summary>
    /// <param name="node">The value under the headers name.</param>
    /// <returns>True when anything was replaced.</returns>
    private static bool RedactHeaders(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
            return RedactStrings(node);

        var lines = text.Split('\n')
            .Select(line =>
            {
                var colon = line.IndexOf(':');
                if (colon <= 0)
                    return line;
                // Keep a CRLF line's \r, so the text keeps its line endings.
                var cr = line.EndsWith('\r') ? "\r" : "";
                return $"{line[..colon]}: {Placeholder}{cr}";
            });
        var result = string.Join('\n', lines);
        if (result == text)
            return false;
        value.ReplaceWith(result);
        return true;
    }
}
