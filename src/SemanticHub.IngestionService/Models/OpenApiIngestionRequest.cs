namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Payload for ingesting OpenAPI specifications via the ingestion service.
/// </summary>
public class OpenApiIngestionRequest
{
    /// <summary>
    /// URL or file path to the OpenAPI specification (YAML or JSON)
    /// </summary>
    public required string SpecSource { get; set; }

    /// <summary>
    /// Optional base document ID prefix. Individual endpoints will be suffixed.
    /// If omitted, generated from the spec info.
    /// </summary>
    public string? DocumentIdPrefix { get; set; }

    /// <summary>
    /// Optional tags to apply to all ingested endpoints
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional metadata to attach to all ingested endpoints
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
