using System.Net;

namespace SemanticHub.IngestionService.Domain.Sitemaps;

/// <summary>
/// Outcome of fetching a sitemap document.
/// </summary>
public sealed record SitemapFetchResult
{
    public bool Success { get; init; }

    public SitemapDocument? Document { get; init; }

    public HttpStatusCode? StatusCode { get; init; }

    public string? Error { get; init; }

    public static SitemapFetchResult FromSuccess(SitemapDocument document) => new()
    {
        Success = true,
        Document = document,
        StatusCode = HttpStatusCode.OK
    };

    public static SitemapFetchResult FromFailure(HttpStatusCode? statusCode, string message) => new()
    {
        Success = false,
        StatusCode = statusCode,
        Error = message
    };
}
