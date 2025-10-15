using SemanticHub.IngestionService.Domain.Sitemaps;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Parses sitemap XML and produces entries plus nested sitemap references.
/// </summary>
public interface ISitemapParser
{
    SitemapParseResult Parse(Uri sourceUri, string content);
}
