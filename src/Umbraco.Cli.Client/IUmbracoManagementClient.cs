namespace Umbraco.Cli.Client;

/// <summary>
/// Typed client for the Umbraco Management API (v1). Composed from per-area role
/// interfaces (ISP) so fakes and consumers can depend on only the area they use
/// (e.g. <see cref="IContentClient"/>) rather than this whole surface. The production
/// HTTP implementation and the test fake implement the full composite.
///
/// This interface can be backed by either the hand-crafted HttpClient
/// implementation (default) or a Kiota-generated client once you have
/// a running Umbraco instance:
///   kiota generate --language csharp \
///     --openapi http://localhost:5000/umbraco/swagger/management/swagger.json \
///     --output src/Umbraco.Cli.Client/Generated \
///     --namespace-name Umbraco.Cli.Client \
///     --class-name UmbracoManagementClient
/// </summary>
public interface IUmbracoManagementClient :
    IAuthClient,
    IContentClient,
    IMediaClient,
    IDocumentTypeClient,
    IDataTypeClient,
    ILanguageClient,
    ITemplateClient,
    IMemberClient,
    IUserClient,
    IDictionaryClient,
    IWebhookClient;
