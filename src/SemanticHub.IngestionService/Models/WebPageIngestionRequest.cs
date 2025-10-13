using System.Collections.Generic;

namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Payload for ingesting web pages via the ingestion service.
/// </summary>
public class WebPageIngestionRequest
{
    /// <summary>
    /// URL of the page to scrape and ingest.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Optional identifier for the resulting document.
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Optional title override for the document.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Optional tags applied to the document.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional additional metadata to attach to the document.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
