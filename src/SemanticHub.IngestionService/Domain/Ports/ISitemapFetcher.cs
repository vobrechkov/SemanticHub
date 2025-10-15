using SemanticHub.IngestionService.Domain.Sitemaps;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Abstraction for retrieving sitemap documents.
/// </summary>
public interface ISitemapFetcher
{
    Task<SitemapFetchResult> FetchAsync(
        Uri sitemapUri,
        CancellationToken cancellationToken = default);
}
