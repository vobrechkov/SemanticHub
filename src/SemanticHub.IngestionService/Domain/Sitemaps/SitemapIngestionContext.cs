using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;

namespace SemanticHub.IngestionService.Domain.Sitemaps;

/// <summary>
/// Ambient context describing the current sitemap ingestion strategy.
/// </summary>
public sealed record SitemapIngestionContext(
    Uri RootSitemap,
    SitemapIngestionSettings Settings,
    SitemapIngestionOptions Options,
    IngestionMetadata Metadata);
