namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Payload for ingesting multiple web pages via the ingestion service.
/// Titles are automatically inferred from page content.
/// </summary>
public class BatchWebPageIngestionRequest
{
    /// <summary>
    /// List of URLs to scrape and ingest.
    /// </summary>
    public required List<string> Urls { get; set; }

    /// <summary>
    /// Maximum number of concurrent ingestion operations.
    /// </summary>
    public int? MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// Delay in milliseconds between requests to avoid overwhelming servers.
    /// </summary>
    public int? ThrottleMilliseconds { get; set; } = 250;

    /// <summary>
    /// Optional tags applied to all documents.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional additional metadata to attach to all documents.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
