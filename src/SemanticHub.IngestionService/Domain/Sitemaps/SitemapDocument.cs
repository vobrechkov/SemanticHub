namespace SemanticHub.IngestionService.Domain.Sitemaps;

/// <summary>
/// Raw sitemap document along with metadata captured during fetching.
/// </summary>
public sealed record SitemapDocument(
    Uri SourceUri,
    string Content,
    bool IsIndex,
    DateTimeOffset RetrievedAt);
