namespace Umbraco.Cli.Commands;

/// <summary>
/// Input the CLI rejects after parsing - a <c>--json-body</c> that is not an object, two inputs
/// that contradict each other. Nothing was sent; the caller must fix the input, so the executor
/// reports it as <c>invalid_argument</c> (exit 1), the same category a parse error gets (#256).
/// </summary>
/// <param name="message">What is wrong with the input, and how to fix it.</param>
public sealed class InvalidInputException(string message) : Exception(message);
