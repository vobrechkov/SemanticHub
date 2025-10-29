using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace SemanticHub.IngestionService.Services.Processors;

/// <summary>
/// Scores HTML elements to identify main content areas using Mozilla Readability-style heuristics.
/// Based on the scoring algorithm used by leading content extraction libraries.
/// </summary>
public partial class ContentScorer
{
    // Negative patterns indicate non-content elements (ads, navigation, comments, etc.)
    [GeneratedRegex(
        @"combx|comment|community|disqus|menu|remark|rss|shoutbox|sidebar|" +
        @"sponsor|ad-break|ad-wrapper|advertisement|banner|breadcrumb|" +
        @"agegate|pagination|pager|popup|promo|share|social|" +
        @"cookie|gdpr|newsletter|subscribe|related-posts|recommended|" +
        @"hidden|invisible|hide|removed",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NegativePattern();

    // Positive patterns indicate content elements
    [GeneratedRegex(
        @"article|body|content|entry|hentry|h-entry|main|page|post|text|blog|story|prose",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PositivePattern();

    private readonly ILogger<ContentScorer>? _logger;

    public ContentScorer(ILogger<ContentScorer>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculate a content score for an HTML element based on multiple heuristics.
    /// Higher scores indicate more likely to be main content.
    /// </summary>
    public int CalculateScore(HtmlNode element)
    {
        ArgumentNullException.ThrowIfNull(element);

        int score = 0;

        // 1. Base score from element type (semantic HTML5 elements get higher scores)
        score += GetElementTypeScore(element);

        // 2. Score from class/ID patterns (positive patterns increase, negative decrease)
        score += GetClassIdWeight(element);

        // 3. Content characteristics (paragraph count, text length, commas indicate prose)
        score += GetContentCharacteristicsScore(element);

        // 4. Penalties for high link density (navigation/menus have high link ratios)
        score += GetLinkDensityPenalty(element);

        // 5. Image/paragraph ratio (galleries/image-heavy content get adjusted)
        score += GetImageParagraphRatio(element);

        return score;
    }

    /// <summary>
    /// Calculate the ratio of link text to total text (0.0 to 1.0).
    /// High link density indicates navigation, sidebars, or footer content.
    /// </summary>
    public double CalculateLinkDensity(HtmlNode element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var textContent = GetTextContent(element);
        var textLength = textContent.Length;

        if (textLength == 0)
        {
            return 1.0; // All links if no text
        }

        var linkNodes = element.SelectNodes(".//a");
        if (linkNodes == null || linkNodes.Count == 0)
        {
            return 0.0; // No links
        }

        var linkTextLength = linkNodes.Sum(a => GetTextContent(a).Length);
        return (double)linkTextLength / textLength;
    }

    /// <summary>
    /// Calculate text density: ratio of text to HTML markup.
    /// Higher density indicates content rather than navigational elements.
    /// </summary>
    public double CalculateTextDensity(HtmlNode element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var textLength = GetTextContent(element).Length;
        var htmlLength = element.OuterHtml.Length;

        if (htmlLength == 0)
        {
            return 0.0;
        }

        return (double)textLength / htmlLength;
    }

    /// <summary>
    /// Get confidence score (0.0 to 1.0) for the element being main content.
    /// </summary>
    public double GetConfidenceScore(HtmlNode element)
    {
        var rawScore = CalculateScore(element);

        // Normalize score to 0-1 range
        // Scores typically range from -50 (definitely not content) to 150 (definitely content)
        // We map this to 0.0-1.0 using a sigmoid-like function
        var normalized = Math.Max(0, Math.Min(100, rawScore + 50)) / 100.0;

        return normalized;
    }

    private static int GetElementTypeScore(HtmlNode element)
    {
        return element.Name.ToLowerInvariant() switch
        {
            "article" => 25,      // HTML5 article is strong indicator
            "main" => 20,         // HTML5 main element
            "section" => 15,      // Section might be content
            "div" => 5,           // Generic container
            "p" => 3,             // Paragraph
            "td" => 3,            // Table cell might have content
            "pre" => 3,           // Code blocks are content
            _ => 0
        };
    }

    private int GetClassIdWeight(HtmlNode element)
    {
        int weight = 0;
        var classId = $"{element.GetAttributeValue("class", "")} {element.GetAttributeValue("id", "")}";

        if (string.IsNullOrWhiteSpace(classId))
        {
            return 0;
        }

        // Check for negative patterns
        if (NegativePattern().IsMatch(classId))
        {
            weight -= 25;
            _logger?.LogTrace("Negative pattern match for element: {TagName} class/id={ClassId}", element.Name, classId.Trim());
        }

        // Check for positive patterns
        if (PositivePattern().IsMatch(classId))
        {
            weight += 25;
            _logger?.LogTrace("Positive pattern match for element: {TagName} class/id={ClassId}", element.Name, classId.Trim());
        }

        // Check role attribute
        var role = element.GetAttributeValue("role", "");
        if (role.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            weight += 25;
        }

        return weight;
    }

    private static int GetContentCharacteristicsScore(HtmlNode element)
    {
        int score = 0;
        var textContent = GetTextContent(element);

        // Comma count indicates prose (proper articles have more commas)
        var commaCount = textContent.Count(c => c == ',');
        score += Math.Min(commaCount, 10); // Cap at 10 points

        // Text length (longer content is more likely to be main content)
        // Award points for every 100 characters, cap at 30 points
        score += Math.Min(textContent.Length / 100, 30);

        // Paragraph count (more paragraphs = more likely content)
        var paragraphCount = element.SelectNodes(".//p")?.Count ?? 0;
        score += paragraphCount * 3;

        return score;
    }

    private int GetLinkDensityPenalty(HtmlNode element)
    {
        var linkDensity = CalculateLinkDensity(element);

        // High link density is a strong negative signal
        if (linkDensity > 0.5)
        {
            _logger?.LogTrace("High link density ({Density:F2}) for element: {TagName}", linkDensity, element.Name);
            return -25;
        }

        if (linkDensity > 0.3)
        {
            return -10;
        }

        return 0;
    }

    private static int GetImageParagraphRatio(HtmlNode element)
    {
        var imageCount = element.SelectNodes(".//img")?.Count ?? 0;
        var paragraphCount = element.SelectNodes(".//p")?.Count ?? 0;

        // If there are more images than paragraphs (and more than 1 image), likely a gallery
        if (imageCount > paragraphCount && imageCount > 1)
        {
            return -10;
        }

        return 0;
    }

    private static string GetTextContent(HtmlNode element)
    {
        return (element.InnerText ?? string.Empty).Trim();
    }
}
