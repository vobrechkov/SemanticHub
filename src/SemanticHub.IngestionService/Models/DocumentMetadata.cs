namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Metadata for an ingested document
/// </summary>
public class DocumentMetadata
{
    /// <summary>
    /// Unique identifier for the document
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Document title
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Source URL or file path
    /// </summary>
    public required string SourceUrl { get; set; }

    /// <summary>
    /// Document type (webpage, openapi, etc.)
    /// </summary>
    public required string SourceType { get; set; }

    /// <summary>
    /// When the document was ingested
    /// </summary>
    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the source was last modified (if available)
    /// </summary>
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Content author (if available)
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Document description or summary
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Tags or keywords
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Additional custom metadata
    /// </summary>
    public Dictionary<string, object> CustomMetadata { get; set; } = [];
}
