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
}
