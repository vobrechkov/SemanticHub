namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Represents a scraped web page with content and metadata
/// </summary>
public class ScrapedPage
{
    /// <summary>
    /// Page URL
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Page title
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Raw HTML content
    /// </summary>
    public required string HtmlContent { get; set; }

    /// <summary>
    /// Converted Markdown content
    /// </summary>
    public string? MarkdownContent { get; set; }

    /// <summary>
    /// Extracted metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Links found on this page (for recursive crawling)
    /// </summary>
    public List<string> Links { get; set; } = [];

    /// <summary>
    /// When the page was scraped
    /// </summary>
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// HTTP status code
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Whether the page was successfully scraped
    /// </summary>
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}
