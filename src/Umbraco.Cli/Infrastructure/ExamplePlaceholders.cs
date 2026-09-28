namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The closed placeholder vocabulary for help examples (docs/conventions.md 8.5). An example stands
/// for an id either with a truncated id (<see cref="TruncatedIdPattern"/>, e.g. <c>3f7a8b2e-...</c>)
/// or with one of the named placeholders in <see cref="Values"/>; every other value is written out
/// as a real one. <c>HelpTextTests</c> swaps each placeholder for its value and parses the result,
/// so a placeholder not listed here fails the build's tests. Adding one means adding it here.
/// </summary>
public static class ExamplePlaceholders
{
    /// <summary>The GUID an id placeholder or a truncated id stands for when an example is parsed.</summary>
    public const string SampleId = "3f7a8b2e-1234-5678-abcd-ef0123456789";

    /// <summary>
    /// A truncated id: eight hex digits, a dash and an ellipsis (<c>3f7a8b2e-...</c>), for any id.
    /// </summary>
    public const string TruncatedIdPattern = @"\b[0-9a-f]{8}-\.\.\.";

    /// <summary>
    /// Each named placeholder (kebab-case, in angle brackets) and the value it parses as: the ids
    /// become <see cref="SampleId"/>, and <c>&lt;secret&gt;</c> a sample secret.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<
        string,
        string
    >
    {
        ["<id>"] = SampleId,
        ["<guid>"] = SampleId,
        ["<folder-id>"] = SampleId,
        ["<version-id>"] = SampleId,
        ["<relation-type-id>"] = SampleId,
        ["<section-id>"] = SampleId,
        ["<secret>"] = "s3cret",
    };
}
