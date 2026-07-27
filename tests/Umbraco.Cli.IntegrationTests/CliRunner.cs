using System.Diagnostics;

namespace Umbraco.Cli.IntegrationTests;

/// <summary>
/// The result of invoking the CLI: exit code and captured streams. See
/// <see cref="CliRunner.Run"/>.
/// </summary>
/// <param name="ExitCode">Process exit code (0 success, 1 API/unexpected, 2 aborted, 130 cancelled).</param>
/// <param name="Stdout">Captured standard output (the JSON envelope on success).</param>
/// <param name="Stderr">Captured standard error (the JSON error envelope on failure).</param>
public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Whether the process exited successfully.</summary>
    public bool Ok => ExitCode == 0;

    /// <summary>
    /// Parses <see cref="Stdout"/> as the CLI's JSON envelope and returns the <c>data</c>
    /// element (or the root if there is no data member).
    /// </summary>
    /// <returns>The parsed <c>data</c> JSON element.</returns>
    public JsonElement Data()
    {
        using var doc = JsonDocument.Parse(Stdout);
        return doc.RootElement.TryGetProperty("data", out var data)
            ? data.Clone()
            : doc.RootElement.Clone();
    }
}

/// <summary>
/// Invokes the built <c>umbraco</c> CLI as a subprocess so integration tests exercise the
/// real end-to-end path (arg parsing, DI, auth, output writer). The CLI emits JSON when its
/// stdout is redirected, which it is here.
/// </summary>
public static class CliRunner
{
    private static readonly Lazy<string> CliDllPath = new(LocateCliDll);

    /// <summary>
    /// Runs the CLI with the given arguments and returns the captured result. Environment
    /// variables (including any UMBRACO_* auth vars) are inherited by the child process.
    /// </summary>
    /// <param name="args">CLI arguments, e.g. <c>["content", "list", "--take", "5"]</c>.</param>
    /// <returns>The exit code and captured stdout/stderr.</returns>
    public static CliResult Run(params string[] args) => RunWithInput(null, args);

    /// <summary>
    /// Runs the CLI with the given arguments, first writing <paramref name="stdin"/> to the
    /// child's standard input (for testing <c>--json-body -</c> piping, #63). A null
    /// <paramref name="stdin"/> means no input is written (stdin is still closed, so the child
    /// is non-interactive).
    /// </summary>
    /// <param name="stdin">Text to pipe to the process's stdin, or null for none.</param>
    /// <param name="args">CLI arguments.</param>
    /// <returns>The exit code and captured stdout/stderr.</returns>
    public static CliResult RunWithInput(string? stdin, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Redirect (and immediately close) stdin so the child is deterministically
            // non-interactive: Console.IsInputRedirected is true, so the destructive-op
            // confirmation gate (#70) always takes the non-interactive path instead of
            // inheriting the test host's stdin and potentially blocking on Console.ReadLine.
            RedirectStandardInput = true,
            // Pipe stdin as UTF-8 so a non-ASCII --json-body survives (the CLI reads stdin as
            // UTF-8); without this the harness would encode using the console code page.
            StandardInputEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            // "dotnet exec <dll> <args>" runs the framework-dependent CLI assembly.
            ArgumentList = { "exec", CliDllPath.Value },
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process =
            Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the CLI process.");

        // Write any piped body, then close stdin. Closing it makes an unread stdin return
        // end-of-input rather than hang, and keeps the child non-interactive (#70). Note: this
        // writes the whole body before draining stdout, so a body larger than the OS pipe
        // buffer combined with early child output could deadlock — fine for test-sized bodies.
        if (stdin is not null)
            process.StandardInput.Write(stdin);
        process.StandardInput.Close();

        // Read both streams before waiting to avoid a full-pipe deadlock.
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new CliResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Locates the built <c>Umbraco.Cli.dll</c> by walking up from the test output directory
    /// to the repo root. The build configuration THIS test assembly was compiled in is tried
    /// first, so <c>dotnet test</c> (Debug) exercises the freshly-built Debug CLI rather than a
    /// possibly-stale Release build (and vice-versa) — otherwise a leftover Release binary
    /// silently shadows the code under test.
    /// </summary>
    /// <returns>The absolute path to the CLI assembly.</returns>
    /// <exception cref="FileNotFoundException">If no built CLI assembly is found.</exception>
    private static string LocateCliDll()
    {
#if DEBUG
        var configs = new[] { "Debug", "Release" };
#else
        var configs = new[] { "Release", "Debug" };
#endif
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var config in configs)
            {
                var candidate = Path.Combine(
                    dir.FullName,
                    "src",
                    "Umbraco.Cli",
                    "bin",
                    config,
                    "net9.0",
                    "Umbraco.Cli.dll"
                );
                if (File.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate a built Umbraco.Cli.dll. Build the solution first."
        );
    }
}
