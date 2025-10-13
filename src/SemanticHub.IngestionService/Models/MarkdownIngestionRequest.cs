namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Payload for ingesting documents from Azure Blob Storage.
/// </summary>
public class BlobIngestionRequest
{
    /// <summary>
    /// Blob path or prefix to ingest (e.g., "folder/" or "folder/file.md")
    /// </summary>
    public required string BlobPath { get; set; }

    /// <summary>
    /// Optional container name. If not specified, uses the default container from configuration.
    /// </summary>
    public string? ContainerName { get; set; }

    /// <summary>
    /// Optional tags to apply to all ingested documents
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional metadata to attach to all ingested documents
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Payload for ingesting HTML content via the ingestion service.
/// </summary>
public class HtmlIngestionRequest
{
    public string? DocumentId { get; set; }

    public string? Title { get; set; }

    public string? SourceUrl { get; set; }

    public List<string>? Tags { get; set; }

    public Dictionary<string, object>? Metadata { get; set; }

    public required string Content { get; set; }
}

/// <summary>
/// Payload for ingesting Markdown content via the ingestion service.
/// </summary>
public class MarkdownIngestionRequest
{
    public string? DocumentId { get; set; }

    public string? Title { get; set; }

    public string? SourceUrl { get; set; }

    public string? SourceType { get; set; }

    public List<string>? Tags { get; set; }

    public Dictionary<string, object>? Metadata { get; set; }

    public required string Content { get; set; }
}
