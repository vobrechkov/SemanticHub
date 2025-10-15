namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Payload for ingesting content via sitemap traversal.
/// </summary>
public class SitemapIngestionRequest
{
    public required string SitemapUrl { get; set; }

    public string? DocumentIdPrefix { get; set; }

    public List<string>? Tags { get; set; }

    public Dictionary<string, object>? Metadata { get; set; }

    public List<string>? AllowedHosts { get; set; }

    public int? MaxPages { get; set; }

    public int? MaxDepth { get; set; }

    public int? ThrottleMilliseconds { get; set; }

    public bool? RespectRobotsTxt { get; set; }
}
