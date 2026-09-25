namespace Umbraco.Cli.Commands;

/// <summary>
/// What a <c>--json-body</c> create reports (#204): the id the item was created with, so a script
/// can chain to it, plus the name and alias the body gave it.
/// </summary>
/// <param name="Id">The created item's id.</param>
/// <param name="Name">The body's <c>name</c>, or null when it has none.</param>
/// <param name="Alias">The body's <c>alias</c>, or null for a kind without one (a data type).</param>
public sealed record RawCreated(Guid Id, string? Name, string? Alias);
