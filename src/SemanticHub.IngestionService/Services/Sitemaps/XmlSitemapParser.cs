using System.Xml.Linq;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Sitemaps;

namespace SemanticHub.IngestionService.Services.Sitemaps;

/// <summary>
/// Parses XML sitemap documents (including sitemap indexes) into structured entries.
/// </summary>
public sealed class XmlSitemapParser(ILogger<XmlSitemapParser> logger) : ISitemapParser
{
    public SitemapParseResult Parse(Uri sourceUri, string content)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        try
        {
            var document = XDocument.Parse(content, LoadOptions.None);
            var root = document.Root;
            if (root is null)
            {
                logger.LogWarning("Sitemap {Sitemap} was empty", sourceUri);
                return SitemapParseResult.Empty;
            }

            if (IsElement(root, "sitemapindex"))
            {
                return ParseIndex(sourceUri, root);
            }

            if (IsElement(root, "urlset"))
            {
                return ParseUrlSet(sourceUri, root);
            }

            logger.LogWarning(
                "Sitemap {Sitemap} did not contain a recognised root element: {Element}",
                sourceUri,
                root.Name);

            return SitemapParseResult.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse sitemap {Sitemap}", sourceUri);
            return SitemapParseResult.Empty;
        }
    }

    private static SitemapParseResult ParseIndex(Uri sourceUri, XElement root)
    {
        var childUris = root
            .Elements()
            .Where(element => IsElement(element, "sitemap"))
            .Select(element => element.Elements().FirstOrDefault(e => IsElement(e, "loc"))?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ToAbsoluteUri(sourceUri, value!))
            .Where(uri => uri is not null)
            .Select(uri => uri!)
            .Distinct()
            .ToArray();

        return new SitemapParseResult
        {
            ChildSitemaps = childUris
        };
    }

    private static SitemapParseResult ParseUrlSet(Uri sourceUri, XElement root)
    {
        var entries = root
            .Elements()
            .Where(element => IsElement(element, "url"))
            .Select(element =>
            {
                var locValue = element.Elements().FirstOrDefault(e => IsElement(e, "loc"))?.Value;
                if (string.IsNullOrWhiteSpace(locValue))
                {
                    return null;
                }

                var loc = ToAbsoluteUri(sourceUri, locValue);
                if (loc is null)
                {
                    return null;
                }

                var lastMod = ParseDate(element.Elements().FirstOrDefault(e => IsElement(e, "lastmod"))?.Value);
                var changeFreq = element.Elements().FirstOrDefault(e => IsElement(e, "changefreq"))?.Value;
                var priority = ParsePriority(element.Elements().FirstOrDefault(e => IsElement(e, "priority"))?.Value);

                return new SitemapEntry
                {
                    Location = loc,
                    LastModified = lastMod,
                    ChangeFrequency = changeFreq,
                    Priority = priority,
                    HeuristicScore = 0d
                };
            })
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToArray();

        return new SitemapParseResult
        {
            Entries = entries
        };
    }

    private static bool IsElement(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private static double? ParsePriority(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.TryParse(value, out var priority)
            ? priority
            : null;
    }

    private static Uri? ToAbsoluteUri(Uri baseUri, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        if (!Uri.TryCreate(baseUri, value, out var resolved))
        {
            return null;
        }

        return resolved;
    }
}
