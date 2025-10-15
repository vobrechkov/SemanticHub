using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Splits generated Markdown into smaller documents when necessary.
/// </summary>
public interface IOpenApiDocumentSplitter
{
    IReadOnlyList<OpenApiEndpointDocument> Split(
        OpenApiSpecificationDocument specification,
        OpenApiEndpoint endpoint,
        string markdown);
}
