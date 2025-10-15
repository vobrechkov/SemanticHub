using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Abstraction for retrieving HTML content from external sources.
/// </summary>
public interface IHtmlScraper
{
    Task<ScrapedPage> ScrapeAsync(Uri url, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScrapedPage>> ScrapeManyAsync(
        IEnumerable<Uri> urls,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScrapedPage>> ScrapeRecursivelyAsync(
        Uri startUrl,
        int maxDepth = 2,
        int maxPages = 50,
        IEnumerable<string>? allowedDomains = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScrapedPage>> ScrapeSitemapAsync(
        Uri sitemapUrl,
        int maxPages = 100,
        CancellationToken cancellationToken = default);
}
