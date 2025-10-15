using SemanticHub.IngestionService.Domain.Sitemaps;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Determines whether sitemap entries should be processed.
/// </summary>
public interface IUrlFilterPolicy
{
    Task<bool> ShouldIncludeAsync(
        SitemapEntry entry,
        SitemapIngestionContext context,
        CancellationToken cancellationToken = default);
}
