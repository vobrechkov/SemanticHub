using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Generates Markdown documentation for OpenAPI endpoints.
/// </summary>
public interface IOpenApiMarkdownGenerator
{
    string Generate(OpenApiSpecificationDocument specification, OpenApiEndpoint endpoint);
}
