namespace SemanticHub.IngestionService.Domain.Results;

/// <summary>
/// Summary outcome returned from sitemap ingestion workflow executions.
/// </summary>
public sealed class SitemapIngestionResult
{
    public string SitemapUrl { get; set; } = string.Empty;

    public int TotalDiscovered { get; set; }

    public int TotalFiltered { get; set; }

    public int TotalIngested { get; set; }

    public int TotalFailed { get; set; }

    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    public TimeSpan Duration { get; set; }

    public string? Message { get; set; }
}
