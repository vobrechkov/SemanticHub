using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Sitemaps;

namespace SemanticHub.IngestionService.Services.Sitemaps;

/// <summary>
/// Applies domain-specific policies to determine whether sitemap URLs should be ingested.
/// </summary>
public sealed class SitemapUrlFilterPolicy(
    HttpClient httpClient,
    ILogger<SitemapUrlFilterPolicy> logger,
    IngestionOptions options) : IUrlFilterPolicy
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<SitemapUrlFilterPolicy> _logger = logger;
    private readonly SitemapIngestionOptions _sitemapOptions = options.Sitemap;
    private readonly ConcurrentDictionary<string, Lazy<Task<RobotsRules>>> _robotsCache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> ShouldIncludeAsync(
        SitemapEntry entry,
        SitemapIngestionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);

        if (entry.Location.Scheme is not ("http" or "https"))
        {
            _logger.LogDebug("Skipping {Url} because non-http(s) scheme was detected", entry.Location);
            return false;
        }

        var allowedHosts = ResolveAllowedHosts(context);
        if (allowedHosts.Count > 0 &&
            !allowedHosts.Any(host => string.Equals(host, entry.Location.Host, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug(
                "Skipping {Url} because host {Host} is not within allowed hosts",
                entry.Location,
                entry.Location.Host);
            return false;
        }

        var respectRobots = context.Settings.RespectRobotsTxt ?? _sitemapOptions.RespectRobotsTxt;
        if (!respectRobots)
        {
            return true;
        }

        var isAllowed = await IsAllowedByRobotsAsync(entry.Location, cancellationToken);
        if (!isAllowed)
        {
            _logger.LogDebug("robots.txt disallowed {Url}", entry.Location);
        }

        return isAllowed;
    }

    private IReadOnlyCollection<string> ResolveAllowedHosts(SitemapIngestionContext context)
    {
        if (context.Settings.AllowedHosts.Count > 0)
        {
            return context.Settings.AllowedHosts;
        }

        if (context.Metadata.SourceUri is not null)
        {
            return new[] { context.Metadata.SourceUri.Host };
        }

        return new[] { context.RootSitemap.Host };
    }

    private async Task<bool> IsAllowedByRobotsAsync(Uri url, CancellationToken cancellationToken)
    {
        var baseUri = new Uri($"{url.Scheme}://{url.Host}");
        var lazyRules = _robotsCache.GetOrAdd(
            baseUri.AbsoluteUri,
            _ => new Lazy<Task<RobotsRules>>(
                () => FetchRobotsAsync(baseUri, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var rules = await lazyRules.Value;
        return rules.IsAllowed(url);
    }

    private async Task<RobotsRules> FetchRobotsAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        try
        {
            var robotsUri = new Uri(baseUri, "/robots.txt");
            var response = await _httpClient.GetAsync(robotsUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "robots.txt for {Host} returned {Status}, treating as allow all",
                    baseUri,
                    response.StatusCode);
                return RobotsRules.AllowAll;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return RobotsRules.Parse(content, _sitemapOptions.UserAgent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to download robots.txt for {Host}, assuming allow", baseUri);
            return RobotsRules.AllowAll;
        }
    }

    private sealed record RobotsRules(IReadOnlyList<string> Allow, IReadOnlyList<string> Disallow)
    {
        public static RobotsRules AllowAll { get; } = new([], []);

        public bool IsAllowed(Uri url)
        {
            if (Disallow.Count == 0)
            {
                return true;
            }

            var path = string.Concat(url.AbsolutePath, url.Query);
            var longestAllow = -1;
            var longestDisallow = -1;

            for (var i = 0; i < Allow.Count; i++)
            {
                var rule = NormalizeRule(Allow[i]);
                if (Matches(path, rule) && rule.Length > longestAllow)
                {
                    longestAllow = rule.Length;
                }
            }

            for (var i = 0; i < Disallow.Count; i++)
            {
                var rule = NormalizeRule(Disallow[i]);
                if (string.IsNullOrEmpty(rule))
                {
                    continue;
                }

                if (Matches(path, rule) && rule.Length > longestDisallow)
                {
                    longestDisallow = rule.Length;
                }
            }

            return longestDisallow <= longestAllow;
        }

        public static RobotsRules Parse(string content, string userAgent)
        {
            var allow = new List<string>();
            var disallow = new List<string>();
            var blocks = ParseBlocks(content);
            var agentToken = ExtractAgentToken(userAgent);

            if (blocks.TryGetValue(agentToken, out var specific))
            {
                allow.AddRange(specific.Allow);
                disallow.AddRange(specific.Disallow);
            }
            else if (blocks.TryGetValue("*", out var wildcard))
            {
                allow.AddRange(wildcard.Allow);
                disallow.AddRange(wildcard.Disallow);
            }

            if (allow.Count == 0 && disallow.Count == 0)
            {
                return AllowAll;
            }

            return new RobotsRules(allow, disallow);
        }

        private static Dictionary<string, RobotsRules> ParseBlocks(string content)
        {
            var result = new Dictionary<string, RobotsRules>(StringComparer.OrdinalIgnoreCase);
            var agents = new List<string>();
            var allow = new List<string>();
            var disallow = new List<string>();

            foreach (var rawLine in content.Split('\n'))
            {
                var line = StripComment(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var separator = line.IndexOf(':');
                if (separator <= 0 || separator == line.Length - 1)
                {
                    continue;
                }

                var directive = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();

                switch (directive.ToLowerInvariant())
                {
                    case "user-agent":
                        Commit();
                        agents.Add(value);
                        break;
                    case "allow":
                        allow.Add(value);
                        break;
                    case "disallow":
                        disallow.Add(value);
                        break;
                }
            }

            Commit();
            return result;

            void Commit()
            {
                if (agents.Count == 0)
                {
                    return;
                }

                var rule = new RobotsRules([..allow], [..disallow]);
                foreach (var agent in agents)
                {
                    result[agent] = rule;
                }

                agents.Clear();
                allow.Clear();
                disallow.Clear();
            }
        }

        private static string StripComment(string line)
        {
            var index = line.IndexOf('#');
            return index >= 0 ? line[..index].Trim() : line.Trim();
        }

        private static bool Matches(string path, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return false;
            }

            if (pattern == "/")
            {
                return true;
            }

            if (pattern.EndsWith('$'))
            {
                var trimmed = pattern[..^1];
                return path.Equals(trimmed, StringComparison.OrdinalIgnoreCase);
            }

            if (pattern.Contains('*'))
            {
                var segments = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
                var currentIndex = 0;

                foreach (var segment in segments)
                {
                    var idx = path.IndexOf(segment, currentIndex, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0)
                    {
                        return false;
                    }

                    currentIndex = idx + segment.Length;
                }

                return true;
            }

            return path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRule(string rule)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                return string.Empty;
            }

            return rule.Trim();
        }

        private static string ExtractAgentToken(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                return "*";
            }

            var separator = userAgent.IndexOfAny(['/', ' ']);
            return separator > 0 ? userAgent[..separator] : userAgent;
        }
    }
}
