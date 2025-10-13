namespace SemanticHub.IngestionService.Models;

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
