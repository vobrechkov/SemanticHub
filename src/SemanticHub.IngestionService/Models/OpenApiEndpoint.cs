using Microsoft.OpenApi.Models;

namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Represents a single OpenAPI endpoint/operation as a document
/// Uses built-in Microsoft.OpenApi.Models types to avoid duplication
/// </summary>
public class OpenApiEndpoint
{
    /// <summary>
    /// Unique identifier for this endpoint
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, etc.)
    /// </summary>
    public required string Method { get; set; }

    /// <summary>
    /// URL path template
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Operation ID from OpenAPI spec
    /// </summary>
    public string? OperationId { get; set; }

    /// <summary>
    /// Operation summary
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Detailed description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Tags/categories for this endpoint
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Parameters (path, query, header, cookie) - uses built-in OpenAPI model
    /// </summary>
    public IList<OpenApiParameter> Parameters { get; set; } = [];

    /// <summary>
    /// Request body schema and examples - uses built-in OpenAPI model
    /// </summary>
    public OpenApiRequestBody? RequestBody { get; set; }

    /// <summary>
    /// Response schemas and examples - uses built-in OpenAPI model
    /// </summary>
    public OpenApiResponses Responses { get; set; } = [];

    /// <summary>
    /// Security requirements
    /// </summary>
    public List<string> Security { get; set; } = [];

    /// <summary>
    /// Servers where this operation is available
    /// </summary>
    public List<string> Servers { get; set; } = [];

    /// <summary>
    /// API version
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Source OpenAPI spec URL or file
    /// </summary>
    public string? SourceSpec { get; set; }
}
