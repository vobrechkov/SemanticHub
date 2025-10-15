namespace SemanticHub.Api.Models;

/// <summary>
/// Request payload for ingesting Markdown content through the ingestion service.
/// </summary>
public class MarkdownIngestionRequest
{
    /// <summary>
    /// Optional identifier for the document. If not supplied, the ingestion pipeline generates one.
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Title for the document.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Source URL or path for traceability.
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Describes the origin of the content (e.g. "manual", "webpage", "openapi").
    /// </summary>
    public string? SourceType { get; set; } = "manual";

    /// <summary>
    /// Optional tags attached to the document.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Additional metadata supplied by callers.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Markdown content to ingest.
    /// </summary>
    public required string Content { get; set; }
}

/// <summary>
/// Request payload for scraping and ingesting a web page.
/// </summary>
public class WebPageIngestionRequest
{
    /// <summary>
    /// URL of the page to scrape.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Optional identifier for the resulting document.
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Optional override for the document title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Optional tags to apply to the ingested document.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Additional metadata to attach to the document.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Request payload for ingesting OpenAPI specifications.
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
    /// Additional metadata to attach to all ingested endpoints
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Response from the ingestion service describing the outcome.
/// </summary>
public class IngestionResponse
{
    /// <summary>
    /// Whether the ingestion operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Identifier of the document stored in Azure AI Search.
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the Azure AI Search index that received the document.
    /// </summary>
    public string? IndexName { get; set; }

    /// <summary>
    /// Number of semantic chunks created in the index.
    /// </summary>
    public int ChunksIndexed { get; set; }

    /// <summary>
    /// Error message when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Descriptive message about the operation outcome.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Response from the ingestion service for OpenAPI spec ingestion.
/// </summary>
public class OpenApiIngestionResponse
{
    /// <summary>
    /// Whether the ingestion operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Source of the OpenAPI specification.
    /// </summary>
    public string SpecSource { get; set; } = string.Empty;

    /// <summary>
    /// Number of endpoints successfully processed.
    /// </summary>
    public int EndpointsProcessed { get; set; }

    /// <summary>
    /// Total number of endpoints found in the spec.
    /// </summary>
    public int TotalEndpoints { get; set; }

    /// <summary>
    /// Total number of semantic chunks created across all endpoints.
    /// </summary>
    public int TotalChunksIndexed { get; set; }

    /// <summary>
    /// Descriptive message about the operation outcome.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// List of errors encountered during processing.
    /// </summary>
    public List<string>? Errors { get; set; }

    /// <summary>
    /// Error message when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
