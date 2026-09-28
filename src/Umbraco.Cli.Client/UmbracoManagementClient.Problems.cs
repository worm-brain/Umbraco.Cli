using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions.Serialization;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Turning Umbraco's ProblemDetails error bodies into what the CLI reports (#286): a message that
/// says what went wrong and what to fix, and the body itself as the error's <c>details</c>.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Hints for <c>operationStatus</c> values whose fix is not in Umbraco's own wording. The
    /// invite failure is the worst case: Umbraco says only "Cannot send user invitation", and
    /// nothing is logged, when the cause is that no SMTP server is configured.
    /// </summary>
    private static readonly Dictionary<string, string> OperationStatusHints = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["CannotInvite"] =
            "Umbraco could not send the invitation email; configure SMTP "
            + "(Umbraco:CMS:Global:Smtp) on the site, or create the user without an invite.",
    };

    /// <summary>
    /// Builds the message for a ProblemDetails body, in the order a reader needs it: what failed
    /// (<c>title</c>) and Umbraco's code for it (<c>operationStatus</c>), the reason (the first
    /// line of <c>detail</c>), what to fix (<c>invalidProperties</c> and the <c>errors</c> map,
    /// #48), and a hint for known codes.
    /// <para>
    /// Only the first line of <c>detail</c> is used, and not at all when it is a stack trace:
    /// on an unhandled exception Umbraco puts the exception message in <c>title</c> and the trace
    /// in <c>detail</c>, and the trace used to become the whole message. The full body is still in
    /// the error's details.
    /// </para>
    /// </summary>
    /// <param name="pd">The ProblemDetails Kiota read from the error response.</param>
    /// <param name="status">The HTTP status, for a body that has no title.</param>
    /// <returns>The message.</returns>
    internal static string DescribeProblem(Gen.ProblemDetails pd, int status)
    {
        var title = Blank(pd.Title);
        var reason = FirstLine(pd.Detail);
        var operationStatus = AdditionalString(pd, "operationStatus");

        // What failed. A body with only a detail leads with it, as before.
        var head = title ?? reason ?? $"Error {status}";
        if (operationStatus is not null)
            head += $" ({operationStatus})";

        // The reason, unless it is the head already (or a trace, which FirstLine dropped).
        var message =
            reason is not null
            && !string.Equals(reason, head, StringComparison.Ordinal)
            && title is not null
                ? $"{head}: {reason}"
                : head;
        message = EndSentence(message);

        var invalid = AdditionalStrings(pd, "invalidProperties");
        if (invalid.Count > 0)
            message += $" Invalid properties: {string.Join(", ", invalid)}.";

        if (FormatProblemDetailsErrors(pd) is { } fieldErrors)
            message += $" ({fieldErrors})";

        if (
            operationStatus is not null
            && OperationStatusHints.TryGetValue(operationStatus, out var hint)
        )
            message += $" {hint}";

        return message;
    }

    /// <summary>
    /// The ProblemDetails body as Umbraco sent it, for the error's <c>details</c> (#286): the
    /// standard members followed by every extension member (<c>operationStatus</c>,
    /// <c>invalidProperties</c>, <c>errors</c>, and anything a later Umbraco adds), none of them
    /// dropped or renamed.
    /// </summary>
    /// <param name="pd">The ProblemDetails Kiota read from the error response.</param>
    /// <returns>The body as a JSON object.</returns>
    internal static JsonObject ProblemBody(Gen.ProblemDetails pd)
    {
        var body = new JsonObject();
        if (pd.Type is not null)
            body["type"] = pd.Type;
        if (pd.Title is not null)
            body["title"] = pd.Title;
        if (pd.Status is not null)
            body["status"] = pd.Status;
        if (pd.Detail is not null)
            body["detail"] = pd.Detail;
        if (pd.Instance is not null)
            body["instance"] = pd.Instance;

        // Kiota puts every member without a typed property in AdditionalData, as UntypedNodes
        // (or plain values for members it could read as primitives).
        foreach (var (name, value) in pd.AdditionalData ?? new Dictionary<string, object>())
            body[name] = value switch
            {
                UntypedNode node => UntypedNodeFactory.ToJsonNode(node),
                null => null,
                _ => JsonValue.Create(value.ToString()),
            };
        return body;
    }

    /// <summary>The first non-blank line of <paramref name="text"/>, or null when it is blank or a stack trace.</summary>
    /// <param name="text">The text, e.g. a ProblemDetails <c>detail</c>.</param>
    /// <returns>The first line, trimmed, or null.</returns>
    private static string? FirstLine(string? text)
    {
        var line = text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

        // A .NET stack frame starts with "at "; a trace carries no reason worth a message.
        return line is null || line.StartsWith("at ", StringComparison.Ordinal) ? null : line;
    }

    /// <summary>Ends <paramref name="text"/> with a full stop unless it already ends a sentence.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text, ending in punctuation.</returns>
    private static string EndSentence(string text) =>
        text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') ? text : text + ".";

    /// <summary>Returns null for a null/whitespace string, else the trimmed string.</summary>
    /// <param name="value">The string.</param>
    /// <returns>The trimmed string, or null.</returns>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A string extension member of a ProblemDetails body, e.g. <c>operationStatus</c>.</summary>
    /// <param name="pd">The ProblemDetails.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The value, or null when absent or not a string.</returns>
    private static string? AdditionalString(Gen.ProblemDetails pd, string name) =>
        pd.AdditionalData?.TryGetValue(name, out var raw) is true
            ? raw switch
            {
                UntypedString s => Blank(s.GetValue()),
                string s => Blank(s),
                _ => null,
            }
            : null;

    /// <summary>A string-array extension member of a ProblemDetails body, e.g. <c>invalidProperties</c>.</summary>
    /// <param name="pd">The ProblemDetails.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The non-blank strings, or an empty list when absent.</returns>
    private static IReadOnlyList<string> AdditionalStrings(Gen.ProblemDetails pd, string name) =>
        pd.AdditionalData?.TryGetValue(name, out var raw) is true && raw is UntypedArray array
            ? array
                .GetValue()
                .OfType<UntypedString>()
                .Select(s => Blank(s.GetValue()))
                .OfType<string>()
                .ToList()
            : [];
}
