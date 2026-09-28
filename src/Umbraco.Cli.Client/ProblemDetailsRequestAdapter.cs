using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Wraps the Kiota request adapter so every call reads a 4xx/5xx error body as Umbraco's
/// ProblemDetails (#286). The generated builders only map the statuses the OpenAPI spec declares,
/// and Umbraco also returns undeclared ones (a 500 from <c>user invite</c> with
/// <c>operationStatus: CannotInvite</c>, say). With no factory for such a status Kiota throws a
/// bare <see cref="ApiException"/> and the body - the one place that says what went wrong - is
/// never read. The declared mappings are kept; only a missing <c>4XX</c> / <c>5XX</c> is filled in.
/// </summary>
/// <param name="inner">The adapter that actually sends requests.</param>
internal sealed class ProblemDetailsRequestAdapter(IRequestAdapter inner) : IRequestAdapter
{
    /// <summary>The fallback mappings: any client or server error body is a ProblemDetails.</summary>
    internal static readonly Dictionary<string, ParsableFactory<IParsable>> Fallback = new()
    {
        { "4XX", Gen.ProblemDetails.CreateFromDiscriminatorValue },
        { "5XX", Gen.ProblemDetails.CreateFromDiscriminatorValue },
    };

    /// <summary>
    /// Adds the <see cref="Fallback"/> ranges to a call's own error mapping. Kiota looks up the
    /// exact status first and the range second, so a declared mapping still wins.
    /// </summary>
    /// <param name="declared">The generated call's error mapping, or null when it declared none.</param>
    /// <returns>The mapping to send with.</returns>
    internal static Dictionary<string, ParsableFactory<IParsable>> WithFallback(
        Dictionary<string, ParsableFactory<IParsable>>? declared
    )
    {
        var merged = declared is null
            ? new Dictionary<string, ParsableFactory<IParsable>>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ParsableFactory<IParsable>>(
                declared,
                StringComparer.OrdinalIgnoreCase
            );
        foreach (var (range, factory) in Fallback)
            merged.TryAdd(range, factory);
        return merged;
    }

    /// <inheritdoc />
    public ISerializationWriterFactory SerializationWriterFactory =>
        inner.SerializationWriterFactory;

    /// <inheritdoc />
    public string? BaseUrl
    {
        get => inner.BaseUrl;
        set => inner.BaseUrl = value;
    }

    /// <inheritdoc />
    public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) =>
        inner.EnableBackingStore(backingStoreFactory);

    /// <inheritdoc />
    public Task<ModelType?> SendAsync<ModelType>(
        RequestInformation requestInfo,
        ParsableFactory<ModelType> factory,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
        CancellationToken cancellationToken = default
    )
        where ModelType : IParsable =>
        inner.SendAsync(requestInfo, factory, WithFallback(errorMapping), cancellationToken);

    /// <inheritdoc />
    public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
        RequestInformation requestInfo,
        ParsableFactory<ModelType> factory,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
        CancellationToken cancellationToken = default
    )
        where ModelType : IParsable =>
        inner.SendCollectionAsync(
            requestInfo,
            factory,
            WithFallback(errorMapping),
            cancellationToken
        );

    /// <inheritdoc />
    public Task<ModelType?> SendPrimitiveAsync<ModelType>(
        RequestInformation requestInfo,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
        CancellationToken cancellationToken = default
    ) =>
        inner.SendPrimitiveAsync<ModelType>(
            requestInfo,
            WithFallback(errorMapping),
            cancellationToken
        );

    /// <inheritdoc />
    public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
        RequestInformation requestInfo,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
        CancellationToken cancellationToken = default
    ) =>
        inner.SendPrimitiveCollectionAsync<ModelType>(
            requestInfo,
            WithFallback(errorMapping),
            cancellationToken
        );

    /// <inheritdoc />
    public Task SendNoContentAsync(
        RequestInformation requestInfo,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
        CancellationToken cancellationToken = default
    ) => inner.SendNoContentAsync(requestInfo, WithFallback(errorMapping), cancellationToken);

    /// <inheritdoc />
    public Task<T?> ConvertToNativeRequestAsync<T>(
        RequestInformation requestInfo,
        CancellationToken cancellationToken = default
    ) => inner.ConvertToNativeRequestAsync<T>(requestInfo, cancellationToken);
}
