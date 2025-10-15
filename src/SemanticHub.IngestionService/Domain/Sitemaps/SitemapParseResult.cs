namespace SemanticHub.IngestionService.Domain.Sitemaps;

/// <summary>
/// Represents the parsed outcome from a sitemap document including nested sitemap references.
/// </summary>
public sealed record SitemapParseResult
{
    public IReadOnlyList<SitemapEntry> Entries { get; init; } = Array.Empty<SitemapEntry>();

    public IReadOnlyList<Uri> ChildSitemaps { get; init; } = Array.Empty<Uri>();

    public static SitemapParseResult Empty { get; } = new();
}
