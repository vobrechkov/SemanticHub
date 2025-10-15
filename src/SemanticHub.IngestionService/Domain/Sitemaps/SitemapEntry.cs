namespace SemanticHub.IngestionService.Domain.Sitemaps;

/// <summary>
/// Represents a single entry returned from a sitemap document.
/// </summary>
public sealed record SitemapEntry
{
    public required Uri Location { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public string? ChangeFrequency { get; init; }

    public double? Priority { get; init; }

    /// <summary>
    /// Calculated score used by heuristics to prioritise ingestion order.
    /// </summary>
    public double HeuristicScore { get; init; }

    public bool HasChangedRecently(TimeSpan window) =>
        LastModified.HasValue && LastModified.Value >= DateTimeOffset.UtcNow.Subtract(window);
}
