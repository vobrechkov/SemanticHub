using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.OpenApi;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Resolves the raw specification content for an OpenAPI ingestion request.
/// </summary>
public interface IOpenApiSpecLocator
{
    Task<OpenApiSpecDocument> LocateAsync(
        OpenApiSpecificationIngestion request,
        CancellationToken cancellationToken = default);
}
