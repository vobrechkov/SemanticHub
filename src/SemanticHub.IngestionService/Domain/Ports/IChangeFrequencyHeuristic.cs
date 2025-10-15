using SemanticHub.IngestionService.Domain.Sitemaps;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Calculates prioritisation scores for sitemap entries using change frequency metadata.
/// </summary>
public interface IChangeFrequencyHeuristic
{
    double CalculateScore(SitemapEntry entry, SitemapIngestionContext context);
}
