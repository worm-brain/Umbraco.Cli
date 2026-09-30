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
///   kiota generate --culture csharp \
///     --openapi http://localhost:5000/umbraco/swagger/management/swagger.json \
///     --output src/Umbraco.Cli.Client/Generated \
///     --namespace-name Umbraco.Cli.Client \
///     --class-name UmbracoManagementClient
/// </summary>
public interface IUmbracoManagementClient
    : IAuthClient,
        IContentClient,
        IMediaClient,
        IMediaTypeClient,
        IDocumentTypeClient,
        IDataTypeClient,
        ILanguageClient,
        ITemplateClient,
        IMemberClient,
        IMemberTypeClient,
        IUserClient,
        IDictionaryClient,
        IWebhookClient,
        // Full-fidelity raw-JSON schema access for the export/diff/apply pipeline (#68, ADR 0005).
        ISchemaClient,
        // Full-fidelity raw-JSON document access for the content pipeline (#100, ADR 0006).
        IContentSnapshotClient,
        // Raw media and file access for the media pipeline (#226, ADR 0008).
        IMediaSnapshotClient,
        // Static-file resources: scripts, stylesheets, partial views (#105).
        IStaticFileClient,
        // Small coverage resources: member groups, tags, cultures (#107).
        IMemberGroupClient,
        ITagClient,
        ICultureClient,
        // User-administration resources: user groups, user data (#109).
        IUserGroupClient,
        IUserDataClient,
        // Document blueprints (content templates): CRUD, folders, scaffold, move (#113).
        IDocumentBlueprintClient,
        // Read-only diagnostics: server, health, log-viewer, models-builder, manifest (#115).
        IServerClient,
        IHealthClient,
        ILogViewerClient,
        IModelsBuilderClient,
        IManifestClient,
        // Redirects and relations (#118).
        IRedirectClient,
        IRelationTypeClient,
        IRelationClient,
        // Examine, imaging, property-type usage (#121).
        IExamineClient,
        IImagingClient,
        IPropertyTypeClient,
        // One <id|alias> lookup for every kind (#250 Phase 3).
        IReferenceResolver,
        // The guarded raw request behind `umbraco api` (#438, ADR 0010).
        IPassthroughClient;
