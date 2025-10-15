using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Sitemaps;

namespace SemanticHub.IngestionService.Services.Sitemaps;

/// <summary>
/// Produces heuristic scores combining declared change frequency, priority, and recency.
/// </summary>
public sealed class DefaultChangeFrequencyHeuristic(
    IngestionOptions options) : IChangeFrequencyHeuristic
{
    private readonly SitemapIngestionOptions _sitemapOptions = options.Sitemap;

    public double CalculateScore(SitemapEntry entry, SitemapIngestionContext context)
    {
        var changeFrequencyScore = MapChangeFrequency(entry.ChangeFrequency);
        var recencyScore = CalculateRecency(entry.LastModified);
        var frequencyWeight = Math.Clamp(_sitemapOptions.ChangeFrequencyWeight, 0, 1);
        var combined = (frequencyWeight * changeFrequencyScore) + ((1 - frequencyWeight) * recencyScore);

        if (entry.Priority is { } priority)
        {
            combined = (combined + Math.Clamp(priority, 0, 1)) / 2.0;
        }

        return Math.Clamp(combined, 0, 1);
    }

    private static double MapChangeFrequency(string? changeFrequency)
    {
        if (string.IsNullOrWhiteSpace(changeFrequency))
        {
            return 0.5;
        }

        return changeFrequency.ToLowerInvariant() switch
        {
            "always" => 1.0,
            "hourly" => 0.95,
            "daily" => 0.85,
            "weekly" => 0.7,
            "monthly" => 0.5,
            "yearly" => 0.25,
            "never" => 0.1,
            _ => 0.5
        };
    }

    private double CalculateRecency(DateTimeOffset? lastModified)
    {
        if (!lastModified.HasValue)
        {
            return 0.5;
        }

        var age = DateTimeOffset.UtcNow - lastModified.Value;
        if (age <= TimeSpan.Zero)
        {
            return 1.0;
        }

        var halfLifeDays = Math.Max(1, _sitemapOptions.RecencyHalfLifeDays);
        var halfLife = TimeSpan.FromDays(halfLifeDays);
        var exponent = -Math.Log(2) * age.TotalDays / halfLife.TotalDays;
        return Math.Clamp(Math.Exp(exponent), 0, 1);
    }
}
