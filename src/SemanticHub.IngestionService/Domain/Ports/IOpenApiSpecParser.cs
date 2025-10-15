using SemanticHub.IngestionService.Domain.OpenApi;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Parses OpenAPI specification content into a normalized domain representation.
/// </summary>
public interface IOpenApiSpecParser
{
    /// <summary>
    /// Quick heuristic check to determine if the supplied content resembles an OpenAPI specification.
    /// </summary>
    bool LooksLikeSpecification(string content);

    /// <summary>
    /// Parse the specification document into a structured representation.
    /// </summary>
    Task<OpenApiSpecificationDocument> ParseAsync(
        OpenApiSpecDocument specDocument,
        CancellationToken cancellationToken = default);
}
