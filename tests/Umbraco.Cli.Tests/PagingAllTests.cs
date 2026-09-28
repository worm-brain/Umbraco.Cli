using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>--all</c> on paged commands (#196): the executor pages until the collection is exhausted,
/// reports the list complete, and fails loudly rather than truncating at the safety cap.
/// </summary>
[Collection("ConsoleCapture")]
public class PagingAllTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    /// <summary>The shipped tree, wired to <paramref name="client"/>.</summary>
    /// <param name="client">The fake client every command talks to.</param>
    /// <returns>The root command.</returns>
    private static RootCommand BuildRoot(IUmbracoManagementClient client)
    {
        var stub = new StubHttpClientFactory();
        var global = new GlobalOptions();
        var clientFactory = new FakeClientFactory(client);
        var configStore = new ConfigStore(
            Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json")
        );
        var auth = new UmbracoAuthService(stub);
        var factory = new CommandContextFactory(
            configStore,
            auth,
            stub,
            global,
            clientFactory,
            new MutationInterceptState()
        );
        var executor = new CommandExecutor(factory, new ConsoleConfirmationPrompt());
        return CliRoot.Build(global, configStore, auth, executor, stub, clientFactory);
    }

    /// <summary>Runs a command line, capturing stdout and stderr.</summary>
    /// <param name="root">The root command.</param>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code and the captured streams.</returns>
    private static async Task<(int Exit, string Out, string Err)> Run(RootCommand root, string args)
    {
        var (origOut, origErr) = (Console.Out, Console.Error);
        using var outSw = new StringWriter();
        using var errSw = new StringWriter();
        Console.SetOut(outSw);
        Console.SetError(errSw);
        try
        {
            var exit = await root.Parse(args).InvokeAsync();
            return (exit, outSw.ToString(), errSw.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    private const string Auth = "--host https://x --token t --output json";

    /// <summary>A fake whose data-type list holds <paramref name="count"/> items.</summary>
    /// <param name="count">How many data types to seed.</param>
    /// <returns>The fake.</returns>
    private static FakeUmbracoManagementClient WithDataTypes(int count)
    {
        var fake = new FakeUmbracoManagementClient();
        for (var i = 0; i < count; i++)
            fake.DataTypeList.Add(new DataTypeResponse { Id = Guid.NewGuid(), Name = $"DT {i}" });
        return fake;
    }

    [Fact]
    public async Task DataTypeList_All_ReturnsEveryItemAcrossPages()
    {
        var root = BuildRoot(WithDataTypes(250));

        var (exit, stdout, _) = await Run(root, $"{Auth} data-type list --all");

        Assert.Equal(0, exit);
        Assert.Equal(
            250,
            JsonDocument.Parse(stdout).RootElement.GetProperty("data").GetArrayLength()
        );
    }

    [Fact]
    public async Task DataTypeList_All_ReportsRealTotalAndNoMore()
    {
        var root = BuildRoot(WithDataTypes(250));

        var (_, stdout, _) = await Run(root, $"{Auth} data-type list --all");

        var meta = JsonDocument.Parse(stdout).RootElement.GetProperty("meta");
        Assert.Equal(
            (250, false),
            (meta.GetProperty("total").GetInt32(), meta.GetProperty("hasMore").GetBoolean())
        );
    }

    [Fact]
    public async Task DataTypeList_WithoutAll_StillReturnsOnePage()
    {
        var root = BuildRoot(WithDataTypes(250));

        var (_, stdout, _) = await Run(root, $"{Auth} data-type list");

        Assert.Equal(
            PagingOptions.DefaultTake,
            JsonDocument.Parse(stdout).RootElement.GetProperty("data").GetArrayLength()
        );
    }

    [Fact]
    public async Task DataTypeList_TruncatedCsv_HintAdvertisesAll()
    {
        var root = BuildRoot(WithDataTypes(250));

        var (_, _, stderr) = await Run(
            root,
            "--host https://x --token t --output csv data-type list"
        );

        Assert.Contains("or --all", stderr);
    }

    [Theory]
    [InlineData("data-type list --all --take 5")]
    [InlineData("data-type list --all --skip 10")]
    [InlineData("user list --all --skip 0")]
    public void All_WithExplicitSkipOrTake_IsAParseError(string args)
    {
        var errors = TestCliRoot.Build().Parse(args).Errors;

        Assert.Contains(errors, e => e.Message.Contains("--all"));
    }

    [Fact]
    public void All_Alone_Parses()
    {
        var errors = TestCliRoot.Build().Parse("data-type list --all").Errors;

        Assert.Empty(errors);
    }

    [Fact]
    public async Task CollectAllPagesAsync_TotalOverCap_FailsWithInvalidArgument()
    {
        // The server reports more than the cap on the first page, so the walk stops at once.
        var result = await CommandExecutor.CollectAllPagesAsync<int>(
            new FakeUmbracoManagementClient(),
            (_, _, _, _) =>
                Task.FromResult(
                    UmbracoResponse<PagedResponse<int>>.Success(
                        new PagedResponse<int>
                        {
                            Total = PagingOptions.MaxAllItems + 1,
                            Items = Enumerable.Range(0, PagingOptions.DefaultTake),
                        }
                    )
                ),
            CancellationToken.None
        );

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
    }

    [Fact]
    public async Task CollectAllPagesAsync_NoTotalAndNeverShort_FailsAtTheCap()
    {
        // A source that never reports a total and always returns a full page would loop forever;
        // the cap turns that into an error instead of a silent truncation.
        var calls = 0;
        var result = await CommandExecutor.CollectAllPagesAsync<int>(
            new FakeUmbracoManagementClient(),
            (_, _, take, _) =>
            {
                calls++;
                return Task.FromResult(
                    UmbracoResponse<PagedResponse<int>>.Success(
                        new PagedResponse<int> { Total = 0, Items = Enumerable.Range(0, take) }
                    )
                );
            },
            CancellationToken.None
        );

        Assert.Equal(
            (false, PagingOptions.MaxAllItems / PagingOptions.DefaultTake),
            (result.IsSuccess, calls)
        );
    }

    [Fact]
    public async Task CollectAllPagesAsync_PageFails_ReturnsThatFailure()
    {
        var result = await CommandExecutor.CollectAllPagesAsync<int>(
            new FakeUmbracoManagementClient(),
            (_, _, _, _) =>
                Task.FromResult(UmbracoResponse<PagedResponse<int>>.Failure(500, "boom")),
            CancellationToken.None
        );

        Assert.Equal("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task CollectAllPagesAsync_AsksForConsecutivePages()
    {
        var skips = new List<int>();
        await CommandExecutor.CollectAllPagesAsync<int>(
            new FakeUmbracoManagementClient(),
            (_, skip, take, _) =>
            {
                skips.Add(skip);
                var items = Enumerable.Range(skip, Math.Max(0, Math.Min(take, 230 - skip)));
                return Task.FromResult(
                    UmbracoResponse<PagedResponse<int>>.Success(
                        new PagedResponse<int> { Total = 230, Items = items }
                    )
                );
            },
            CancellationToken.None
        );

        Assert.Equal([0, 100, 200], skips);
    }
}
