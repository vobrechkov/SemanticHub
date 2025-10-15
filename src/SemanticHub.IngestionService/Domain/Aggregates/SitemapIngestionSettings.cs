namespace SemanticHub.IngestionService.Domain.Aggregates;

/// <summary>
/// Fine-grained configuration for sitemap ingestion requests.
/// </summary>
public sealed record SitemapIngestionSettings
{
    /// <summary>
    /// Optional override for the maximum number of pages to ingest from the sitemap.
    /// </summary>
    public int? MaxPages { get; init; }

    /// <summary>
    /// Optional override for the depth that nested sitemaps will be followed.
    /// </summary>
    public int? MaxDepth { get; init; }

    /// <summary>
    /// Optional throttle between individual page scrapes in milliseconds.
    /// </summary>
    public int? ThrottleMilliseconds { get; init; }

    /// <summary>
    /// Optional flag to override whether robots.txt should be honoured.
    /// </summary>
    public bool? RespectRobotsTxt { get; init; }

    /// <summary>
    /// Domains that are allowed to be ingested. If empty the sitemap host is used.
    /// </summary>
    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();
}
